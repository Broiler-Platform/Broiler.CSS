using System;
using System.Globalization;

namespace Broiler.CSS;

/// <summary>
/// Used-value resolution for the CSS <c>zoom</c> property.
/// </summary>
/// <remarks>
/// Promoted from the HtmlBridge layout-metrics/serialization glue, which resolved the
/// same specified-to-used composition in two places. Pure CSS value semantics with no
/// DOM/JS coupling; see <see cref="CssLengthParser.SetElementZoom"/> for how a resolved
/// factor is applied while evaluating lengths.
/// </remarks>
public static class CssZoom
{
    /// <summary>
    /// Composes an element's specified <c>zoom</c> with its parent's used zoom.
    /// <c>zoom</c> is multiplicative down the tree, so an empty value or the
    /// <c>inherit</c>/<c>normal</c> keywords yield the parent's factor unchanged,
    /// a positive number multiplies it, and anything else (non-numeric or
    /// non-positive) also leaves it unchanged.
    /// </summary>
    public static double ResolveUsed(string? specifiedZoom, double parentZoom)
    {
        if (string.IsNullOrWhiteSpace(specifiedZoom) ||
            specifiedZoom.Equals("inherit", StringComparison.OrdinalIgnoreCase) ||
            specifiedZoom.Equals("normal", StringComparison.OrdinalIgnoreCase))
        {
            return parentZoom;
        }

        if (double.TryParse(specifiedZoom, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom) && zoom > 0)
            return parentZoom * zoom;

        return parentZoom;
    }
}
