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
    [InlineData("display", "var(--d)", true)]
    [InlineData("color", "env(safe-area-inset-top)", true)]
    // Empty / whitespace is never acceptable.
    [InlineData("color", "", false)]
    [InlineData("color", "   ", false)]
    // Unknown vendor-prefixed color values are rejected; standard prefixes pass.
    [InlineData("color", "-acid3-bogus", false)]
    [InlineData("color", "-webkit-link", true)]
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
