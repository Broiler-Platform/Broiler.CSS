namespace Broiler.CSS;

/// <summary>
/// Single source of truth for the physical measurement factors used during CSS
/// used-value resolution and layout. Consolidates the "96 DPI" assumption and the
/// per-unit pixel conversions that were previously scattered as inline literals
/// across <c>CssLengthParser</c>, <c>CssBoxProperties</c>, <c>CssLayoutEngine</c>,
/// and <c>CssStyleEngine</c>.
///
/// <para>
/// The absolute-unit factors are <b>derived</b> from <see cref="Dpi"/> rather than
/// copied from the historical truncated <c>float</c> literals (e.g. the old
/// <c>3.779527559f</c> / <c>37.795275591f</c>). The derived <c>double</c> values
/// are mathematically exact to full <c>double</c> precision and differ from those
/// literals by ~1e-10 relative — a precision improvement that is far below
/// sub-pixel significance (orders of magnitude under the WPT ≤1%-differing-pixel
/// gate).
/// </para>
/// </summary>
public static class CssMetrics
{
    /// <summary>
    /// CSS reference pixel density: 96 CSS px per inch (CSS Values 3 §5.2). Every
    /// absolute-unit factor below is derived from this one constant.
    /// </summary>
    public const double Dpi = 96.0;

    /// <summary>1pt = 1/72 in, so 1pt = <see cref="Dpi"/>/72 CSS px (= 4/3 ≈ 1.3333).</summary>
    public const double PtToPx = Dpi / 72.0;

    /// <summary>
    /// Inverse of <see cref="PtToPx"/> (= 0.75); the factor a <c>px</c> length uses
    /// when a caller requests <c>fontAdjust</c> (px→pt) resolution.
    /// </summary>
    public const double PxToPt = 72.0 / Dpi;

    /// <summary>1in = <see cref="Dpi"/> CSS px.</summary>
    public const double PxPerInch = Dpi;

    /// <summary>1cm = <see cref="Dpi"/>/2.54 CSS px (≈ 37.7953).</summary>
    public const double PxPerCm = Dpi / 2.54;

    /// <summary>1mm = <see cref="Dpi"/>/25.4 CSS px (≈ 3.7795).</summary>
    public const double PxPerMm = Dpi / 25.4;

    /// <summary>1Q = 1/40 cm (CSS Values 3), so <see cref="PxPerCm"/>/40 (≈ 0.9449).</summary>
    public const double PxPerQ = PxPerCm / 40.0;

    /// <summary>1pc = 12pt = 12·<see cref="PtToPx"/> CSS px (= 16 at 96 DPI).</summary>
    public const double PxPerPica = 12.0 * PtToPx;

    /// <summary>
    /// CSS <c>line-height: normal</c> fallback expressed as a multiple of the font
    /// size, used when no font metric is available.
    /// </summary>
    public const double NormalLineHeightFactor = 1.2;

    /// <summary>
    /// Initial (root) font size in points — the single source that
    /// <see cref="CssConstants.FontSize"/> forwards to. Equivalent to the browser
    /// default of 16px (12pt · 96/72); the renderer's
    /// obsolete <c>CssBoxModel</c> 16px literal is dead code, so no 12-vs-16 conflict.
    /// </summary>
    public const double DefaultFontSizePt = 12.0;

    /// <summary>Initial font size resolved to CSS px (<see cref="DefaultFontSizePt"/>·<see cref="PtToPx"/>).</summary>
    public const double DefaultFontSizePx = DefaultFontSizePt * PtToPx;
}
