namespace Broiler.CSS.Tests;

/// <summary>
/// Behavior-parity guard for <see cref="CssLength"/> after its unit detection
/// was routed through the shared <c>CssLengthParser.GetUnit</c> scanner. Pins
/// the exact pre-refactor contract:
/// the recognized unit set (deliberately NARROWER than the parser — no lh/rlh/Q),
/// the CssUnit projection, the relative-unit flag, and the error semantics.
/// </summary>
public sealed class CssLengthTests
{
    [Theory]
    // unit,        expected CssUnit,   number, isRelative
    [InlineData("12px", CssUnit.Px, 12.0, true)]
    [InlineData("1.5em", CssUnit.Em, 1.5, true)]
    [InlineData("2ex", CssUnit.Ex, 2.0, true)]
    [InlineData("3ch", CssUnit.Ch, 3.0, true)]
    [InlineData("4ic", CssUnit.Ic, 4.0, true)]
    [InlineData("5rem", CssUnit.Rem, 5.0, true)]
    [InlineData("10vh", CssUnit.Vh, 10.0, true)]
    [InlineData("10vw", CssUnit.Vw, 10.0, true)]
    [InlineData("25vmin", CssUnit.Vmin, 25.0, true)]
    [InlineData("75vmax", CssUnit.Vmax, 75.0, true)]
    [InlineData("10mm", CssUnit.Mm, 10.0, false)]
    [InlineData("2cm", CssUnit.Cm, 2.0, false)]
    [InlineData("3in", CssUnit.In, 3.0, false)]
    [InlineData("12pt", CssUnit.Pt, 12.0, false)]
    [InlineData("1pc", CssUnit.Pc, 1.0, false)]
    [InlineData("50VMIN", CssUnit.Vmin, 50.0, true)] // viewport units are case-insensitive
    public void Parses_Supported_Units(string text, CssUnit unit, double number, bool isRelative)
    {
        var len = new CssLength(text);
        Assert.False(len.HasError);
        Assert.False(len.IsPercentage);
        Assert.Equal(unit, len.Unit);
        Assert.Equal(number, len.Number, 6);
        Assert.Equal(isRelative, len.IsRelative);
    }

    [Fact]
    public void Percentage_Stores_Fraction_And_Flags_It()
    {
        var len = new CssLength("50%");
        Assert.True(len.IsPercentage);
        Assert.False(len.HasError);
        Assert.Equal(CssUnit.None, len.Unit);
        Assert.Equal(0.5, len.Number, 6); // ParseNumber("50%", 1) => 0.5
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    public void Empty_Or_Zero_Is_Neutral(string text)
    {
        var len = new CssLength(text);
        Assert.False(len.HasError);
        Assert.False(len.IsPercentage);
        Assert.Equal(CssUnit.None, len.Unit);
        Assert.Equal(0.0, len.Number);
    }

    [Theory]
    // Units CssLength has never recognized stay errors after the M3 delegation.
    [InlineData("2lh")]
    [InlineData("3rlh")]
    [InlineData("5q")]
    [InlineData("5Q")]
    // Case-sensitive font-relative units: uppercase is not recognized.
    [InlineData("5EM")]
    [InlineData("12PX")]
    // Genuinely unknown / malformed units.
    [InlineData("10foo")]
    [InlineData("abpx")] // valid unit, unparseable number
    public void Unsupported_Or_Malformed_Sets_Error(string text)
    {
        Assert.True(new CssLength(text).HasError);
    }

    [Theory]
    [InlineData("12")]  // no unit, length < 3
    [InlineData("5")]
    public void Unitless_Nonzero_Sets_Error_But_Keeps_Number(string text)
    {
        var len = new CssLength(text);
        Assert.True(len.HasError);
        Assert.Equal(double.Parse(text, System.Globalization.CultureInfo.InvariantCulture), len.Number, 6);
    }

    [Fact]
    public void ConvertEmToPoints_Drives_The_FontSize_Path()
    {
        // Mirrors CssBoxProperties font-size resolution: an em length converts to
        // points against the parent font size, then round-trips through ToString.
        var len = new CssLength("2em");
        Assert.Equal(CssUnit.Em, len.Unit);
        var pts = len.ConvertEmToPoints(16); // 2em * 16 = 32.0pt
        Assert.Equal("32.0pt", pts.ToString());
    }
}
