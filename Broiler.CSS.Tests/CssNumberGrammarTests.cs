namespace Broiler.CSS.Tests;

/// <summary>
/// <c>CssLengthParser.TryParseCssNumber</c> recognizes the CSS <c>&lt;number&gt;</c> grammar
/// (CSS Syntax 3 §4.3.12) and only then converts: an optional sign, digits with an optional
/// fraction or a fraction alone, and an optional exponent. Surrounding white space is tolerated.
/// </summary>
public sealed class CssNumberGrammarTests
{
    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("12", 12.0)]
    [InlineData("-1.5", -1.5)]
    [InlineData("+.5", 0.5)]
    [InlineData(".5", 0.5)]
    [InlineData("1e2", 100.0)]
    [InlineData("1E+2", 100.0)]
    [InlineData("2.5e-1", 0.25)]
    [InlineData(" 3 ", 3.0)]
    public void Accepts_Css_Numbers(string text, double expected)
    {
        Assert.True(CssLengthParser.TryParseCssNumber(text, out var value));
        Assert.Equal(expected, value, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1,5")]
    [InlineData("1,000")]
    [InlineData("1.")]
    [InlineData(".")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("1e2.5")]
    [InlineData("5-")]
    [InlineData("1 2")]
    [InlineData("0x10")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("١٢")]
    public void Rejects_Everything_Else(string text)
    {
        Assert.False(CssLengthParser.TryParseCssNumber(text, out var value));
        Assert.Equal(0.0, value);
    }
}
