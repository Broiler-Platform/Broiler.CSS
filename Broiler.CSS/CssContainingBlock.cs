using System;

namespace Broiler.CSS;

/// <summary>
/// Shared predicates for which CSS properties make a box a containing block for
/// its absolutely/fixed-positioned descendants.
/// </summary>
/// <remarks>
/// The <c>transform</c> / <c>contain</c> / <c>will-change</c> trio below was
/// duplicated between the HtmlBridge anchor resolver and the Broiler.Layout native
/// containing-block path (each annotated as mirroring the other); this is the single
/// source of truth. The <c>position</c> keyword branch is handled by each caller,
/// which needs it at a different point.
/// </remarks>
public static class CssContainingBlock
{
    /// <summary>
    /// Whether a non-<c>position</c> property establishes a containing block for
    /// abs/fixed descendants: a <c>transform</c> other than <c>none</c> (CSS Transforms),
    /// <c>contain</c> including <c>layout</c>/<c>paint</c>/<c>strict</c>/<c>content</c>
    /// (CSS Containment), or <c>will-change: transform</c> (CSS Will Change §3).
    /// </summary>
    public static bool CreatedByTransformContainOrWillChange(string? transform, string? contain, string? willChange)
    {
        if (!string.IsNullOrWhiteSpace(transform) &&
            !string.Equals(transform, "none", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(contain))
        {
            var c = contain.ToLowerInvariant();
            if (c.Contains("layout") || c.Contains("paint") || c.Contains("strict") || c.Contains("content"))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(willChange) &&
            willChange.Contains("transform", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
