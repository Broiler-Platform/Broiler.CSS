namespace Broiler.CSS.Dom.Tests;

public sealed class CssDeclarationValidatorTests
{
    [Theory]
    // Closed-keyword properties: valid keywords are accepted.
    [InlineData("display", "block", true)]
    [InlineData("position", "absolute", true)]
    [InlineData("float", "inline-start", true)]
    [InlineData("visibility", "collapse", true)]
    [InlineData("box-sizing", "border-box", true)]
    [InlineData("font-style", "oblique", true)]
    // Closed-keyword properties: invalid keywords are rejected (error recovery).
    [InlineData("display", "bogus", false)]
    [InlineData("position", "downwards", false)]
    [InlineData("float", "middle", false)]
    [InlineData("visibility", "translucent", false)]
    [InlineData("box-sizing", "padding-box", false)]
    // The richer CSS.Dom table (superset of the old bridge copy) accepts these.
    [InlineData("display", "inline table", true)]        // two-value display syntax
    [InlineData("display", "ruby-base", true)]           // internal ruby display
    [InlineData("display", "inline-table", true)]
    [InlineData("border-style", "solid dashed", true)]   // border-style list (1..4)
    [InlineData("display", "grid-lanes", false)]         // experimental, deliberately rejected
    // Open properties accept any non-empty value.
    [InlineData("color", "rebeccapurple", true)]
    [InlineData("width", "42px", true)]
    [InlineData("--custom", "anything goes", true)]
    // CSS-wide keywords and deferred substitutions are always valid.
    [InlineData("display", "inherit", true)]
    [InlineData("display", "revert", true)]
    [InlineData("display", "revert-layer", true)]
    [InlineData("position", "revert-layer", true)]
    [InlineData("display", "var(--d)", true)]
    [InlineData("color", "env(safe-area-inset-top)", true)]
    // Empty / whitespace is never acceptable.
    [InlineData("color", "", false)]
    [InlineData("color", "   ", false)]
    // Unknown vendor-prefixed color values are rejected; standard prefixes pass.
    [InlineData("color", "-acid3-bogus", false)]
    [InlineData("color", "-webkit-link", true)]
    // A legacy (comma-separated) rgb()/hsl() is checked against the colour parser: hsl() needs
    // percentages, so Acid3's `hsla(0, 0, 0, 1)` is invalid and dropped. Everything the parser
    // cannot judge — modern syntax, nested math, substitutions, newer colour functions — passes.
    [InlineData("color", "hsla(0, 0, 0, 1)", false)]
    [InlineData("color", "HSL(0, 0, 0)", false)]
    [InlineData("color", "rgb(0, 0, zero)", false)]
    [InlineData("border-color", "red hsla(0, 0, 0, 1)", false)]
    [InlineData("color", "hsla(0, 0%, 0%, 1)", true)]
    [InlineData("color", "hsl(120deg, 100%, 50%)", true)]
    [InlineData("color", "rgba(255, 0, 0, 0.5)", true)]
    [InlineData("color", "rgb(10%, 20%, 30%)", true)]
    [InlineData("border-color", "red hsla(0, 0%, 0%, 1)", true)]
    [InlineData("color", "rgb(0 128 128 / 50%)", true)]
    [InlineData("color", "hsl(120 100 50)", true)]
    [InlineData("color", "rgb(calc(255), 0, 0)", true)]
    [InlineData("color", "rgba(var(--rgb), 0.5)", true)]
    [InlineData("color", "hsl(from red h s l)", true)]
    [InlineData("color", "oklch(0.6 0.12 200)", true)]
    [InlineData("color", "color-mix(in srgb, currentcolor 50%, red)", true)]
    [InlineData("color", "light-dark(rgb(0, 0, 0), white)", true)]
    [InlineData("color", "transparent", true)]
    [InlineData("color", "currentcolor", true)]
    [InlineData("color", "Canvas", true)]
    [InlineData("color", "#0f08", true)]
    // CSS Values 4 calc-type-checking: a bare <number> (unitless, incl. 0) is a
    // type error as a top-level min()/max()/clamp() argument in a length context,
    // so the declaration is dropped (WPT css-values/max-unitless-zero-invalid).
    [InlineData("height", "min(0, 100%)", false)]
    [InlineData("height", "min(100%)", true)]
    [InlineData("width", "max(0, 100px)", false)]
    [InlineData("height", "clamp(0, 50%, 100%)", false)]
    [InlineData("margin-left", "min(1, 10px)", false)]
    [InlineData("top", "max(50%, min(0, 10px))", false)]   // caught in the nested min()
    // Valid math is untouched: 0px is a length, calc() may carry a <number>, and a
    // number outside the length-property set (opacity/line-height) is not policed.
    [InlineData("height", "min(0px, 100%)", true)]
    [InlineData("width", "max(calc(100% / 3), 50px)", true)]
    [InlineData("height", "clamp(10px, 50%, 100px)", true)]
    [InlineData("width", "min(50%, var(--x))", true)]      // substitution deferred
    [InlineData("opacity", "clamp(0, 0.5, 1)", true)]      // <number> property, allowed
    [InlineData("line-height", "min(1.5, 2)", true)]       // <number> property, allowed
    public void IsAcceptableDeclarationValue_Matches_The_Cascade_Table(
        string property, string value, bool expected)
    {
        Assert.Equal(expected, CssDeclarationValidator.IsAcceptableDeclarationValue(property, value));
    }

    [Fact]
    public void IsAcceptableDeclarationValue_Is_Case_Insensitive_On_Property_And_Value()
    {
        Assert.True(CssDeclarationValidator.IsAcceptableDeclarationValue("DISPLAY", "BLOCK"));
        Assert.False(CssDeclarationValidator.IsAcceptableDeclarationValue("Display", "Bogus"));
    }

    [Fact]
    public void IsAcceptableDeclarationValue_Rejects_Null_Property()
    {
        Assert.Throws<ArgumentNullException>(
            () => CssDeclarationValidator.IsAcceptableDeclarationValue(null!, "block"));
    }

    [Fact]
    public void IsAcceptableDeclarationValue_Treats_Null_Value_As_Unacceptable()
    {
        Assert.False(CssDeclarationValidator.IsAcceptableDeclarationValue("color", null!));
    }
}
