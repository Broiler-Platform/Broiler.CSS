namespace Broiler.CSS.Tests;

/// <summary>
/// The CSS Color 5 <c>contrast-color()</c> function — resolves to whichever of black or white
/// contrasts more with its argument.
/// <para>
/// REGRESSION GUARD (WPT issue #1491, problem 6):
/// <c>css/css-color/contrast-color-style-query.html</c> sets a registered <c>&lt;color&gt;</c>
/// custom property to <c>contrast-color(#000)</c> and matches
/// <c>@container style(--contrast-color: white)</c> on it. With the function unresolved the
/// declaration never matched and the test rendered 100% white against Chromium's green.
/// </para>
/// </summary>
public sealed class CssContrastColorTests
{
    private static readonly CssColor Black = new(0, 0, 0);
    private static readonly CssColor White = new(255, 255, 255);

    [Theory]
    // Dark inputs take white; the case from the issue is the first row.
    [InlineData("#000")]
    [InlineData("#000000")]
    [InlineData("black")]
    [InlineData("navy")]
    [InlineData("rgb(0, 0, 128)")]
    [InlineData("#333333")]
    public void Dark_Colors_Contrast_With_White(string input)
    {
        Assert.True(CssValueParser.TryParseColor(input, out var color));
        Assert.Equal(White, CssContrastColor.Pick(color));
    }

    [Theory]
    [InlineData("#fff")]
    [InlineData("white")]
    [InlineData("yellow")]
    [InlineData("#cccccc")]
    [InlineData("rgb(255, 255, 0)")]
    public void Light_Colors_Contrast_With_Black(string input)
    {
        Assert.True(CssValueParser.TryParseColor(input, out var color));
        Assert.Equal(Black, CssContrastColor.Pick(color));
    }

    // The threshold is where the two WCAG contrast ratios are equal, not luminance 0.5 —
    // a mid grey is light enough that black contrasts better.
    [Fact(Timeout = 600000)]
    public void The_Threshold_Is_The_Equal_Contrast_Luminance_Not_Mid_Grey()
    {
        Assert.True(CssValueParser.TryParseColor("#767676", out var justLight));
        Assert.Equal(Black, CssContrastColor.Pick(justLight));

        Assert.True(CssValueParser.TryParseColor("#757575", out var justDark));
        Assert.Equal(White, CssContrastColor.Pick(justDark));
    }

    [Fact(Timeout = 600000)]
    public void Relative_Luminance_Spans_Zero_To_One()
    {
        Assert.Equal(0.0, CssContrastColor.RelativeLuminance(Black), 6);
        Assert.Equal(1.0, CssContrastColor.RelativeLuminance(White), 6);
        // Green dominates the human-sensitivity weighting; blue barely registers.
        Assert.True(CssContrastColor.RelativeLuminance(new CssColor(0, 255, 0)) >
                    CssContrastColor.RelativeLuminance(new CssColor(0, 0, 255)));
    }

    [Theory]
    [InlineData("contrast-color(#000)", 255, 255, 255)]
    [InlineData("contrast-color(#fff)", 0, 0, 0)]
    [InlineData("CONTRAST-COLOR( black )", 255, 255, 255)]
    [InlineData("contrast-color(rgb(0, 0, 0))", 255, 255, 255)]
    public void TryResolve_Reads_The_Function(string input, byte r, byte g, byte b)
    {
        Assert.True(CssContrastColor.TryResolve(input, out var color));
        Assert.Equal(new CssColor(r, g, b), color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("white")]
    [InlineData("contrast-color()")]
    [InlineData("contrast-color(not-a-color)")]
    [InlineData("contrast-color(#000")]
    // Must not fire on a longer identifier that merely ends with the function name.
    [InlineData("--my-contrast-color(#000)")]
    public void TryResolve_Rejects_Non_Functions_And_Unparseable_Arguments(string input)
    {
        Assert.False(CssContrastColor.TryResolve(input, out _));
    }

    // The function is a <color>, so the ordinary colour parser must accept it.
    [Fact(Timeout = 600000)]
    public void TryParseColor_Accepts_The_Function()
    {
        Assert.True(CssValueParser.TryParseColor("contrast-color(#000)", out var color));
        Assert.Equal(White, color);
    }

    // Wired in the same pass: system colours are <color> keywords too, so TryParseColor now routes
    // to CssSystemColors. Asserted with Field/FieldText because those resolve both before and
    // after patch 0036 fills in the rest of the CSS Color 4 §6 table (problem 28) — this test must
    // not depend on whether that patch has been applied.
    [Fact(Timeout = 600000)]
    public void TryParseColor_Routes_To_System_Colors()
    {
        Assert.True(CssValueParser.TryParseColor("Field", out var field));
        Assert.Equal(White, field);
        Assert.True(CssValueParser.TryParseColor("FieldText", out var fieldText));
        Assert.Equal(Black, fieldText);
    }

    [Fact(Timeout = 600000)]
    public void ResolveFunctions_Rewrites_Occurrences_In_Place()
    {
        Assert.Equal(
            "1px solid rgb(255, 255, 255)",
            CssContrastColor.ResolveFunctions("1px solid contrast-color(#000)"));

        Assert.Equal(
            "rgb(255, 255, 255) rgb(0, 0, 0)",
            CssContrastColor.ResolveFunctions("contrast-color(black) contrast-color(white)"));
    }

    [Theory]
    // No occurrence, or an argument that does not parse — left byte-identical so an unsupported
    // colour syntax degrades to the pre-support behaviour rather than resolving to something wrong.
    [InlineData("1px solid red")]
    [InlineData("contrast-color(color-mix(in srgb, red, blue))")]
    [InlineData("")]
    public void ResolveFunctions_Leaves_Unresolvable_Input_Untouched(string input)
    {
        Assert.Equal(input, CssContrastColor.ResolveFunctions(input));
    }
}
