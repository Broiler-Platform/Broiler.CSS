using Broiler.CSS;

namespace Broiler.CSS.Tests;

/// <summary>
/// Behaviour-parity guard for <see cref="PositionTryRule"/>, the third neutral
/// anchor-positioning syntax model promoted out of the HtmlBridge anchor resolver
/// (Phase 5 item 4). Pins the exact <c>@position-try</c> at-rule + fallback-list
/// grammar ported from the bridge: comment stripping before declaration parsing,
/// case-insensitive declaration names, last-wins on duplicate rule/decl names, and
/// the comma-split fallback list (trimmed, empties preserved).
/// </summary>
public sealed class PositionTryRuleTests
{
    [Fact]
    public void Parse_SingleRule_ExtractsDeclarations()
    {
        var rules = PositionTryRule.Parse("@position-try --a { top: 10px; left: 20px }");
        Assert.True(rules.ContainsKey("--a"));
        Assert.Equal("10px", rules["--a"]["top"]);
        Assert.Equal("20px", rules["--a"]["left"]);
    }

    [Fact]
    public void Parse_MultipleRules()
    {
        var rules = PositionTryRule.Parse(
            "@position-try --a { top: 1px } @position-try --b { bottom: 2px }");
        Assert.Equal(2, rules.Count);
        Assert.Equal("1px", rules["--a"]["top"]);
        Assert.Equal("2px", rules["--b"]["bottom"]);
    }

    [Fact]
    public void Parse_StripsCommentsBeforeDeclarations()
    {
        // A comment inside the body contains ':' and ';' that would corrupt parsing.
        var rules = PositionTryRule.Parse(
            "@position-try --a { /* 2: position right; here */ right: 5px }");
        Assert.Single(rules["--a"]);
        Assert.Equal("5px", rules["--a"]["right"]);
    }

    [Fact]
    public void Parse_DeclarationNamesAreCaseInsensitive()
    {
        var rules = PositionTryRule.Parse("@position-try --a { TOP: 3px }");
        Assert.Equal("3px", rules["--a"]["top"]);
        Assert.Equal("3px", rules["--a"]["ToP"]);
    }

    [Fact]
    public void Parse_LastDuplicateRuleWins()
    {
        var rules = PositionTryRule.Parse(
            "@position-try --a { top: 1px } @position-try --a { top: 9px }");
        Assert.Single(rules);
        Assert.Equal("9px", rules["--a"]["top"]);
    }

    [Fact]
    public void Parse_RuleNamesAreCaseSensitive()
    {
        var rules = PositionTryRule.Parse(
            "@position-try --a { top: 1px } @position-try --A { top: 2px }");
        Assert.Equal(2, rules.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".x { color: red }")]
    public void Parse_NoRules_ReturnsEmpty(string css)
    {
        Assert.Empty(PositionTryRule.Parse(css));
    }

    [Fact]
    public void Parse_SkipsBlankAndColonlessDeclarations()
    {
        var rules = PositionTryRule.Parse("@position-try --a { top: 1px; ; garbage; left: 2px }");
        Assert.Equal(2, rules["--a"].Count);
        Assert.Equal("1px", rules["--a"]["top"]);
        Assert.Equal("2px", rules["--a"]["left"]);
    }

    [Theory]
    [InlineData("--a, --b, --c", new[] { "--a", "--b", "--c" })]
    [InlineData("  --a ,--b ", new[] { "--a", "--b" })]
    [InlineData("--only", new[] { "--only" })]
    [InlineData("--a,,--b", new[] { "--a", "", "--b" })] // empties preserved
    public void ParseFallbackList_SplitsAndTrims(string value, string[] expected)
    {
        Assert.Equal(expected, PositionTryRule.ParseFallbackList(value));
    }
}
