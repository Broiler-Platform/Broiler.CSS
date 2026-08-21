using Broiler.CSS;

namespace Broiler.CSS.Tests;

/// <summary>
/// Pins the element <c>zoom</c> hook on <see cref="CssLengthParser"/>
/// (<see cref="CssLengthParser.SetElementZoom"/>): a <c>calc()</c> whose sub-terms mix absolute,
/// percentage and font-/viewport-relative units scales each term by the right factor — absolute
/// units (and <c>rem</c>/<c>rlh</c>) by the absolute-zoom factor, percentages by the percent-zoom
/// factor, while <c>em</c>-family units (which already ride the caller's zoomed <c>emFactor</c>) and
/// viewport units are left untouched. The factors default to the neutral <c>1.0</c>, so a caller that
/// never opts in sees the pre-zoom parser. Consumed by the engine's native CSS <c>zoom</c> model for
/// the <c>calc()</c> path (the non-<c>calc()</c> path scales the resolved value at the call site; the
/// hardcoded absolute unit→pixel factors are the only lever that cannot be reached from outside the
/// parser, which is why this lives here).
/// </summary>
public sealed class CssLengthZoomTests
{
    private static double Parse(string expr, double basis, double em, double absoluteZoom, double percentZoom)
    {
        CssLengthParser.SetElementZoom(absoluteZoom, percentZoom);
        try { return CssLengthParser.ParseLength(expr, basis, em); }
        finally { CssLengthParser.SetElementZoom(1.0, 1.0); }
    }

    [Fact(Timeout = 600000)]
    public void Default_Factors_Leave_Calc_Unscaled()
    {
        // No opt-in (1.0, 1.0): byte-identical to the pre-zoom parser.
        Assert.Equal(30, Parse("calc(10px + 20px)", 0, 16, 1.0, 1.0), 6);
    }

    [Fact(Timeout = 600000)]
    public void Calc_AbsoluteTerms_Scale_By_AbsoluteZoom()
    {
        // (10 + 20)px × 2 = 60.
        Assert.Equal(60, Parse("calc(10px + 20px)", 0, 16, 2.0, 1.0), 6);
    }

    [Fact(Timeout = 600000)]
    public void Calc_Rem_Scales_By_AbsoluteZoom()
    {
        // rem is root-relative (16px default) and counts as absolute for element zoom: 2rem = 32 × 2 = 64.
        Assert.Equal(64, Parse("calc(2rem)", 0, 16, 2.0, 1.0), 6);
    }

    [Fact(Timeout = 600000)]
    public void Calc_Percent_Scales_By_PercentZoom_And_Absolute_By_AbsoluteZoom()
    {
        // 50% of 200 = 100 × pctZoom 3 = 300; 10px × absZoom 2 = 20; total 320.
        Assert.Equal(320, Parse("calc(50% + 10px)", 200, 16, 2.0, 3.0), 6);
    }

    [Fact(Timeout = 600000)]
    public void Calc_Em_Rides_The_Zoomed_Font_And_Is_Not_ReScaled()
    {
        // em uses the caller's emFactor (already zoomed by the engine); the absolute-zoom factor must
        // NOT touch it, or it would double-count: 2em = 2 × 16 = 32, unchanged despite absZoom 2.
        Assert.Equal(32, Parse("calc(2em)", 0, 16, 2.0, 1.0), 6);
    }

    [Fact(Timeout = 600000)]
    public void Calc_Viewport_Units_Are_Unaffected_By_ElementZoom()
    {
        CssLengthParser.SetViewportSize(1000, 1000); // 1vw = 10px
        // Element zoom does not scale the viewport: 10vw = 100, unchanged despite absZoom 2.
        Assert.Equal(100, Parse("calc(10vw)", 0, 16, 2.0, 1.0), 6);
    }

    [Fact(Timeout = 600000)]
    public void Bare_Absolute_Length_Also_Honours_AbsoluteZoom()
    {
        // A non-calc length routes through the same evaluator, so the hook is uniform: 25px × 2 = 50.
        Assert.Equal(50, Parse("25px", 0, 16, 2.0, 1.0), 6);
    }
}
