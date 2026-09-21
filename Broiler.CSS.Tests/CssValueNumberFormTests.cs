namespace Broiler.CSS.Tests;

/// <summary>
/// <see cref="CssValueParser.TryParseNumeric"/> reads the same CSS <c>&lt;number&gt;</c> grammar
/// (CSS Syntax 3 §4.3.12) as <c>CssLengthParser</c>, which it now shares a scanner with. It used to
/// scan sign/digits/one dot by hand, so it rejected <c>1e2px</c> outright — the <c>e</c> became part
/// of the unit — and accepted <c>1.px</c>, because the conversion tolerates a trailing dot. Both
/// answers contradicted the length parser in this same package, and the second one reached colours
/// too, since the saturation and lightness channels of <c>hsl()</c> parse through here.
/// </summary>
public sealed class CssValueNumberFormTests
{
    [Theory]
    [InlineData("1e2px", 100.0, CssUnit.Px)]
    [InlineData("1E2px", 100.0, CssUnit.Px)]
    [InlineData("1e+1px", 10.0, CssUnit.Px)]
    [InlineData("2.5e-1em", 0.25, CssUnit.Em)]
    [InlineData("5e1%", 50.0, CssUnit.Percent)]
    [InlineData("1e2", 100.0, CssUnit.None)]
    [InlineData(".5px", 0.5, CssUnit.Px)]
    [InlineData("+.5px", 0.5, CssUnit.Px)]
    [InlineData("-1.5px", -1.5, CssUnit.Px)]
    public void TryParseNumeric_Reads_Every_Css_Number_Form(string text, double number, CssUnit unit)
    {
        Assert.True(CssValueParser.TryParseNumeric(text, out var value));
        Assert.Equal(unit, value.Unit);
        Assert.Equal(number, value.Number, 9);
    }

    [Theory]
    [InlineData("1em", 1.0, CssUnit.Em)]
    [InlineData("2ex", 2.0, CssUnit.Ex)]
    [InlineData("-3e-1em", -0.3, CssUnit.Em)]
    public void TryParseNumeric_Does_Not_Read_A_Units_Leading_E_As_An_Exponent(
        string text,
        double number,
        CssUnit unit)
    {
        Assert.True(CssValueParser.TryParseNumeric(text, out var value));
        Assert.Equal(unit, value.Unit);
        Assert.Equal(number, value.Number, 9);
    }

    [Theory]
    [InlineData("1.px")]
    [InlineData("1.%")]
    [InlineData("1.")]
    [InlineData("1,5px")]
    [InlineData("1epx")]
    [InlineData("5-px")]
    [InlineData("1e2.5px")]
    [InlineData(".px")]
    public void TryParseNumeric_Rejects_Forms_That_Are_Not_Css_Numbers(string text)
    {
        Assert.False(CssValueParser.TryParseNumeric(text, out var value));
        Assert.Equal(default, value);
    }

    /// <summary>
    /// The digit requirement is why a downstream consumer moved to this parser: its previous
    /// <see cref="System.Globalization.NumberStyles.Float"/> parse read <c>NaNpx</c> and
    /// <c>Infinitypx</c> as successful lengths. Sharing the number scan must not give that back.
    /// </summary>
    [Theory]
    [InlineData("NaNpx")]
    [InlineData("Infinitypx")]
    [InlineData("NaN")]
    [InlineData("-Infinity")]
    [InlineData("∞px")]
    public void TryParseNumeric_Still_Requires_A_Digit(string text)
    {
        Assert.False(CssValueParser.TryParseNumeric(text, out _));
    }

    [Theory]
    [InlineData("1e2px")]
    [InlineData("1E2px")]
    [InlineData("2.5e-1em")]
    [InlineData("1.px")]
    [InlineData("1,5px")]
    [InlineData("NaNpx")]
    [InlineData("1epx")]
    public void TryParseNumeric_Agrees_With_The_Length_Parser(string text)
    {
        Assert.Equal(CssLengthParser.IsValidLength(text), CssValueParser.TryParseNumeric(text, out _));
    }

    [Fact]
    public void Hsl_Channels_Read_An_Exponent_And_Reject_A_Trailing_Dot()
    {
        Assert.True(CssValueParser.TryParseColor("hsl(120, 50%, 50%)", out var plain));
        Assert.True(CssValueParser.TryParseColor("hsl(120, 5e1%, 50%)", out var exponent));
        Assert.Equal(plain, exponent);

        Assert.False(CssValueParser.TryParseColor("hsl(120, 5.%, 50%)", out _));
    }
}
