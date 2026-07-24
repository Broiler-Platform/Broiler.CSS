using System.Collections.Generic;

namespace Broiler.CSS;

/// <summary>
/// The CSS 1-to-4-value box shorthand rule (<c>margin</c>, <c>padding</c>,
/// <c>border-width</c>, <c>inset</c>, …).
/// </summary>
public static class CssBoxShorthand
{
    /// <summary>
    /// Maps already-split shorthand tokens to their per-side (top/right/bottom/left)
    /// values per the shorthand rule: 1 value → all four sides; 2 → top/bottom and
    /// right/left; 3 → top, right/left, bottom; 4+ → top, right, bottom, left. An empty
    /// token list yields four empty strings.
    /// </summary>
    public static (string Top, string Right, string Bottom, string Left) SelectTrbl(IReadOnlyList<string> parts) =>
        parts.Count switch
        {
            0 => ("", "", "", ""),
            1 => (parts[0], parts[0], parts[0], parts[0]),
            2 => (parts[0], parts[1], parts[0], parts[1]),
            3 => (parts[0], parts[1], parts[2], parts[1]),
            _ => (parts[0], parts[1], parts[2], parts[3]),
        };
}
