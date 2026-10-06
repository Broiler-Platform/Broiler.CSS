using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Broiler.CSS;

/// <summary>
/// CSS Color 4/5 colour functions — <c>hwb()</c>, <c>lab()</c>, <c>lch()</c>, <c>oklab()</c>,
/// <c>oklch()</c>, <c>color()</c> and <c>color-mix()</c> — resolved to the <c>rgba()</c> form
/// <see cref="CssValueParser.TryParseColor"/> already reads, plus the space-separated
/// <c>/ &lt;alpha&gt;</c> spelling of <c>rgb()</c>/<c>hsl()</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a normaliser rather than a parser.</b> A renderer calls this on a declaration
/// value before handing it to <see cref="CssValueParser.TryParseColor"/>, and the rewritten
/// <c>rgba(…)</c> is something that parser already understands. The conversion is not folded into
/// <see cref="CssValueParser.TryParseColor"/> itself because it resolves a <c>color-mix()</c>
/// operand or a relative colour's origin through that same parser: calling this from inside it
/// would recurse, and doing that safely means carrying the supplied <c>currentcolor</c>, the nesting
/// limit and the rounding below into the parser's own contract. That is a separate change; this
/// type keeps the behaviour it had in Broiler.Layout, where it was written.
/// </para>
/// <para>
/// <b>What it was fixing.</b> Only <c>rgb()</c>, <c>hsl()</c>, hex and the named colours parsed.
/// A renderer that folds an unrecognised value to <i>opaque black</i> painted
/// <c>background: oklch(0.6 0.12 200)</c> as a solid black rectangle rather than the colour asked
/// for, and, worse, did the same with any value the parser merely did not know. A colour stop inside
/// a gradient was dropped outright instead, which is how WPT
/// <c>css-images/gradient/gradient-single-stop-none-interpolation</c> came to render blank — and to
/// render blank on <i>both</i> sides, so it passed its own reference at 100% while painting none of
/// what it asks for.
/// </para>
/// <para>
/// <b>What needs the caller.</b> <c>currentcolor</c>, as a <c>color-mix()</c> operand or a relative
/// colour's origin, resolves only against a <c>color</c> the caller passes in; with none, the
/// function is left as written. <c>color-mix()</c> mixes in the rectangular spaces exactly and in the
/// polar ones through their rectangular equivalent with the requested hue arc.
/// </para>
/// </remarks>
public static class CssColor4
{
    /// <summary>
    /// Rewrites every CSS Color 4 colour function inside <paramref name="value"/> to
    /// <c>rgba(r, g, b, a)</c>, leaving everything else — including colours the existing parser
    /// already reads — byte-identical.
    /// </summary>
    /// <remarks>
    /// Works over a whole declaration value rather than a single colour, because the values that
    /// need it most are compound: a gradient's stop list, a <c>box-shadow</c>'s offsets-then-colour,
    /// a multi-layer <c>background</c>. Returns the original string instance when nothing matched,
    /// so the common case allocates nothing.
    /// </remarks>
    public static string NormalizeColorFunctions(string? value) =>
        NormalizeColorFunctions(value, currentColor: null);

    /// <param name="currentColor">
    /// The element's resolved <c>color</c>, as any spelling the base parser reads, so a
    /// <c>color-mix()</c> or relative colour naming <c>currentcolor</c> can be resolved. Null leaves
    /// such a function unconverted rather than guessing — the caller that has no element has no
    /// basis to resolve it against, and inventing black would be worse than leaving it alone.
    /// </param>
    /// <inheritdoc cref="NormalizeColorFunctions(string?)"/>
    public static string NormalizeColorFunctions(string? value, string? currentColor)
    {
        if (string.IsNullOrEmpty(value) || !MayContainColorFunction(value))
            return value ?? string.Empty;

        var previousCurrentColor = _currentColor;
        _currentColor = currentColor;
        try
        {
            return NormalizeCore(value);
        }
        finally
        {
            _currentColor = previousCurrentColor;
        }
    }

    /// <summary>
    /// The <c>currentcolor</c> substitution in force for the conversion running on this thread.
    /// Thread-static because callers resolve styles in parallel (Broiler.Layout's style recalc
    /// does), and passed this way rather than threaded
    /// through five signatures for one operand form that only <c>color-mix()</c> and the relative
    /// syntax admit.
    /// </summary>
    [ThreadStatic]
    private static string? _currentColor;

    private static string NormalizeCore(string value)
    {
        StringBuilder? output = null;
        var copiedTo = 0;
        var index = 0;
        while (index < value.Length)
        {
            if (!TryReadFunctionName(value, index, out var name, out var openParen) ||
                !IsConvertibleFunction(name))
            {
                index++;
                continue;
            }

            if (!TryFindMatchingParen(value, openParen, out var closeParen) ||
                !TryParse(value.AsSpan(index, closeParen - index + 1).ToString(), out var rgba))
            {
                // Not a colour after all (an unsupported colour space, an unreadable component).
                // Leave the text exactly as written and keep scanning past it, so a later colour in
                // the same value is still converted.
                index = closeParen > index ? closeParen + 1 : index + 1;
                continue;
            }

            output ??= new StringBuilder(value.Length + 16);
            output.Append(value, copiedTo, index - copiedTo);
            output.Append(Format(rgba));
            copiedTo = closeParen + 1;
            index = copiedTo;
        }

        if (output is null)
            return value;

        output.Append(value, copiedTo, value.Length - copiedTo);
        return output.ToString();
    }

