using System;
using System.Collections.Generic;
using System.Globalization;

namespace Broiler.CSS;

/// <summary>
/// Resolves a used border width for one side from cascaded declarations, following the
/// longhand → <c>border-width</c> shorthand → <c>border</c> shorthand fallback chain.
/// </summary>
/// <remarks>
/// Font-free: pixel lengths are taken as-is and the
/// <c>thin</c>/<c>medium</c>/<c>thick</c> keywords resolve through
/// <see cref="CssLengthParser.GetActualBorderWidth"/> (1/3/5 px), so em-relative widths are
/// not resolved here. Promoted from the HtmlBridge anchor resolver, which needed border
/// widths without live font metrics.
/// </remarks>
public static class CssBorderWidth
{
    private static readonly string[] BorderStyleKeywords =
        ["solid", "dotted", "dashed", "double", "groove", "ridge", "inset", "outset"];

    /// <summary>
    /// Resolves the used width (px) for <paramref name="sideProperty"/> (e.g.
    /// <c>border-left-width</c>), consulting that longhand, then the <c>border-width</c>
    /// shorthand, then <paramref name="shorthandProperty"/> (e.g. <c>border</c>). A
    /// <c>border</c> shorthand carrying only a style implies the initial <c>medium</c>
    /// width. Returns 0 when no declaration applies.
    /// </summary>
    public static double Resolve(
        IReadOnlyDictionary<string, string> declarations,
        string sideProperty,
        string shorthandProperty)
    {
        if (declarations.TryGetValue(sideProperty, out var sideVal) && sideVal != null)
            return ResolveKeywordOrPx(sideVal);

        // The border-width shorthand (1-4 values: top [right [bottom [left]]]).
        if (declarations.TryGetValue("border-width", out var bwVal) && bwVal != null)
        {
            var parts = bwVal.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var (top, right, bottom, left) = CssBoxShorthand.SelectTrbl(parts);
            var token = sideProperty switch
            {
                "border-top-width" => top,
                "border-right-width" => right,
                "border-bottom-width" => bottom,
                "border-left-width" => left,
                _ => top,
            };
            return ResolveKeywordOrPx(token);
        }

        // The border shorthand (e.g. "1px solid red" or just "solid").
        if (declarations.TryGetValue(shorthandProperty, out var shortVal) && shortVal != null)
        {
            var tokens = shortVal.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // An explicit width (length or keyword) in the shorthand wins.
            foreach (var part in tokens)
            {
                var px = TryParsePx(part);
                if (px.HasValue)
                    return px.Value;
                if (IsWidthKeyword(part))
                    return ResolveKeywordOrPx(part);
            }

            // "border: solid" (style only, no width) → the initial width keyword, medium.
            foreach (var part in tokens)
            {
                foreach (var style in BorderStyleKeywords)
                {
                    if (part.Equals(style, StringComparison.OrdinalIgnoreCase))
                        return ResolveKeywordOrPx("medium");
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Converts a border-width keyword or pixel length to a number: the
    /// <c>thin</c>/<c>medium</c>/<c>thick</c> keywords defer to
    /// <see cref="CssLengthParser.GetActualBorderWidth"/>; anything else is read as a
    /// pixel length (0 when unparseable).
    /// </summary>
    public static double ResolveKeywordOrPx(string value)
    {
        var v = value.Trim();
        if (IsWidthKeyword(v))
            return CssLengthParser.GetActualBorderWidth(v.ToLowerInvariant(), 0);
        return TryParsePx(v) ?? 0;
    }

    private static bool IsWidthKeyword(string value) =>
        value.Equals("thin", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("medium", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("thick", StringComparison.OrdinalIgnoreCase);

    private static double? TryParsePx(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            v = v[..^2];
        if (v.Contains('%')) return null;
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
