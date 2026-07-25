using System;
using System.Collections.Generic;

namespace Broiler.CSS;

/// <summary>
/// Classification helpers for the CSS <c>overflow</c> shorthand and its
/// <c>overflow-x</c>/<c>overflow-y</c> longhands.
/// </summary>
/// <remarks>
/// Promoted from the HtmlBridge glue layer, where the clipping predicate was
/// duplicated across the anchor resolver, scroll simulation, and layout metrics.
/// Pure CSS keyword classification with no DOM/JS coupling.
/// </remarks>
public static class CssOverflow
{
    private static bool Clips(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var v = value.Trim().ToLowerInvariant();
        return v.Contains("hidden") || v.Contains("scroll") || v.Contains("auto") || v.Contains("clip");
    }

    /// <summary>
    /// Whether any of the <c>overflow</c> / <c>overflow-x</c> / <c>overflow-y</c> values
    /// makes the box clip its overflow (i.e. is <c>hidden</c>, <c>scroll</c>, <c>auto</c>,
    /// or <c>clip</c> — anything other than the <c>visible</c> initial value).
    /// </summary>
    public static bool ClipsOverflow(string? overflow, string? overflowX, string? overflowY) =>
        Clips(overflow) || Clips(overflowX) || Clips(overflowY);

    /// <summary>
    /// Convenience overload reading the three properties from a computed/declared
    /// property map.
    /// </summary>
    public static bool ClipsOverflow(IReadOnlyDictionary<string, string> props)
    {
        props.TryGetValue("overflow", out var overflow);
        props.TryGetValue("overflow-x", out var overflowX);
        props.TryGetValue("overflow-y", out var overflowY);
        return ClipsOverflow(overflow, overflowX, overflowY);
    }
}
