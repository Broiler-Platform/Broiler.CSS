using System;
using System.Collections.Generic;
using System.Globalization;

namespace Broiler.CSS;

public static class CssValueParser
{
    private static readonly Dictionary<string, CssColor> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = new(0, 0, 0),
        ["silver"] = new(192, 192, 192),
        ["gray"] = new(128, 128, 128),
        ["white"] = new(255, 255, 255),
        ["maroon"] = new(128, 0, 0),
        ["red"] = new(255, 0, 0),
        ["purple"] = new(128, 0, 128),
        ["fuchsia"] = new(255, 0, 255),
        ["green"] = new(0, 128, 0),
        ["lime"] = new(0, 255, 0),
        ["olive"] = new(128, 128, 0),
        ["yellow"] = new(255, 255, 0),
        ["navy"] = new(0, 0, 128),
        ["blue"] = new(0, 0, 255),
        ["teal"] = new(0, 128, 128),
        ["aqua"] = new(0, 255, 255),
        ["transparent"] = new(0, 0, 0, 0),
    };

    public static CssValue Parse(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return new CssValue(string.Empty);

        if (TryParseColor(value, out var color))
            return new CssValue(value);

        if (TryParseNumeric(value, out var numeric))
            return new CssValue(value, numeric);

        if (IsQuoted(value))
            return new CssValue(value);
        if (StartsFunction(value, "url"))
            return new CssValue(value);
        if (LooksLikeFunction(value))
            return new CssValue(value);
        if (value.StartsWith("--", StringComparison.Ordinal))
            return new CssValue(value);

        return new CssValue(value);
    }

    public static bool TryParseNumeric(string? text, out CssNumericValue value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var input = text.Trim();
        var numberEnd = 0;
        if (numberEnd < input.Length && input[numberEnd] is '+' or '-')
            numberEnd++;

        var sawDigit = false;
        var sawDot = false;
        while (numberEnd < input.Length)
        {
            var character = input[numberEnd];
            if (char.IsDigit(character))
            {
                sawDigit = true;
                numberEnd++;
                continue;
            }

            if (character == '.' && !sawDot)
            {
                sawDot = true;
                numberEnd++;
                continue;
            }

            break;
        }

        if (!sawDigit ||
            !double.TryParse(input[..numberEnd], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        var suffix = input[numberEnd..];
        var unit = ParseUnit(suffix);
        if (unit == CssUnit.Unknown)
            return false;

        value = new CssNumericValue(number, unit);
        return true;
    }

    public static bool TryParseColor(string? text, out CssColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var input = text.Trim();
        if (NamedColors.TryGetValue(input, out color))
            return true;

        if (input[0] == '#')
            return TryParseHexColor(input, out color);
        if (StartsFunction(input, "rgb"))
            return TryParseRgbColor(input, out color);
        if (StartsFunction(input, "rgba"))
            return TryParseRgbColor(input, out color);
        if (StartsFunction(input, "hsl") || StartsFunction(input, "hsla"))
            return TryParseHslColor(input, out color);
        // CSS Color 5: contrast-color(<color>) resolves to black or white, so it is an absolute
        // colour once its argument parses (WPT issue #1491 problem 6).
        if (StartsFunction(input, "contrast-color"))
            return CssContrastColor.TryResolve(input, out color);
        // CSS Color 4 §6 system colours (Canvas, ButtonFace, …) are <color> keywords too.
        if (CssSystemColors.TryResolve(input, out color))
            return true;

        return false;
    }

    private static CssUnit ParseUnit(string suffix) => suffix.ToLowerInvariant() switch
    {
        "" => CssUnit.None,
        "%" => CssUnit.Percent,
        "px" => CssUnit.Px,
        "em" => CssUnit.Em,
        "rem" => CssUnit.Rem,
        "ex" => CssUnit.Ex,
        "ch" => CssUnit.Ch,
        "ic" => CssUnit.Ic,
        "lh" => CssUnit.Lh,
        "rlh" => CssUnit.Rlh,
        "vw" => CssUnit.Vw,
        "vh" => CssUnit.Vh,
        "vmin" => CssUnit.Vmin,
        "vmax" => CssUnit.Vmax,
        "cm" => CssUnit.Cm,
        "mm" => CssUnit.Mm,
        "q" => CssUnit.Q,
        "in" => CssUnit.In,
        "pt" => CssUnit.Pt,
        "pc" => CssUnit.Pc,
        "deg" => CssUnit.Deg,
        "grad" => CssUnit.Grad,
        "rad" => CssUnit.Rad,
        "turn" => CssUnit.Turn,
        "s" => CssUnit.S,
        "ms" => CssUnit.Ms,
        "fr" => CssUnit.Fr,
        _ => CssUnit.Unknown,
    };

    private static bool TryParseHexColor(string input, out CssColor color)
    {
        color = default;
        var hex = input.AsSpan(1);
        if (hex.Length is not (3 or 4 or 6 or 8))
            return false;

        Span<byte> channels = [0, 0, 0, 255];
        var shortForm = hex.Length is 3 or 4;
        var channelCount = hex.Length is 4 or 8 ? 4 : 3;
        for (var index = 0; index < channelCount; index++)
        {
            var part = shortForm ? hex.Slice(index, 1) : hex.Slice(index * 2, 2);
            if (!byte.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
                return false;
            channels[index] = shortForm ? (byte)((parsed << 4) | parsed) : parsed;
        }

        color = new CssColor(channels[0], channels[1], channels[2], channels[3]);
        return true;
    }

    private static bool TryParseRgbColor(string input, out CssColor color)
    {
        color = default;
        if (!TryGetFunctionBody(input, out var body))
            return false;

        var normalized = body.Replace("/", ",", StringComparison.Ordinal);
        var parts = normalized.Contains(',')
            ? normalized.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (3 or 4))
            return false;

        if (!TryParseRgbChannel(parts[0], out var red) ||
            !TryParseRgbChannel(parts[1], out var green) ||
            !TryParseRgbChannel(parts[2], out var blue))
        {
            return false;
        }

        byte alpha = 255;
        if (parts.Length == 4 && !TryParseAlpha(parts[3], out alpha))
            return false;

        color = new CssColor(red, green, blue, alpha);
        return true;
    }

    private static bool TryParseHslColor(string input, out CssColor color)
    {
        color = default;
        if (!TryGetFunctionBody(input, out var body))
            return false;

        var normalized = body.Replace("/", ",", StringComparison.Ordinal);
        var parts = normalized.Contains(',')
            ? normalized.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (3 or 4) ||
            !TryParseHue(parts[0], out var hue) ||
            !TryParsePercentage(parts[1], out var saturation) ||
            !TryParsePercentage(parts[2], out var lightness))
        {
            return false;
        }

        byte alpha = 255;
        if (parts.Length == 4 && !TryParseAlpha(parts[3], out alpha))
            return false;

        var chroma = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var segment = hue / 60d;
        var x = chroma * (1 - Math.Abs((segment % 2) - 1));
        var (r, g, b) = segment switch
        {
            < 1 => (chroma, x, 0d),
            < 2 => (x, chroma, 0d),
            < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma),
            < 5 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };
        var m = lightness - (chroma / 2);
        color = new CssColor(ToByte(r + m), ToByte(g + m), ToByte(b + m), alpha);
        return true;
    }

    private static bool TryParseRgbChannel(string text, out byte value)
    {
        value = 0;
        if (text.EndsWith('%') &&
            double.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            value = ToByte(Math.Clamp(percent, 0, 100) / 100d);
            return true;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return false;
        value = (byte)Math.Round(Math.Clamp(number, 0, 255));
        return true;
    }

    private static bool TryParseAlpha(string text, out byte value)
    {
        value = 255;
        if (text.EndsWith('%') &&
            double.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            value = ToByte(Math.Clamp(percent, 0, 100) / 100d);
            return true;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return false;
        value = number <= 1 ? ToByte(Math.Clamp(number, 0, 1)) : (byte)Math.Round(Math.Clamp(number, 0, 255));
        return true;
    }

    private static bool TryParseHue(string text, out double hue)
    {
        hue = 0;
        var value = Parse(text);
        if (value.Numeric is not { } numeric)
            return false;

        hue = numeric.Unit switch
        {
            CssUnit.None or CssUnit.Deg => numeric.Number,
            CssUnit.Grad => numeric.Number * 0.9,
            CssUnit.Rad => numeric.Number * (180d / Math.PI),
            CssUnit.Turn => numeric.Number * 360d,
            _ => double.NaN,
        };
        if (double.IsNaN(hue))
            return false;
        hue = ((hue % 360) + 360) % 360;
        return true;
    }

    private static bool TryParsePercentage(string text, out double value)
    {
        value = 0;
        if (!TryParseNumeric(text, out var numeric) || numeric.Unit != CssUnit.Percent)
            return false;
        value = Math.Clamp(numeric.Number / 100d, 0, 1);
        return true;
    }

    private static bool TryGetFunctionBody(string input, out string body)
    {
        body = string.Empty;
        var open = input.IndexOf('(');
        if (open <= 0 || input[^1] != ')')
            return false;
        body = input[(open + 1)..^1].Trim();
        return true;
    }

    private static bool IsQuoted(string value) =>
        value.Length >= 2 &&
        ((value[0] == '"' && value[^1] == '"') ||
         (value[0] == '\'' && value[^1] == '\''));

    private static bool StartsFunction(string value, string name) =>
        value.StartsWith(name + "(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')');

    private static bool LooksLikeFunction(string value)
    {
        var open = value.IndexOf('(');
        return open > 0 && value.EndsWith(')');
    }

    private static byte ToByte(double normalized) =>
        (byte)Math.Round(Math.Clamp(normalized, 0, 1) * 255);
}
