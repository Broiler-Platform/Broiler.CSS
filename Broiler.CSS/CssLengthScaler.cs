using System;
using System.Globalization;

namespace Broiler.CSS;

/// <summary>
/// Scales absolute CSS length tokens by a numeric factor (e.g. a used-zoom factor),
/// re-emitting the value with its unit. Pure string arithmetic with no DOM, layout,
/// or host coupling. Promoted from the HtmlBridge zoom-serialization projection.
/// </summary>
public static class CssLengthScaler
{
    private static readonly string[] ScalableUnits = ["px", "pt", "em", "rem"];

    /// <summary>
    /// Scales a single <c>px</c>/<c>pt</c>/<c>em</c>/<c>rem</c> length token by
    /// <paramref name="factor"/>, formatting the result with up to three decimals.
    /// Returns <see langword="false"/> for tokens without one of those units or a
    /// non-numeric magnitude.
    /// </summary>
    public static bool TryScaleLengthToken(string token, double factor, out string scaled)
    {
        scaled = string.Empty;
        var trimmed = token.Trim();
        if (trimmed.Length == 0)
            return false;

        foreach (var unit in ScalableUnits)
        {
            if (!trimmed.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
                continue;

            var numericPart = trimmed[..^unit.Length];
            if (!double.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return false;

            scaled = $"{(number * factor).ToString("0.###", CultureInfo.InvariantCulture)}{unit}";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Scales a serializable CSS length value by <paramref name="factor"/>: a single
    /// length token, or a whitespace-separated 2–4 token list (each scaled). The
    /// <c>auto</c>/<c>none</c>/<c>normal</c> keywords and unrecognized shapes return
    /// <see langword="false"/> (left unscaled by the caller).
    /// </summary>
    public static bool TryScaleValue(string value, double factor, out string scaled)
    {
        scaled = string.Empty;
        var trimmed = value.Trim();
        if (trimmed.Length == 0 ||
            trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("normal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (TryScaleLengthToken(trimmed, factor, out scaled))
            return true;

        var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 2 or 3 or 4)
        {
            var scaledParts = new string[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!TryScaleLengthToken(parts[i], factor, out scaledParts[i]))
                    return false;
            }

            scaled = string.Join(" ", scaledParts);
            return true;
        }

        return false;
    }
}