    /// <summary>
    /// Parses one complete colour function to straight (non-premultiplied) sRGB, with the channels
    /// in 0–255 and alpha in 0–1. Returns false for anything this does not convert, including the
    /// <c>rgb()</c>/<c>hsl()</c>/hex/named forms the existing parser already handles — with the one
    /// exception of the modern <c>/ &lt;alpha&gt;</c> spelling, which it does not.
    /// </summary>
    public static bool TryParse(string? text, out (double R, double G, double B, double A) rgba)
    {
        rgba = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // A relative colour's origin and a color-mix()'s operands are themselves colours, so both
        // recurse — and the input is arbitrary author CSS, which can nest them as deeply as it
        // likes. A stack overflow cannot be caught in .NET and would take the process with it, so
        // the depth is bounded here rather than trusted. Real content nests one or two deep; the
        // limit only ever refuses a colour, which every caller already handles.
        if (_depth >= MaxNestingDepth)
            return false;

        var input = text.Trim();
        if (!TryReadFunctionName(input, 0, out var name, out var openParen) ||
            !TryFindMatchingParen(input, openParen, out var closeParen) ||
            closeParen != input.Length - 1)
        {
            return false;
        }

        var body = input[(openParen + 1)..closeParen];

        _depth++;
        try
        {
            return StartsWithKeyword(body, "from")
                ? TryParseRelative(name, body, out rgba)
                : Dispatch(name, body, out rgba);
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>How deeply a colour may nest another colour inside itself before this declines.</summary>
    private const int MaxNestingDepth = 16;

    /// <summary>Nesting depth of the conversion running on this thread; thread-static for the same
    /// reason <see cref="_currentColor"/> is.</summary>
    [ThreadStatic]
    private static int _depth;

    private static bool Dispatch(string name, string body, out (double R, double G, double B, double A) rgba)
    {
        rgba = default;
        return name switch
        {
            "hwb" => TryParseHwb(body, out rgba),
            "lab" => TryParseLabLike(body, isOk: false, polar: false, out rgba),
            "lch" => TryParseLabLike(body, isOk: false, polar: true, out rgba),
            "oklab" => TryParseLabLike(body, isOk: true, polar: false, out rgba),
            "oklch" => TryParseLabLike(body, isOk: true, polar: true, out rgba),
            "color" => TryParseColorFunction(body, out rgba),
            "color-mix" => TryParseColorMix(body, out rgba),
            "rgb" or "rgba" => TryParseModernRgb(body, out rgba),
            "hsl" or "hsla" => TryParseModernHsl(body, out rgba),
            _ => false,
        };
    }

    // ------------------------------------------------------- relative colour syntax

    /// <summary>
    /// CSS Color 5 §4: <c>&lt;function&gt;(from &lt;color&gt; [&lt;space&gt;] c1 c2 c3[ / a])</c>,
    /// where the origin colour is converted into the destination space and its channels become
    /// keywords the components may name, arithmetic on, or ignore.
    /// </summary>
    /// <remarks>
    /// The origin is resolved with the same machinery as any other colour, so it may itself be a
    /// nested function, a <c>color-mix()</c>, <c>currentcolor</c>, a hex or a name. What the
    /// keywords are bound to depends entirely on the *destination*: <c>color(from red display-p3 r
    /// g b)</c> binds red's display-p3 coordinates, not its sRGB ones, which is the whole point of
    /// the syntax and the part that cannot be faked by string substitution.
    /// </remarks>
    private static bool TryParseRelative(
        string name, string body, out (double R, double G, double B, double A) rgba)
    {
        rgba = default;

        var rest = body.AsSpan().TrimStart();
        rest = rest[4..].TrimStart(); // "from"

        if (!TrySplitOriginColor(rest.ToString(), out var originToken, out var remainder) ||
            !TryResolveAnyColor(originToken, out var origin))
        {
            return false;
        }

        var space = name;
        if (name == "color")
        {
            var spaceSplit = remainder.AsSpan().TrimStart();
            var spaceEnd = 0;
            while (spaceEnd < spaceSplit.Length && !char.IsWhiteSpace(spaceSplit[spaceEnd]))
                spaceEnd++;
            if (spaceEnd == 0)
                return false;
            space = spaceSplit[..spaceEnd].ToString().ToLowerInvariant();
            remainder = spaceSplit[spaceEnd..].ToString();
        }

        if (!TryBindOriginChannels(space, origin, out var bindings, out var channelNames))
            return false;

        if (!TrySplitComponents(remainder, out var parts, out var alphaToken) || parts.Count != 3)
            return false;

        var resolved = new double[3];
        for (var index = 0; index < 3; index++)
        {
            if (!TryRelativeComponent(parts[index], bindings, channelNames[index], out resolved[index]))
                return false;
        }

        // An omitted alpha inherits the origin's, which is the one place the syntax defaults to
        // something other than the property's initial value (§4.1).
        double alpha;
        if (alphaToken is null)
            alpha = origin.A;
        else if (!TryRelativeComponent(alphaToken, bindings, "alpha", out alpha))
            return false;

        return TryResolveAnyColor(BuildAbsolute(name, space, resolved, Clamp(alpha, 0, 1)), out rgba);
    }

    /// <summary>Reads the origin colour off the front of a relative colour's body, keeping a nested
    /// function whole rather than splitting it at its first space.</summary>
    private static bool TrySplitOriginColor(string text, out string origin, out string remainder)
    {
        origin = string.Empty;
        remainder = string.Empty;

        var index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;

        var start = index;
        var depth = 0;
        while (index < text.Length)
        {
            var character = text[index];
            if (character == '(')
                depth++;
            else if (character == ')')
                depth--;
            else if (depth == 0 && char.IsWhiteSpace(character))
                break;
            index++;
        }

        if (index == start)
            return false;

        origin = text[start..index];
        remainder = text[index..];
        return true;
    }

    /// <summary>
    /// The origin colour's coordinates in the destination space, keyed by the channel names that
    /// space exposes, plus <c>alpha</c>.
    /// </summary>
    private static bool TryBindOriginChannels(
        string space,
        (double R, double G, double B, double A) origin,
        out Dictionary<string, double> bindings,
        out string[] channelNames)
    {
        bindings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        channelNames = Array.Empty<string>();

        double[] channels;
        switch (space)
        {
            case "rgb":
            case "rgba":
                channelNames = new[] { "r", "g", "b" };
                channels = new[] { origin.R, origin.G, origin.B };
                break;
            case "hsl":
            case "hsla":
            {
                var (h, s, l) = RgbToHsl(origin.R / 255.0, origin.G / 255.0, origin.B / 255.0);
                channelNames = new[] { "h", "s", "l" };
                channels = new[] { h, s * 100.0, l * 100.0 };
                break;
            }

            case "hwb":
            {
                var (h, w, b) = RgbToHwb(origin.R / 255.0, origin.G / 255.0, origin.B / 255.0);
                channelNames = new[] { "h", "w", "b" };
                channels = new[] { h, w * 100.0, b * 100.0 };
                break;
            }

            case "lab":
            case "oklab":
                channelNames = new[] { "l", "a", "b" };
                channels = ToMixSpace(space, origin);
                break;
            case "lch":
            case "oklch":
                channelNames = new[] { "l", "c", "h" };
                channels = ToMixSpace(space, origin);
                break;
            case "xyz":
            case "xyz-d50":
            case "xyz-d65":
                channelNames = new[] { "x", "y", "z" };
                channels = ToMixSpace(space, origin);
                break;
            case "srgb":
            case "srgb-linear":
            case "display-p3":
            case "display-p3-linear":
            case "a98-rgb":
            case "prophoto-rgb":
            case "rec2020":
                channelNames = new[] { "r", "g", "b" };
                channels = SrgbToPredefined(space, origin);
                break;
            default:
                return false;
        }

        for (var index = 0; index < 3; index++)
            bindings[channelNames[index]] = channels[index];
        bindings["alpha"] = origin.A;
        return true;
    }

    /// <summary>The inverse of the <c>color()</c> decoding: sRGB bytes back out into one of the
    /// predefined spaces, so a relative colour can read the origin's coordinates there.</summary>
    private static double[] SrgbToPredefined(string space, (double R, double G, double B, double A) color)
    {
        var linear = (SrgbToLinear(color.R / 255.0), SrgbToLinear(color.G / 255.0), SrgbToLinear(color.B / 255.0));
        switch (space)
        {
            case "srgb":
                return new[] { color.R / 255.0, color.G / 255.0, color.B / 255.0 };
            case "srgb-linear":
                return new[] { linear.Item1, linear.Item2, linear.Item3 };
            case "display-p3-linear":
                return AsArray(Transform(Invert(DisplayP3ToXyzD65), LinearSrgbToXyzD65(linear)));
            case "display-p3":
            {
                var p3 = Transform(Invert(DisplayP3ToXyzD65), LinearSrgbToXyzD65(linear));
                return new[] { LinearToSrgb(p3.Item1), LinearToSrgb(p3.Item2), LinearToSrgb(p3.Item3) };
            }

            case "a98-rgb":
            {
                var a98 = Transform(Invert(A98ToXyzD65), LinearSrgbToXyzD65(linear));
                return new[] { LinearToA98(a98.Item1), LinearToA98(a98.Item2), LinearToA98(a98.Item3) };
            }

            case "prophoto-rgb":
            {
                var pro = Transform(Invert(ProPhotoToXyzD50), XyzD65ToD50(LinearSrgbToXyzD65(linear)));
                return new[]
                {
                    LinearToProPhoto(pro.Item1), LinearToProPhoto(pro.Item2), LinearToProPhoto(pro.Item3),
                };
            }

            case "rec2020":
            {
                var rec = Transform(Invert(Rec2020ToXyzD65), LinearSrgbToXyzD65(linear));
                return new[]
                {
                    LinearToRec2020(rec.Item1), LinearToRec2020(rec.Item2), LinearToRec2020(rec.Item3),
                };
            }

            default:
                return new[] { color.R / 255.0, color.G / 255.0, color.B / 255.0 };
        }
    }

    /// <summary>
    /// One component of a relative colour: a channel keyword, a plain number or percentage,
    /// <c>none</c>, or a <c>calc()</c> over those.
    /// </summary>
    private static bool TryRelativeComponent(
        string token, Dictionary<string, double> bindings, string channelName, out double value)
    {
        value = 0;
        if (string.IsNullOrEmpty(token))
            return false;

        if (bindings.TryGetValue(token, out value))
            return true;

        if (token.StartsWith("calc(", StringComparison.OrdinalIgnoreCase) && token.EndsWith(')'))
            return TryEvaluateCalc(token[5..^1], bindings, out value);

        // A percentage in a relative colour is relative to that channel's own reference range, the
        // same one the absolute notation uses — which is why the channel name has to come along.
        return TryComponent(token, PercentReferenceFor(channelName, bindings), out value);
    }

    /// <summary>What <c>100%</c> means for a named channel, so a percentage component resolves the
    /// way the absolute notation would resolve it.</summary>
    private static double PercentReferenceFor(string channelName, Dictionary<string, double> bindings)
        => channelName switch
        {
            "alpha" => 1.0,
            "s" or "w" => 100.0,
            // Lightness: 100 in lab/lch, 1 in oklab/oklch — told apart by the magnitude the origin
            // bound, since both spaces name the channel `l`.
            "l" => bindings.TryGetValue("l", out var l) && Math.Abs(l) > 1.5 ? 100.0 : 1.0,
            "c" => 150.0,
            "a" => 125.0,
            "h" or "x" or "y" or "z" => 1.0,
            _ => 1.0,
        };

    /// <summary>
    /// A <c>calc()</c> over the bound channel keywords: the four operators, parentheses and unary
    /// minus. Deliberately no units, lengths or nested CSS functions — a relative colour's
    /// arithmetic is on plain numbers, and anything richer is left unconverted rather than
    /// half-evaluated.
    /// </summary>
    private static bool TryEvaluateCalc(
        string expression, Dictionary<string, double> bindings, out double value)
    {
        var position = 0;
        value = 0;
        if (!TryParseSum(expression, bindings, ref position, out var result))
            return false;

        SkipSpace(expression, ref position);
        if (position != expression.Length)
            return false;

        value = result;
        return true;
    }

    private static void SkipSpace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
            position++;
    }

    private static bool TryParseSum(
        string text, Dictionary<string, double> bindings, ref int position, out double value)
    {
        if (!TryParseProduct(text, bindings, ref position, out value))
            return false;

        while (true)
        {
            SkipSpace(text, ref position);
            if (position >= text.Length || (text[position] != '+' && text[position] != '-'))
                return true;

            var op = text[position++];
            if (!TryParseProduct(text, bindings, ref position, out var operand))
                return false;
            value = op == '+' ? value + operand : value - operand;
        }
    }

    private static bool TryParseProduct(
        string text, Dictionary<string, double> bindings, ref int position, out double value)
    {
        if (!TryParseUnary(text, bindings, ref position, out value))
            return false;

        while (true)
        {
            SkipSpace(text, ref position);
            if (position >= text.Length || (text[position] != '*' && text[position] != '/'))
                return true;

            var op = text[position++];
            if (!TryParseUnary(text, bindings, ref position, out var operand))
                return false;
            if (op == '/' && operand == 0)
                return false;
            value = op == '*' ? value * operand : value / operand;
        }
    }

    private static bool TryParseUnary(
        string text, Dictionary<string, double> bindings, ref int position, out double value)
    {
        value = 0;
        SkipSpace(text, ref position);
        if (position >= text.Length)
            return false;

        if (text[position] == '-')
        {
            position++;
            if (!TryParseUnary(text, bindings, ref position, out value))
                return false;
            value = -value;
            return true;
        }

        if (text[position] == '+')
        {
            position++;
            return TryParseUnary(text, bindings, ref position, out value);
        }

        if (text[position] == '(')
        {
            position++;
            if (!TryParseSum(text, bindings, ref position, out value))
                return false;
            SkipSpace(text, ref position);
            if (position >= text.Length || text[position] != ')')
                return false;
            position++;
            return true;
        }

        var start = position;
        while (position < text.Length &&
            (char.IsLetterOrDigit(text[position]) || text[position] is '.' or '%' or '-'))
        {
            position++;
        }

        if (position == start)
            return false;

        var token = text[start..position];
        if (bindings.TryGetValue(token, out value))
            return true;

        return double.TryParse(
            token.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Re-spells a resolved relative colour as the equivalent absolute one, so the single
    /// conversion path above handles it rather than a second, parallel one here.</summary>
    private static string BuildAbsolute(string name, string space, double[] c, double alpha)
    {
        static string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        return name switch
        {
            "rgb" or "rgba" => $"rgb({N(c[0])} {N(c[1])} {N(c[2])} / {N(alpha)})",
            "hsl" or "hsla" => $"hsl({N(c[0])} {N(c[1])}% {N(c[2])}% / {N(alpha)})",
            "hwb" => $"hwb({N(c[0])} {N(c[1])}% {N(c[2])}% / {N(alpha)})",
            "color" => $"color({space} {N(c[0])} {N(c[1])} {N(c[2])} / {N(alpha)})",
            _ => $"{name}({N(c[0])} {N(c[1])} {N(c[2])} / {N(alpha)})",
        };
    }

    private static double LinearToA98(double channel)
    {
        var sign = channel < 0 ? -1.0 : 1.0;
        return sign * Math.Pow(Math.Abs(channel), 256.0 / 563.0);
    }

    private static double LinearToProPhoto(double channel)
    {
        var magnitude = Math.Abs(channel);
        var sign = channel < 0 ? -1.0 : 1.0;
        return magnitude <= 1.0 / 512.0 ? channel * 16.0 : sign * Math.Pow(magnitude, 1 / 1.8);
    }

    private static double LinearToRec2020(double channel)
    {
        const double Alpha = 1.09929682680944;
        const double Beta = 0.018053968510807;
        var magnitude = Math.Abs(channel);
        var sign = channel < 0 ? -1.0 : 1.0;
        return magnitude < Beta
            ? channel * 4.5
            : sign * ((Alpha * Math.Pow(magnitude, 0.45)) - (Alpha - 1));
    }

    /// <summary>Inverts a 3×3 primary matrix, so each predefined space needs only its forward
    /// constants and the reverse direction cannot drift out of step with them.</summary>
    private static double[,] Invert(double[,] m)
    {
        var determinant =
            (m[0, 0] * ((m[1, 1] * m[2, 2]) - (m[1, 2] * m[2, 1]))) -
            (m[0, 1] * ((m[1, 0] * m[2, 2]) - (m[1, 2] * m[2, 0]))) +
            (m[0, 2] * ((m[1, 0] * m[2, 1]) - (m[1, 1] * m[2, 0])));

        var inverse = new double[3, 3];
        inverse[0, 0] = ((m[1, 1] * m[2, 2]) - (m[1, 2] * m[2, 1])) / determinant;
        inverse[0, 1] = ((m[0, 2] * m[2, 1]) - (m[0, 1] * m[2, 2])) / determinant;
        inverse[0, 2] = ((m[0, 1] * m[1, 2]) - (m[0, 2] * m[1, 1])) / determinant;
        inverse[1, 0] = ((m[1, 2] * m[2, 0]) - (m[1, 0] * m[2, 2])) / determinant;
        inverse[1, 1] = ((m[0, 0] * m[2, 2]) - (m[0, 2] * m[2, 0])) / determinant;
        inverse[1, 2] = ((m[0, 2] * m[1, 0]) - (m[0, 0] * m[1, 2])) / determinant;
        inverse[2, 0] = ((m[1, 0] * m[2, 1]) - (m[1, 1] * m[2, 0])) / determinant;
        inverse[2, 1] = ((m[0, 1] * m[2, 0]) - (m[0, 0] * m[2, 1])) / determinant;
        inverse[2, 2] = ((m[0, 0] * m[1, 1]) - (m[0, 1] * m[1, 0])) / determinant;
        return inverse;
    }

    /// <summary>The canonical <c>rgba()</c> spelling of a resolved colour, as the existing parser
    /// reads it: integer channels, alpha to three decimals, invariant culture.</summary>
    private static string Format((double R, double G, double B, double A) rgba)
    {
        var r = (int)Math.Round(Clamp(rgba.R, 0, 255), MidpointRounding.AwayFromZero);
        var g = (int)Math.Round(Clamp(rgba.G, 0, 255), MidpointRounding.AwayFromZero);
        var b = (int)Math.Round(Clamp(rgba.B, 0, 255), MidpointRounding.AwayFromZero);
        var a = Clamp(rgba.A, 0, 1);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"rgba({r}, {g}, {b}, {a.ToString("0.###", CultureInfo.InvariantCulture)})");
    }

    // ---------------------------------------------------------------- scanning

    /// <summary>A cheap reject for the overwhelmingly common value that holds no colour function at
    /// all, so the getters this runs behind stay allocation-free.</summary>
    private static bool MayContainColorFunction(string value)
    {
        if (value.IndexOf('(') < 0)
            return false;

        foreach (var marker in FunctionMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Relative colour syntax turns any of the functions into a convertible one, including the
        // two below that are otherwise the base parser's business.
        if (value.Contains("from", StringComparison.OrdinalIgnoreCase))
            return true;

        // `rgb(… / a)` and `hsl(… / a)` are convertible only in their slash-alpha spelling; a slash
        // inside the value is the cheapest thing that can distinguish them.
        return value.IndexOf('/') >= 0 &&
            (value.Contains("rgb", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("hsl", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] FunctionMarkers =
    {
        "hwb(", "lab(", "lch(", "oklab(", "oklch(", "color(", "color-mix(",
    };

    private static bool IsConvertibleFunction(string name) => name switch
    {
        "hwb" or "lab" or "lch" or "oklab" or "oklch" or "color" or "color-mix" => true,
        // Converted only when the modern slash-alpha form is used; TryParse re-checks and declines
        // the legacy comma form, which the existing parser reads correctly on its own.
        "rgb" or "rgba" or "hsl" or "hsla" => true,
        _ => false,
    };

    /// <summary>Reads the CSS identifier ending at the <c>(</c> that starts at or after
    /// <paramref name="start"/>, rejecting a match that is only the tail of a longer identifier (so
    /// the <c>lab</c> inside <c>oklab</c> is never taken for a function of its own).</summary>
    private static bool TryReadFunctionName(string value, int start, out string name, out int openParen)
    {
        name = string.Empty;
        openParen = -1;

        if (start >= value.Length || !IsIdentStart(value[start]))
            return false;
        if (start > 0 && (IsIdentPart(value[start - 1]) || value[start - 1] == '-'))
            return false;

        var end = start;
        while (end < value.Length && (IsIdentPart(value[end]) || value[end] == '-'))
            end++;
        if (end >= value.Length || value[end] != '(')
            return false;

        name = value[start..end].ToLowerInvariant();
        openParen = end;
        return true;
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool TryFindMatchingParen(string value, int openParen, out int closeParen)
    {
        var depth = 0;
        for (var index = openParen; index < value.Length; index++)
        {
            if (value[index] == '(')
                depth++;
            else if (value[index] == ')' && --depth == 0)
            {
                closeParen = index;
                return true;
            }
        }

        closeParen = -1;
        return false;
    }

    private static bool StartsWithKeyword(string body, string keyword)
    {
        var trimmed = body.AsSpan().TrimStart();
        return trimmed.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) &&
            (trimmed.Length == keyword.Length || char.IsWhiteSpace(trimmed[keyword.Length]));
    }

    /// <summary>
    /// Splits a function body into its component list and its optional <c>/ &lt;alpha&gt;</c> tail,
    /// tolerating the legacy comma separators as well as the modern whitespace ones and ignoring
    /// separators nested inside a sub-function.
    /// </summary>
    private static bool TrySplitComponents(string body, out List<string> components, out string? alpha)
    {
        components = new List<string>();
        alpha = null;

        var depth = 0;
        var current = new StringBuilder();
        var afterSlash = false;

        void Flush(List<string> into, ref StringBuilder buffer)
        {
            var token = buffer.ToString().Trim();
            if (token.Length > 0)
                into.Add(token);
            buffer = new StringBuilder();
        }

        var alphaParts = new List<string>();
        foreach (var character in body)
        {
            if (character == '(')
                depth++;
            else if (character == ')')
                depth--;

            if (depth == 0 && character == '/')
            {
                if (afterSlash)
                    return false;
                Flush(afterSlash ? alphaParts : components, ref current);
                afterSlash = true;
                continue;
            }

            if (depth == 0 && (character == ',' || char.IsWhiteSpace(character)))
            {
                Flush(afterSlash ? alphaParts : components, ref current);
                continue;
            }

            current.Append(character);
        }

        Flush(afterSlash ? alphaParts : components, ref current);

        if (afterSlash)
        {
            if (alphaParts.Count != 1)
                return false;
            alpha = alphaParts[0];
        }

        return true;
    }

    // ---------------------------------------------------------------- components

    /// <summary>
    /// A component value as a number, with <c>none</c> reading as 0. CSS Color 4 §4.4 makes a
    /// missing component behave as zero everywhere it is not being interpolated against a present
    /// one, which is exactly what
    /// <c>css-images/gradient/gradient-single-stop-none-interpolation</c> asserts.
    /// </summary>
    private static bool TryComponent(string? token, double percentReference, out double value)
    {
        value = 0;
        if (string.IsNullOrEmpty(token))
            return false;
        if (token.Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;

        if (token.EndsWith('%'))
        {
            if (!double.TryParse(
                    token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            {
                return false;
            }

            value = percent / 100.0 * percentReference;
            return true;
        }

        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>An <c>&lt;angle&gt;</c> or bare number in degrees, with <c>none</c> reading as 0.</summary>
    private static bool TryAngle(string? token, out double degrees)
    {
        degrees = 0;
        if (string.IsNullOrEmpty(token))
            return false;
        if (token.Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;

        var (suffix, perTurn) = token.EndsWith("deg", StringComparison.OrdinalIgnoreCase) ? ("deg", 360.0)
            : token.EndsWith("grad", StringComparison.OrdinalIgnoreCase) ? ("grad", 400.0)
            : token.EndsWith("rad", StringComparison.OrdinalIgnoreCase) ? ("rad", 2 * Math.PI)
            : token.EndsWith("turn", StringComparison.OrdinalIgnoreCase) ? ("turn", 1.0)
            : (string.Empty, 360.0);

        var number = suffix.Length == 0 ? token : token[..^suffix.Length];
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
            return false;

        degrees = raw / perTurn * 360.0;
        return true;
    }

    /// <summary>Alpha as 0–1, accepting both the number and percentage spellings; a missing tail is
    /// fully opaque.</summary>
    private static bool TryAlpha(string? token, out double alpha)
    {
        alpha = 1;
        if (token is null)
            return true;
        if (!TryComponent(token, percentReference: 1.0, out alpha))
            return false;
        alpha = Clamp(alpha, 0, 1);
        return true;
    }

    // ---------------------------------------------------------------- functions

    private static bool TryParseHwb(string body, out (double R, double G, double B, double A) rgba)
    {
        rgba = default;
        if (!TrySplitComponents(body, out var parts, out var alphaToken) || parts.Count != 3)
            return false;
        if (!TryAngle(parts[0], out var hue) ||
            !TryComponent(parts[1], 100, out var whiteness) ||
            !TryComponent(parts[2], 100, out var blackness) ||
            !TryAlpha(alphaToken, out var alpha))
        {
            return false;
        }

        whiteness /= 100.0;
        blackness /= 100.0;

        // CSS Color 4 §7: whiteness and blackness that together reach or exceed 1 name a grey, and
        // the hue drops out entirely.
        if (whiteness + blackness >= 1)
        {
            var grey = whiteness / (whiteness + blackness) * 255.0;
            rgba = (grey, grey, grey, alpha);
            return true;
        }

        var (r, g, b) = HslToRgb(hue, 1, 0.5);
        var scale = 1 - whiteness - blackness;
        rgba = (
            (r * scale + whiteness) * 255.0,
            (g * scale + whiteness) * 255.0,
            (b * scale + whiteness) * 255.0,
            alpha);
        return true;
    }

    /// <summary>
    /// The four CIE/OKLab functions, which differ only in their component scales and in whether the
    /// chroma/hue pair has to be unwound into rectangular a/b first.
    /// </summary>
    private static bool TryParseLabLike(
        string body, bool isOk, bool polar, out (double R, double G, double B, double A) rgba)
    {
        rgba = default;
        if (!TrySplitComponents(body, out var parts, out var alphaToken) || parts.Count != 3)
            return false;

        // Percentage references, CSS Color 4 §8–9: lightness spans the full range; a/b are ±125 for
        // lab and ±0.4 for oklab; chroma is 150 and 0.4 respectively.
        var lightnessReference = isOk ? 1.0 : 100.0;
        var axisReference = isOk ? 0.4 : 125.0;
        var chromaReference = isOk ? 0.4 : 150.0;

        if (!TryComponent(parts[0], lightnessReference, out var lightness) ||
            !TryAlpha(alphaToken, out var alpha))
        {
            return false;
        }

        // CSS Color 4 §8.2/§9.2 clamp lightness to the reference range, and the tests say so
        // directly: oklab-l-over-1-1 asserts `oklab(1.5 0.5 0.2)` renders as `oklab(1 0.5 0.2)`.
        // Without the clamp an over-1 lightness reached the gamut mapper's "L >= 1 is white"
        // short-circuit while the in-range twin did not, and the two painted different colours.
        lightness = Clamp(lightness, 0, lightnessReference);

        double a, b;
        if (polar)
        {
            if (!TryComponent(parts[1], chromaReference, out var chroma) ||
                !TryAngle(parts[2], out var hue))
            {
                return false;
            }

            // A negative chroma is clamped rather than reflected through the hue (§9.3).
            chroma = Math.Max(chroma, 0);
            var radians = hue * Math.PI / 180.0;
            a = chroma * Math.Cos(radians);
            b = chroma * Math.Sin(radians);
        }
        else
        {
            if (!TryComponent(parts[1], axisReference, out a) ||
                !TryComponent(parts[2], axisReference, out b))
            {
                return false;
            }
        }

        var linear = isOk
            ? OkLabToLinearSrgb(lightness, a, b)
            : XyzD65ToLinearSrgb(XyzD50ToD65(LabToXyzD50(lightness, a, b)));

        rgba = ToSrgbBytes(linear, alpha);
        return true;
    }

    private static bool TryParseColorFunction(string body, out (double R, double G, double B, double A) rgba)
    {
        rgba = default;
        if (!TrySplitComponents(body, out var parts, out var alphaToken) || parts.Count != 4)
            return false;
        if (!TryAlpha(alphaToken, out var alpha))
            return false;

        var space = parts[0].ToLowerInvariant();
        if (!TryComponent(parts[1], 1, out var c1) ||
            !TryComponent(parts[2], 1, out var c2) ||
            !TryComponent(parts[3], 1, out var c3))
        {
            return false;
        }

        // The predefined spaces of CSS Color 4 §10, each reduced to linear sRGB: decode the space's
        // own transfer function, rotate its primaries into XYZ, and rotate XYZ into sRGB's.
        (double R, double G, double B) linear;
        switch (space)
        {
            case "srgb":
                linear = (SrgbToLinear(c1), SrgbToLinear(c2), SrgbToLinear(c3));
                break;
            case "srgb-linear":
                linear = (c1, c2, c3);
                break;
            case "display-p3":
                linear = XyzD65ToLinearSrgb(
                    Transform(DisplayP3ToXyzD65, (SrgbToLinear(c1), SrgbToLinear(c2), SrgbToLinear(c3))));
                break;
            // Same primaries as display-p3 with the transfer function already undone, so the
            // components go straight into the primary rotation.
            case "display-p3-linear":
                linear = XyzD65ToLinearSrgb(Transform(DisplayP3ToXyzD65, (c1, c2, c3)));
                break;
            case "a98-rgb":
                linear = XyzD65ToLinearSrgb(
                    Transform(A98ToXyzD65, (A98ToLinear(c1), A98ToLinear(c2), A98ToLinear(c3))));
                break;
            case "prophoto-rgb":
                linear = XyzD65ToLinearSrgb(XyzD50ToD65(
                    Transform(ProPhotoToXyzD50,
                        (ProPhotoToLinear(c1), ProPhotoToLinear(c2), ProPhotoToLinear(c3)))));
                break;
            case "rec2020":
                linear = XyzD65ToLinearSrgb(
                    Transform(Rec2020ToXyzD65,
                        (Rec2020ToLinear(c1), Rec2020ToLinear(c2), Rec2020ToLinear(c3))));
                break;
            case "xyz":
            case "xyz-d65":
                linear = XyzD65ToLinearSrgb((c1, c2, c3));
                break;
            case "xyz-d50":
                linear = XyzD65ToLinearSrgb(XyzD50ToD65((c1, c2, c3)));
                break;
            default:
                return false;
        }

        rgba = ToSrgbBytes(linear, alpha);
        return true;
    }

    /// <summary>
    /// <c>color-mix(in &lt;space&gt;[ &lt;hue-method&gt; hue], c1 [p1], c2 [p2])</c>, CSS Color 5 §3.
    /// </summary>
    /// <remarks>
    /// Mixing happens in the named space, so the two colours are converted into it, interpolated
    /// component-wise, and converted back. The polar spaces additionally choose which way round the
    /// hue circle to travel; <c>shorter</c> is the default and the other three are supported because
    /// picking the wrong arc is a visible error, not a rounding one.
    /// </remarks>
    private static bool TryParseColorMix(string body, out (double R, double G, double B, double A) rgba)
    {
        rgba = default;

        var arguments = SplitTopLevel(body, ',');
        if (arguments.Count != 3)
            return false;

        var head = arguments[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (head.Length < 2 || !head[0].Equals("in", StringComparison.OrdinalIgnoreCase))
            return false;

        var space = head[1].ToLowerInvariant();
        var hueMethod = "shorter";
        if (head.Length >= 4 && head[3].Equals("hue", StringComparison.OrdinalIgnoreCase))
            hueMethod = head[2].ToLowerInvariant();
        else if (head.Length != 2)
            return false;

        if (!TryMixOperand(arguments[1], out var first, out var firstPercent) ||
            !TryMixOperand(arguments[2], out var second, out var secondPercent))
        {
            return false;
        }

        // §3.2: an omitted percentage is whatever the other leaves over; both omitted is 50/50; and
        // a pair summing under 100% scales the result's alpha rather than the colours.
        double p1, p2;
        if (firstPercent is null && secondPercent is null)
        {
            p1 = p2 = 0.5;
        }
        else if (firstPercent is null)
        {
            p2 = secondPercent!.Value / 100.0;
            p1 = 1 - p2;
        }
        else if (secondPercent is null)
        {
            p1 = firstPercent.Value / 100.0;
            p2 = 1 - p1;
        }
        else
        {
            p1 = firstPercent.Value / 100.0;
            p2 = secondPercent.Value / 100.0;
        }

        var total = p1 + p2;
        if (total <= 0)
            return false;

        var alphaScale = Math.Min(total, 1.0);
        p1 /= total;
        p2 /= total;

        if (!TryMixInSpace(space, hueMethod, first, second, p1, p2, out var mixed))
            return false;

        rgba = (mixed.R, mixed.G, mixed.B, Clamp(mixed.A * alphaScale, 0, 1));
        return true;
    }

    /// <summary>One <c>&lt;color&gt; &amp;&amp; &lt;percentage&gt;?</c> operand of a
    /// <c>color-mix()</c>, in either order.</summary>
    private static bool TryMixOperand(
        string argument, out (double R, double G, double B, double A) color, out double? percent)
    {
        color = default;
        percent = null;

        var tokens = SplitTopLevel(argument, ' ');
        var colorTokens = new List<string>();
        foreach (var token in tokens)
        {
            if (token.EndsWith('%') &&
                double.TryParse(
                    token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                if (percent is not null)
                    return false;
                percent = parsed;
                continue;
            }

            colorTokens.Add(token);
        }

        return colorTokens.Count == 1 && TryResolveAnyColor(colorTokens[0], out color);
    }

    /// <summary>
    /// Any absolute colour, whether this class converts it or the base parser already did: a
    /// <c>color-mix()</c> operand is much more often <c>red</c> or <c>#0f0</c> than an OKLCH.
    /// </summary>
    private static bool TryResolveAnyColor(string token, out (double R, double G, double B, double A) color)
    {
        // CSS Color 5 §3: `currentcolor` is an ordinary operand of a mix, and the commonest one
        // after a literal. It resolves against the element, which only the caller knows.
        if (token.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
        {
            if (_currentColor is null)
            {
                color = default;
                return false;
            }

            token = _currentColor;
        }

        if (TryParse(token, out color))
            return true;

        if (CssValueParser.TryParseColor(token, out var parsed))
        {
            color = (parsed.Red, parsed.Green, parsed.Blue, parsed.Alpha / 255.0);
            return true;
        }

        color = default;
        return false;
    }

    private static bool TryMixInSpace(
        string space, string hueMethod,
        (double R, double G, double B, double A) first,
        (double R, double G, double B, double A) second,
        double p1, double p2,
        out (double R, double G, double B, double A) mixed)
    {
        mixed = default;
        var alpha = (first.A * p1) + (second.A * p2);

        switch (space)
        {
            case "srgb":
            {
                mixed = (
                    (first.R * p1) + (second.R * p2),
                    (first.G * p1) + (second.G * p2),
                    (first.B * p1) + (second.B * p2),
                    alpha);
                return true;
            }

            case "srgb-linear":
            case "xyz":
            case "xyz-d65":
            case "xyz-d50":
            case "lab":
            case "lch":
            case "oklab":
            case "oklch":
            case "hsl":
            case "hwb":
            {
                var a = ToMixSpace(space, first);
                var b = ToMixSpace(space, second);
                var isPolar = space is "lch" or "oklch" or "hsl" or "hwb";

                // In hsl/hwb the hue is the first component; in lch/oklch it is the third.
                var hueIndex = space is "hsl" or "hwb" ? 0 : 2;
                var components = new double[3];
                for (var index = 0; index < 3; index++)
                {
                    components[index] = isPolar && index == hueIndex
                        ? InterpolateHue(a[index], b[index], p2, hueMethod)
                        : (a[index] * p1) + (b[index] * p2);
                }

                var rgb = FromMixSpace(space, components);
                mixed = (rgb.R, rgb.G, rgb.B, alpha);
                return true;
            }

            default:
                return false;
        }
    }

    private static double[] ToMixSpace(string space, (double R, double G, double B, double A) color)
    {
        var linear = (SrgbToLinear(color.R / 255.0), SrgbToLinear(color.G / 255.0), SrgbToLinear(color.B / 255.0));
        switch (space)
        {
            case "srgb-linear":
                return new[] { linear.Item1, linear.Item2, linear.Item3 };
            case "xyz":
            case "xyz-d65":
                return AsArray(LinearSrgbToXyzD65(linear));
            case "xyz-d50":
                return AsArray(XyzD65ToD50(LinearSrgbToXyzD65(linear)));
            case "lab":
                return AsArray(XyzD50ToLab(XyzD65ToD50(LinearSrgbToXyzD65(linear))));
            case "lch":
                return AsArray(ToPolar(XyzD50ToLab(XyzD65ToD50(LinearSrgbToXyzD65(linear)))));
            case "oklab":
                return AsArray(LinearSrgbToOkLab(linear));
            case "oklch":
                return AsArray(ToPolar(LinearSrgbToOkLab(linear)));
            case "hsl":
                return AsArray(RgbToHsl(color.R / 255.0, color.G / 255.0, color.B / 255.0));
            case "hwb":
                return AsArray(RgbToHwb(color.R / 255.0, color.G / 255.0, color.B / 255.0));
            default:
                return new[] { color.R, color.G, color.B };
        }
    }

    private static (double R, double G, double B) FromMixSpace(string space, double[] components)
    {
        var (c1, c2, c3) = (components[0], components[1], components[2]);
        switch (space)
        {
            case "srgb-linear":
                return ToSrgb((c1, c2, c3));
            case "xyz":
            case "xyz-d65":
                return ToSrgb(XyzD65ToLinearSrgb((c1, c2, c3)));
            case "xyz-d50":
                return ToSrgb(XyzD65ToLinearSrgb(XyzD50ToD65((c1, c2, c3))));
            case "lab":
                return ToSrgb(XyzD65ToLinearSrgb(XyzD50ToD65(LabToXyzD50(c1, c2, c3))));
            case "lch":
            {
                var (l, a, b) = FromPolar(c1, c2, c3);
                return ToSrgb(XyzD65ToLinearSrgb(XyzD50ToD65(LabToXyzD50(l, a, b))));
            }

            case "oklab":
                return ToSrgb(OkLabToLinearSrgb(c1, c2, c3));
            case "oklch":
            {
                var (l, a, b) = FromPolar(c1, c2, c3);
                return ToSrgb(OkLabToLinearSrgb(l, a, b));
            }

            case "hsl":
            {
                var (r, g, b) = HslToRgb(c1, c2, c3);
                return (r * 255.0, g * 255.0, b * 255.0);
            }

            case "hwb":
            {
                var (r, g, b) = HwbToRgb(c1, c2, c3);
                return (r * 255.0, g * 255.0, b * 255.0);
            }

            default:
                return (c1, c2, c3);
        }
    }

    /// <summary>CSS Color 4 §12.4's four hue-interpolation arcs.</summary>
    private static double InterpolateHue(double from, double to, double progress, string method)
    {
        from = NormalizeDegrees(from);
        to = NormalizeDegrees(to);
        var delta = to - from;

        switch (method)
        {
            case "longer":
                if (delta is > -180 and < 180)
                    delta += delta > 0 ? -360 : 360;
                break;
            case "increasing":
                if (delta < 0)
                    delta += 360;
                break;
            case "decreasing":
                if (delta > 0)
                    delta -= 360;
                break;
            default: // shorter
                if (delta > 180)
                    delta -= 360;
                else if (delta < -180)
                    delta += 360;
                break;
        }

        return from + (delta * progress);
    }

    private static double NormalizeDegrees(double degrees)
    {
        degrees %= 360;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static List<string> SplitTopLevel(string value, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var current = new StringBuilder();
        foreach (var character in value)
        {
            if (character == '(')
                depth++;
            else if (character == ')')
                depth--;

            var isSeparator = depth == 0 &&
                (separator == ' ' ? char.IsWhiteSpace(character) : character == separator);
            if (isSeparator)
            {
                var token = current.ToString().Trim();
                if (token.Length > 0)
                    parts.Add(token);
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        var last = current.ToString().Trim();
        if (last.Length > 0)
            parts.Add(last);
        return parts;
    }

    // ---------------------------------------------------------------- rgb / hsl slash-alpha

    /// <summary>
    /// <c>rgb(R G B / A)</c>. The base parser rewrites the slash to a comma and then splits on
    /// commas, which turns the three space-separated channels into a single token and fails the
    /// arity check — so <c>rgb(0 128 128 / 50%)</c> parsed as nothing and painted black. Declines
    /// the comma-separated spelling, which that parser handles correctly.
    /// </summary>
    private static bool TryParseModernRgb(string body, out (double R, double G, double B, double A) rgba)
    {
        rgba = default;
        if (body.IndexOf('/') < 0 || body.IndexOf(',') >= 0)
            return false;
        if (!TrySplitComponents(body, out var parts, out var alphaToken) || parts.Count != 3)
            return false;

        // A percentage channel is relative to 255; a bare number already is a channel.
        if (!TryComponent(parts[0], 255, out var r) ||
            !TryComponent(parts[1], 255, out var g) ||
            !TryComponent(parts[2], 255, out var b) ||
            !TryAlpha(alphaToken, out var alpha))
        {
            return false;
        }

        rgba = (r, g, b, alpha);
        return true;
    }

    /// <summary><c>hsl(H S% L% / A)</c>, the same slash-alpha gap as <see cref="TryParseModernRgb"/>.</summary>
    private static bool TryParseModernHsl(string body, out (double R, double G, double B, double A) rgba)
    {
        rgba = default;
        if (body.IndexOf('/') < 0 || body.IndexOf(',') >= 0)
            return false;
        if (!TrySplitComponents(body, out var parts, out var alphaToken) || parts.Count != 3)
            return false;

        if (!TryAngle(parts[0], out var hue) ||
            !TryComponent(parts[1], 100, out var saturation) ||
            !TryComponent(parts[2], 100, out var lightness) ||
            !TryAlpha(alphaToken, out var alpha))
        {
            return false;
        }

        var (r, g, b) = HslToRgb(hue, Clamp(saturation / 100.0, 0, 1), Clamp(lightness / 100.0, 0, 1));
        rgba = (r * 255.0, g * 255.0, b * 255.0, alpha);
        return true;
    }

    // ---------------------------------------------------------------- colour space maths

    private static (double R, double G, double B, double A) ToSrgbBytes(
        (double R, double G, double B) linear, double alpha)
    {
        var (r, g, b) = ToSrgb(linear);
        return (r, g, b, alpha);
    }

    /// <summary>Gamma-encodes linear sRGB and scales to 0–255, gamut-mapping anything sRGB cannot
    /// show.</summary>
    private static (double R, double G, double B) ToSrgb((double R, double G, double B) linear)
    {
        if (!IsOutsideSrgb(linear))
            return (LinearToSrgb(linear.R) * 255.0, LinearToSrgb(linear.G) * 255.0, LinearToSrgb(linear.B) * 255.0);

        return GamutMapToSrgb(linear);
    }

    private static bool IsOutsideSrgb((double R, double G, double B) linear)
    {
        const double Tolerance = 1e-6;
        return linear.R < -Tolerance || linear.R > 1 + Tolerance ||
            linear.G < -Tolerance || linear.G > 1 + Tolerance ||
            linear.B < -Tolerance || linear.B > 1 + Tolerance;
    }

    /// <summary>
    /// CSS Color 4 §13.2's gamut mapping: reduce OKLCH chroma, binary-searching for the least
    /// reduction whose naive clip is within one just-noticeable difference of the colour asked for.
    /// </summary>
    /// <remarks>
    /// The alternative — clipping each channel independently, which is what the naive conversion
    /// does — shifts hue as well as saturation, and does it most on exactly the colours authors
    /// reach for a wide-gamut notation to express. <c>oklch(0.8 0.4 0)</c> clips to a flat
    /// <c>rgb(255, 0, 179)</c>; mapped, it keeps its hue and lands where a browser puts it. Only
    /// out-of-gamut colours take this path, so nothing sRGB can already show moves.
    /// </remarks>
    private static (double R, double G, double B) GamutMapToSrgb((double R, double G, double B) linear)
    {
        const double JustNoticeableDifference = 0.02;
        const double Epsilon = 0.0001;

        var (lightness, a, b) = LinearSrgbToOkLab(linear);
        if (lightness >= 1.0)
            return (255, 255, 255);
        if (lightness <= 0.0)
            return (0, 0, 0);

        var (_, chroma, hue) = ToPolar((lightness, a, b));

        static (double R, double G, double B) Clip((double R, double G, double B) value) => (
            Clamp(LinearToSrgb(value.R) * 255.0, 0, 255),
            Clamp(LinearToSrgb(value.G) * 255.0, 0, 255),
            Clamp(LinearToSrgb(value.B) * 255.0, 0, 255));

        // The distance the search minimises is measured in OKLab, where a Euclidean step is
        // perceptually even — that is the whole reason the algorithm works in this space.
        static double DeltaEOk((double L, double A, double B) x, (double L, double A, double B) y)
        {
            var dl = x.L - y.L;
            var da = x.A - y.A;
            var db = x.B - y.B;
            return Math.Sqrt((dl * dl) + (da * da) + (db * db));
        }

        (double R, double G, double B) LinearFor(double c)
        {
            var (l, aa, bb) = FromPolar(lightness, c, hue);
            return OkLabToLinearSrgb(l, aa, bb);
        }

        static (double L, double A, double B) OkLabOfSrgbBytes((double R, double G, double B) srgb) =>
            LinearSrgbToOkLab((
                SrgbToLinear(srgb.R / 255.0), SrgbToLinear(srgb.G / 255.0), SrgbToLinear(srgb.B / 255.0)));

        var clipped = Clip(linear);
        if (DeltaEOk(OkLabOfSrgbBytes(clipped), (lightness, a, b)) < JustNoticeableDifference)
            return clipped;

        double min = 0, max = chroma;
        var minInGamut = true;
        while (max - min > Epsilon)
        {
            var candidateChroma = (min + max) / 2;
            var candidateLinear = LinearFor(candidateChroma);
            var (candidateL, candidateA, candidateB) = FromPolar(lightness, candidateChroma, hue);

            if (minInGamut && !IsOutsideSrgb(candidateLinear))
            {
                min = candidateChroma;
                continue;
            }

            clipped = Clip(candidateLinear);
            var difference = DeltaEOk(OkLabOfSrgbBytes(clipped), (candidateL, candidateA, candidateB));
            if (difference < JustNoticeableDifference)
            {
                if (JustNoticeableDifference - difference < Epsilon)
                    return clipped;

                minInGamut = false;
                min = candidateChroma;
            }
            else
            {
                max = candidateChroma;
            }
        }

        return clipped;
    }

    private static double SrgbToLinear(double channel)
    {
        var magnitude = Math.Abs(channel);
        var sign = channel < 0 ? -1.0 : 1.0;
        return magnitude <= 0.04045
            ? channel / 12.92
            : sign * Math.Pow((magnitude + 0.055) / 1.055, 2.4);
    }

    private static double LinearToSrgb(double channel)
    {
        var magnitude = Math.Abs(channel);
        var sign = channel < 0 ? -1.0 : 1.0;
        return magnitude <= 0.0031308
            ? channel * 12.92
            : sign * ((1.055 * Math.Pow(magnitude, 1 / 2.4)) - 0.055);
    }

    private static double A98ToLinear(double channel)
    {
        var sign = channel < 0 ? -1.0 : 1.0;
        return sign * Math.Pow(Math.Abs(channel), 563.0 / 256.0);
    }

    private static double ProPhotoToLinear(double channel)
    {
        var magnitude = Math.Abs(channel);
        var sign = channel < 0 ? -1.0 : 1.0;
        return magnitude <= 16.0 / 512.0 ? channel / 16.0 : sign * Math.Pow(magnitude, 1.8);
    }

    private static double Rec2020ToLinear(double channel)
    {
        const double Alpha = 1.09929682680944;
        const double Beta = 0.018053968510807;
        var magnitude = Math.Abs(channel);
        var sign = channel < 0 ? -1.0 : 1.0;
        return magnitude < Beta * 4.5
            ? channel / 4.5
            : sign * Math.Pow((magnitude + Alpha - 1) / Alpha, 1 / 0.45);
    }

    private static (double X, double Y, double Z) LabToXyzD50(double lightness, double a, double b)
    {
        const double Kappa = 24389.0 / 27.0;
        const double Epsilon = 216.0 / 24389.0;

        var fy = (lightness + 16.0) / 116.0;
        var fx = fy + (a / 500.0);
        var fz = fy - (b / 200.0);

        var x = Math.Pow(fx, 3) > Epsilon ? Math.Pow(fx, 3) : ((116.0 * fx) - 16.0) / Kappa;
        var y = lightness > Kappa * Epsilon ? Math.Pow((lightness + 16.0) / 116.0, 3) : lightness / Kappa;
        var z = Math.Pow(fz, 3) > Epsilon ? Math.Pow(fz, 3) : ((116.0 * fz) - 16.0) / Kappa;

        return (x * D50White.X, y * D50White.Y, z * D50White.Z);
    }

    private static (double L, double A, double B) XyzD50ToLab((double X, double Y, double Z) xyz)
    {
        const double Kappa = 24389.0 / 27.0;
        const double Epsilon = 216.0 / 24389.0;

        static double F(double t, double epsilon, double kappa) =>
            t > epsilon ? Math.Cbrt(t) : ((kappa * t) + 16.0) / 116.0;

        var fx = F(xyz.X / D50White.X, Epsilon, Kappa);
        var fy = F(xyz.Y / D50White.Y, Epsilon, Kappa);
        var fz = F(xyz.Z / D50White.Z, Epsilon, Kappa);

        return ((116.0 * fy) - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
    }

    private static (double L, double A, double B) LinearSrgbToOkLab((double R, double G, double B) linear)
    {
        var l = (0.4122214708 * linear.R) + (0.5363325363 * linear.G) + (0.0514459929 * linear.B);
        var m = (0.2119034982 * linear.R) + (0.6806995451 * linear.G) + (0.1073969566 * linear.B);
        var s = (0.0883024619 * linear.R) + (0.2817188376 * linear.G) + (0.6299787005 * linear.B);

        var l_ = Math.Cbrt(l);
        var m_ = Math.Cbrt(m);
        var s_ = Math.Cbrt(s);

        return (
            (0.2104542553 * l_) + (0.7936177850 * m_) - (0.0040720468 * s_),
            (1.9779984951 * l_) - (2.4285922050 * m_) + (0.4505937099 * s_),
            (0.0259040371 * l_) + (0.7827717662 * m_) - (0.8086757660 * s_));
    }

    private static (double R, double G, double B) OkLabToLinearSrgb(double lightness, double a, double b)
    {
        var l_ = lightness + (0.3963377774 * a) + (0.2158037573 * b);
        var m_ = lightness - (0.1055613458 * a) - (0.0638541728 * b);
        var s_ = lightness - (0.0894841775 * a) - (1.2914855480 * b);

        var l = l_ * l_ * l_;
        var m = m_ * m_ * m_;
        var s = s_ * s_ * s_;

        return (
            (4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s),
            (-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s),
            (-0.0041960863 * l) - (0.7034186147 * m) + (1.7076147010 * s));
    }

    /// <summary>Rectangular a/b to polar chroma/hue, keeping the lightness in place — the shared
    /// tail of Lab→LCH and OKLab→OKLCH.</summary>
    private static (double L, double C, double H) ToPolar((double L, double A, double B) lab)
    {
        var chroma = Math.Sqrt((lab.A * lab.A) + (lab.B * lab.B));
        var hue = NormalizeDegrees(Math.Atan2(lab.B, lab.A) * 180.0 / Math.PI);
        return (lab.L, chroma, hue);
    }

    private static (double L, double A, double B) FromPolar(double lightness, double chroma, double hue)
    {
        var radians = hue * Math.PI / 180.0;
        return (lightness, chroma * Math.Cos(radians), chroma * Math.Sin(radians));
    }

    private static (double R, double G, double B) HslToRgb(double hue, double saturation, double lightness)
    {
        hue = NormalizeDegrees(hue) / 30.0;
        var a = saturation * Math.Min(lightness, 1 - lightness);

        double Channel(double n)
        {
            var k = (n + hue) % 12;
            return lightness - (a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1)));
        }

        return (Channel(0), Channel(8), Channel(4));
    }

    private static (double H, double S, double L) RgbToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2.0;
        var delta = max - min;

        if (delta <= 0)
            return (0, 0, lightness);

        var saturation = delta / (1 - Math.Abs((2 * lightness) - 1));
        double hue;
        if (max == r)
            hue = ((g - b) / delta % 6) * 60;
        else if (max == g)
            hue = (((b - r) / delta) + 2) * 60;
        else
            hue = (((r - g) / delta) + 4) * 60;

        return (NormalizeDegrees(hue), saturation, lightness);
    }

    private static (double R, double G, double B) HwbToRgb(double hue, double whiteness, double blackness)
    {
        if (whiteness + blackness >= 1)
        {
            var grey = whiteness / (whiteness + blackness);
            return (grey, grey, grey);
        }

        var (r, g, b) = HslToRgb(hue, 1, 0.5);
        var scale = 1 - whiteness - blackness;
        return ((r * scale) + whiteness, (g * scale) + whiteness, (b * scale) + whiteness);
    }

    private static (double H, double W, double B) RgbToHwb(double r, double g, double b)
    {
        var (hue, _, _) = RgbToHsl(r, g, b);
        return (hue, Math.Min(r, Math.Min(g, b)), 1 - Math.Max(r, Math.Max(g, b)));
    }

    private static (double X, double Y, double Z) LinearSrgbToXyzD65((double R, double G, double B) linear)
        => Transform(SrgbToXyzD65, linear);

    private static (double R, double G, double B) XyzD65ToLinearSrgb((double X, double Y, double Z) xyz)
        => Transform(XyzD65ToSrgb, xyz);

    private static (double X, double Y, double Z) XyzD50ToD65((double X, double Y, double Z) xyz)
        => Transform(BradfordD50ToD65, xyz);

    private static (double X, double Y, double Z) XyzD65ToD50((double X, double Y, double Z) xyz)
        => Transform(BradfordD65ToD50, xyz);

    private static (double, double, double) Transform(double[,] matrix, (double A, double B, double C) v) => (
        (matrix[0, 0] * v.A) + (matrix[0, 1] * v.B) + (matrix[0, 2] * v.C),
        (matrix[1, 0] * v.A) + (matrix[1, 1] * v.B) + (matrix[1, 2] * v.C),
        (matrix[2, 0] * v.A) + (matrix[2, 1] * v.B) + (matrix[2, 2] * v.C));

    private static double[] AsArray((double A, double B, double C) v) => new[] { v.A, v.B, v.C };

    private static double Clamp(double value, double min, double max) =>
        double.IsNaN(value) ? min : Math.Min(Math.Max(value, min), max);

    private static readonly (double X, double Y, double Z) D50White =
        (0.3457 / 0.3585, 1.0, (1.0 - 0.3457 - 0.3585) / 0.3585);

    private static readonly double[,] SrgbToXyzD65 =
    {
        { 0.41239079926595934, 0.357584339383878, 0.1804807884018343 },
        { 0.21263900587151027, 0.715168678767756, 0.07219231536073371 },
        { 0.01933081871559182, 0.11919477979462598, 0.9505321522496607 },
    };

    private static readonly double[,] XyzD65ToSrgb =
    {
        { 3.2409699419045226, -1.537383177570094, -0.4986107602930034 },
        { -0.9692436362808796, 1.8759675015077202, 0.04155505740717559 },
        { 0.05563007969699366, -0.20397695888897652, 1.0569715142428786 },
    };

    private static readonly double[,] BradfordD50ToD65 =
    {
        { 0.9554734527042182, -0.023098536874261423, 0.0632593086610217 },
        { -0.028369706963208136, 1.0099954580058226, 0.021041398966943008 },
        { 0.012314001688319899, -0.020507696433477912, 1.3303659366080753 },
    };

    private static readonly double[,] BradfordD65ToD50 =
    {
        { 1.0479298208405488, 0.022946793341019088, -0.05019222954313557 },
        { 0.029627815688159344, 0.990434484573249, -0.01707382502938514 },
        { -0.009243058152591178, 0.015055144896577895, 0.7518742899580008 },
    };

    private static readonly double[,] DisplayP3ToXyzD65 =
    {
        { 0.4865709486482162, 0.26566769316909306, 0.1982172852343625 },
        { 0.2289745640697488, 0.6917385218365064, 0.079286914093745 },
        { 0.0000000000000000, 0.04511338185890264, 1.043944368900976 },
    };

    private static readonly double[,] A98ToXyzD65 =
    {
        { 0.5766690429101305, 0.1855582379065463, 0.1882286462349947 },
        { 0.2973449752505361, 0.6273635662554661, 0.0752914584939978 },
        { 0.0270313613864123, 0.0706888525358272, 0.9913375368376388 },
    };

    private static readonly double[,] ProPhotoToXyzD50 =
    {
        { 0.7977604896723027, 0.13518583717574031, 0.0313493495815248 },
        { 0.2880711282292934, 0.7118432178101014, 0.00008565396060525902 },
        { 0.0000000000000000, 0.0000000000000000, 0.8251046025104601 },
    };

    private static readonly double[,] Rec2020ToXyzD65 =
    {
        { 0.6369580483012914, 0.14461690358620832, 0.1688809751641721 },
        { 0.2627002120112671, 0.6779980715188708, 0.05930171646986196 },
        { 0.0000000000000000, 0.028072693049087428, 1.060985057710791 },
    };
}
