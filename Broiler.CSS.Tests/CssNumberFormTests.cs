namespace Broiler.CSS.Tests;

/// <summary>
/// Lengths accept exactly the CSS <c>&lt;number&gt;</c> forms (CSS Syntax 3 §4.3.12) before their
/// unit. The length parsers used <see cref="System.Globalization.NumberStyles.Number"/>, which admits
/// a thousands separator and a trailing sign but no exponent, and <see cref="double.TryParse(string?, out double)"/>
/// accepts <c>NaN</c>, <c>Infinity</c> and a trailing dot under any style. So <c>1,5px</c> read as
/// <c>15px</c>, <c>NaNpx</c> was a valid length, and <c>1e2px</c> was rejected.
/// </summary>
public sealed class CssNumberFormTests
{
    [Theory]
    [InlineData("1e2px")]
    [InlineData("1E2px")]
    [InlineData("1e+1px")]
    [InlineData("2.5e-1em")]
    [InlineData(".5px")]
    [InlineData("+.5px")]
    [InlineData("-1.5px")]
    public void IsValidLength_Accepts_Css_Number_Forms_Including_An_Exponent(string value)
    {
        Assert.True(CssLengthParser.IsValidLength(value));
    }

    [Theory]
    [InlineData("1,5px")]
    [InlineData("1,000px")]
    [InlineData("NaNpx")]
    [InlineData("Infinitypx")]
    [InlineData("1.px")]
    [InlineData("1epx")]
    [InlineData("5-px")]
    [InlineData("1e2.5px")]
    public void IsValidLength_Rejects_Forms_That_Are_Not_Css_Numbers(string value)
    {
        Assert.False(CssLengthParser.IsValidLength(value));
    }

    [Fact]
    public void CssLength_Reads_An_Exponent()
    {
        var length = new CssLength("1e2px");

        Assert.False(length.HasError);
        Assert.Equal(CssUnit.Px, length.Unit);
        Assert.Equal(100.0, length.Number, 6);
    }

    [Theory]
    [InlineData("1,5px")]
    [InlineData("NaNpx")]
    [InlineData("1.px")]
    public void CssLength_Rejects_Forms_That_Are_Not_Css_Numbers(string value)
    {
        Assert.True(new CssLength(value).HasError);
    }

    [Fact]
    public void ParseNumber_Reads_An_Exponent_And_Rejects_A_Thousands_Separator()
    {
        Assert.Equal(10.0, CssLengthParser.ParseNumber("1e1%", 100), 6);
        Assert.Equal(0.0, CssLengthParser.ParseNumber("1,5%", 100), 6);
    }

    [Fact]
    public void ParseLength_Reads_An_Exponent_And_Does_Not_Read_A_Comma_As_Thousands()
    {
        Assert.Equal(10.0, CssLengthParser.ParseLength("1e1px", 100, 16), 6);
        Assert.Equal(0.0, CssLengthParser.ParseLength("1,5px", 100, 16), 6);
    }
}
