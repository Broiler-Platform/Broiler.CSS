namespace Broiler.CSS.Tests;

/// <summary>
/// Pins the CSS Values 4 §6.1 viewport-unit family: the physical units
/// (<c>vw</c>/<c>vh</c>/<c>vmin</c>/<c>vmax</c>), the logical units
/// (<c>vi</c>/<c>vb</c>), and the small/large/dynamic variants
/// (<c>sv*</c>/<c>lv*</c>/<c>dv*</c>).
/// <para>
/// REGRESSION GUARD (WPT issue #1491, problem 30): <c>vb</c> did not resolve at
/// all, so <c>page-box-008-print.html</c>'s <c>block-size: 100vb</c> box got no
/// size and the test rendered the body's hotpink background where Chromium
/// renders a yellow box.
/// </para>
/// </summary>
public sealed class CssViewportUnitTests
{
    // A deliberately non-square viewport so an axis mix-up cannot pass:
    // 1vw = 8px, 1vh = 6px, 1vmin = 6px, 1vmax = 8px.
    private const float ViewportWidth = 800;
    private const float ViewportHeight = 600;

    private static double Parse(string value, string? writingMode = null)
    {
        CssLengthParser.SetViewportSize(ViewportWidth, ViewportHeight, writingMode);
        return CssLengthParser.ParseLength(value, 0, 16);
    }

    [Theory]
    [InlineData("100vw", 800)]
    [InlineData("100vh", 600)]
    [InlineData("100vmin", 600)]
    [InlineData("100vmax", 800)]
    [InlineData("50vw", 400)]
    [InlineData("10vh", 60)]
    public void Resolves_Physical_Viewport_Units(string value, double expected)
    {
        Assert.Equal(expected, Parse(value), 6);
    }

    // The unit from the issue: 100vb must be the full block axis, not zero.
    [Theory]
    [InlineData("100vb", 600)]  // block axis = height under horizontal-tb
    [InlineData("100vi", 800)]  // inline axis = width
    [InlineData("25vb", 150)]
    [InlineData("25vi", 200)]
    public void Resolves_Logical_Viewport_Units_In_Horizontal_Writing_Mode(string value, double expected)
    {
        Assert.Equal(expected, Parse(value), 6);
        // The default overload must agree — horizontal-tb is the assumed mode.
        CssLengthParser.SetViewportSize(ViewportWidth, ViewportHeight);
        Assert.Equal(expected, CssLengthParser.ParseLength(value, 0, 16), 6);
    }

    // CSS Values 4 §6.1.4: the logical axes follow the ROOT element's writing
    // mode, so a vertical root swaps them.
    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    [InlineData("sideways-rl")]
    [InlineData("sideways-lr")]
    public void Logical_Viewport_Units_Swap_Axes_In_Vertical_Writing_Modes(string writingMode)
    {
        Assert.Equal(800, Parse("100vb", writingMode), 6);  // block axis = width
        Assert.Equal(600, Parse("100vi", writingMode), 6);  // inline axis = height
    }

    [Fact]
    public void Physical_Viewport_Units_Ignore_The_Writing_Mode()
    {
        Assert.Equal(800, Parse("100vw", "vertical-rl"), 6);
        Assert.Equal(600, Parse("100vh", "vertical-rl"), 6);
    }

    // The four viewport sizes coincide in a headless render (no retractable UA
    // chrome), so every variant resolves to its default-viewport value.
    [Theory]
    [InlineData("100svw", 800)]
    [InlineData("100lvw", 800)]
    [InlineData("100dvw", 800)]
    [InlineData("100svh", 600)]
    [InlineData("100lvh", 600)]
    [InlineData("100dvh", 600)]
    [InlineData("100svb", 600)]
    [InlineData("100lvi", 800)]
    [InlineData("100dvb", 600)]
    [InlineData("100svmin", 600)]
    [InlineData("100lvmax", 800)]
    [InlineData("100dvmin", 600)]
    public void Resolves_Small_Large_And_Dynamic_Viewport_Variants(string value, double expected)
    {
        Assert.Equal(expected, Parse(value), 6);
    }

    // "svmin" must not be scanned as a stray 's' followed by "vmin" — that would
    // leave "1s" as the number and fail to parse.
    [Fact]
    public void Longest_Unit_Spelling_Wins()
    {
        Assert.Equal(600, Parse("100svmin"), 6);
        Assert.Equal(600, Parse("100vmin"), 6);
    }

    [Theory]
    [InlineData("100vb")]
    [InlineData("100vi")]
    [InlineData("50svh")]
    [InlineData("50lvmax")]
    [InlineData("1.5dvi")]
    [InlineData("100VB")]
    [InlineData("100DvI")]
    public void IsValidLength_Accepts_The_Whole_Viewport_Family(string value)
    {
        Assert.True(CssLengthParser.IsValidLength(value));
    }

    [Theory]
    [InlineData("100vq")]
    [InlineData("100xvh")]
    [InlineData("100sv")]
    [InlineData("vb")]
    public void IsValidLength_Rejects_Non_Viewport_Units(string value)
    {
        Assert.False(CssLengthParser.IsValidLength(value));
    }

    [Theory]
    [InlineData("100vb", CssUnit.Vb)]
    [InlineData("100vi", CssUnit.Vi)]
    // The variants report their canonical default-viewport unit.
    [InlineData("100svb", CssUnit.Vb)]
    [InlineData("100dvi", CssUnit.Vi)]
    [InlineData("100lvh", CssUnit.Vh)]
    [InlineData("100svmin", CssUnit.Vmin)]
    public void CssLength_Projects_Logical_Units_Onto_CssUnit(string value, CssUnit expected)
    {
        var length = new CssLength(value);
        Assert.False(length.HasError);
        Assert.Equal(expected, length.Unit);
        Assert.True(length.IsRelative);
    }

    [Theory]
    [InlineData("100vb", 600)]
    [InlineData("100vi", 800)]
    [InlineData("100dvb", 600)]
    public void ParseToPixels_Resolves_Logical_Units(string value, double expected)
    {
        Assert.Equal(expected, CssLengthParser.ParseToPixels(value, 800, 600), 6);
    }

    [Fact]
    public void ParseToPixels_Honours_The_Root_Writing_Mode()
    {
        Assert.Equal(800, CssLengthParser.ParseToPixels("100vb", 800, 600, "vertical-rl"), 6);
        Assert.Equal(600, CssLengthParser.ParseToPixels("100vi", 800, 600, "vertical-rl"), 6);
    }

    [Fact]
    public void Logical_Units_Compose_Inside_Calc()
    {
        Assert.Equal(310, Parse("calc(50vb + 10px)"), 6);
        Assert.Equal(1000, Parse("calc(100vi + 100vb - 400px)"), 6);
        Assert.Equal(800, Parse("max(100vi, 100vb)"), 6);
        Assert.Equal(600, Parse("min(100vi, 100vb)"), 6);
    }
}
