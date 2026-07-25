namespace Broiler.CSS;

/// <summary>
/// Classification helpers for the CSS <c>writing-mode</c> property.
/// </summary>
/// <remarks>
/// Promoted from the HtmlBridge glue layer, where the vertical-mode predicate was
/// duplicated across the anchor resolver, hit testing, scrolling metrics, and
/// serialization. Pure CSS keyword classification with no DOM/JS coupling.
/// </remarks>
public static class CssWritingMode
{
    /// <summary>
    /// Whether <paramref name="writingMode"/> is a vertical writing mode
    /// (<c>vertical-rl</c>, <c>vertical-lr</c>, <c>sideways-rl</c>, or
    /// <c>sideways-lr</c>). Whitespace and case are ignored; <see langword="null"/>
    /// / empty (the default <c>horizontal-tb</c>) is not vertical.
    /// </summary>
    public static bool IsVertical(string? writingMode)
    {
        var normalized = writingMode?.Trim().ToLowerInvariant();
        return normalized is "vertical-rl" or "vertical-lr" or "sideways-rl" or "sideways-lr";
    }
}
