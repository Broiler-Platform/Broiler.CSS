using System;
using System.Text;

namespace Broiler.CSS;

/// <summary>
/// The CSS Color 5 <c>contrast-color()</c> function: given a colour, it resolves to whichever of
/// black or white contrasts more with it.
/// <para>
/// The choice uses the WCAG 2 contrast ratio <c>(L₁ + 0.05) / (L₂ + 0.05)</c> over relative
/// luminance. White wins exactly when the input's luminance is below
/// <c>√(1.05 × 0.05) − 0.05 ≈ 0.1791</c> — the luminance at which the two ratios are equal — so the
/// comparison collapses to one threshold rather than computing both ratios.
/// </para>
/// <para>
/// DIAGNOSTIC NOTE (WPT issue #1491, problem 6):
/// <c>css/css-color/contrast-color-style-query.html</c> registers a <c>&lt;color&gt;</c> custom
/// property, sets it to <c>contrast-color(#000)</c>, and matches
/// <c>@container style(--contrast-color: white)</c> on it. With the function unresolved the
/// declaration never matched and the test rendered 100% white against Chromium's green. It needs
/// both halves — this function <em>and</em> style container queries.
/// </para>
/// </summary>
public static class CssContrastColor
{
    /// <summary>
    /// The luminance below which white contrasts more than black. Solves
    /// <c>1.05 / (L + 0.05) = (L + 0.05) / 0.05</c> for L.
    /// </summary>
    private const double WhiteWinsBelowLuminance = 0.17912878474779204;

    private static readonly CssColor Black = new(0, 0, 0);
    private static readonly CssColor White = new(255, 255, 255);

    /// <summary>
    /// Picks the higher-contrast of black and white for <paramref name="against"/>.
    /// Alpha is ignored: the function is defined over the colour itself, and the result is always
    /// fully opaque.
    /// </summary>
    public static CssColor Pick(CssColor against) =>
        RelativeLuminance(against) < WhiteWinsBelowLuminance ? White : Black;

    /// <summary>
    /// WCAG 2 relative luminance: sRGB channels linearised, then weighted for human luminance
    /// sensitivity (green dominates, blue barely registers).
    /// </summary>
    public static double RelativeLuminance(CssColor color) =>
        (0.2126 * Linearise(color.Red)) +
        (0.7152 * Linearise(color.Green)) +
        (0.0722 * Linearise(color.Blue));

    private static double Linearise(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// Resolves a single <c>contrast-color(&lt;color&gt;)</c> token to its absolute colour.
    /// Returns <see langword="false"/> when <paramref name="text"/> is not that function, or when
    /// its argument is not a colour this engine can parse — in which case callers must leave the
    /// value untouched rather than guess.
    /// </summary>
    public static bool TryResolve(string? text, out CssColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var input = text.Trim();
        if (!TryReadFunctionArgument(input, 0, out var argument, out var end) || end != input.Length)
            return false;

        if (!CssValueParser.TryParseColor(argument, out var against))
            return false;

        color = Pick(against);
        return true;
    }

    /// <summary>
    /// Rewrites every <c>contrast-color(...)</c> occurrence in <paramref name="value"/> to its
    /// resolved <c>rgb(...)</c>, leaving the rest byte-identical. An occurrence whose argument does
    /// not parse is left alone, so an unsupported colour syntax degrades to the pre-support
    /// behaviour instead of resolving to something wrong.
    /// <para>
    /// Nested occurrences are not expected — the argument is a <c>&lt;color&gt;</c>, and
    /// <c>contrast-color()</c> of a <c>contrast-color()</c> is degenerate — but a nested inner call
    /// is simply left in place rather than mis-parsed.
    /// </para>
    /// </summary>
    public static string ResolveFunctions(string? value)
    {
        if (string.IsNullOrEmpty(value) ||
            value.IndexOf("contrast-color", StringComparison.OrdinalIgnoreCase) < 0)
            return value ?? string.Empty;

        StringBuilder? sb = null;
        var copiedTo = 0;
        var i = 0;

        while (i < value.Length)
        {
            if (!TryReadFunctionArgument(value, i, out var argument, out var end))
            {
                i++;
                continue;
            }

            if (CssValueParser.TryParseColor(argument, out var against))
            {
                sb ??= new StringBuilder(value.Length);
                sb.Append(value, copiedTo, i - copiedTo);
                sb.Append(Pick(against).ToCssString());
                copiedTo = end;
            }

            i = end;
        }

        if (sb is null)
            return value;

        sb.Append(value, copiedTo, value.Length - copiedTo);
        return sb.ToString();
    }

    /// <summary>
    /// Reads a <c>contrast-color(</c>…<c>)</c> starting at <paramref name="start"/>, reporting its
    /// argument text and the index just past the closing parenthesis. Parenthesis depth is tracked
    /// so a function-valued argument (<c>rgb(0 0 0)</c>) does not terminate the scan early.
    /// </summary>
    private static bool TryReadFunctionArgument(string value, int start, out string argument, out int end)
    {
        argument = string.Empty;
        end = start;

        const string Name = "contrast-color";
        if (start + Name.Length >= value.Length)
            return false;
        if (string.Compare(value, start, Name, 0, Name.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;

        // The identifier must not be the tail of a longer one (e.g. "--my-contrast-color").
        if (start > 0 && (char.IsLetterOrDigit(value[start - 1]) || value[start - 1] is '-' or '_'))
            return false;

        var i = start + Name.Length;
        while (i < value.Length && char.IsWhiteSpace(value[i]))
            i++;
        if (i >= value.Length || value[i] != '(')
            return false;

        var depth = 0;
        var argumentStart = i + 1;
        for (; i < value.Length; i++)
        {
            if (value[i] == '(')
            {
                depth++;
            }
            else if (value[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    argument = value[argumentStart..i].Trim();
                    end = i + 1;
                    return argument.Length > 0;
                }
            }
        }

        return false;
    }
}
