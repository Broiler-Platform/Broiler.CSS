namespace Broiler.CSS.Tests;

/// <summary>
/// <see cref="CssSyntax.TrimPreservingEscapes"/> trims a selector without deleting whitespace that a
/// backslash escapes. A plain <see cref="string.Trim()"/> turned Acid3's <c>#\ </c> (the id <c>" "</c>)
/// into <c>#\</c>, an empty id, so test 28's "FAIL" was never hidden.
/// </summary>
public sealed class CssSyntaxTrimTests
{
    [Theory]
    [InlineData("  div  ", "div")]
    [InlineData("#\\  ", "#\\ ")]
    [InlineData("#\\ ", "#\\ ")]
    [InlineData("#\\\t\n", "#\\\t")]
    [InlineData("#\\20  ", "#\\20 ")]
    [InlineData("#a\\ b ", "#a\\ b")]
    // An escaped backslash escapes nothing after it, so the space that follows is ordinary.
    [InlineData("#a\\\\ ", "#a\\\\")]
    [InlineData("\\ ", "\\ ")]
    [InlineData("   ", "")]
    public void Trims_Whitespace_But_Not_Escaped_Whitespace(string text, string expected)
    {
        Assert.Equal(expected, CssSyntax.TrimPreservingEscapes(text));
    }

    [Theory]
    [InlineData("\\ x", 0, 2)]
    [InlineData("\\20 x", 0, 4)]
    [InlineData("\\000020x", 0, 7)]
    [InlineData("\\1234567", 0, 7)]
    [InlineData("a\\", 1, 2)]
    public void ConsumeEscape_Takes_The_Escape_Whole(string text, int index, int expected)
    {
        Assert.Equal(expected, CssSyntax.ConsumeEscape(text, index));
    }

    [Fact]
    public void Parser_Keeps_An_Escaped_Trailing_Space_In_A_Rule_Selector()
    {
        // Acid3: an ID selector for a single space — backslash, space, then the separating space.
        var sheet = new CssParser().ParseStyleSheet("#\\  { color: transparent; }");

        var rule = Assert.IsType<CssStyleRule>(Assert.Single(sheet.Rules));
        Assert.Equal("#\\ ", Assert.Single(rule.Selectors.Selectors).Text);
    }

    [Theory]
    [InlineData("#\\ ", "#\\ ")]
    [InlineData("#a\\ b , #\\  ", "#a\\ b;#\\ ")]
    public void SelectorParser_Keeps_Escaped_Trailing_Whitespace(string list, string expected)
    {
        var texts = CssSelectorParser.Parse(list).Selectors.Select(selector => selector.Text);
        Assert.Equal(expected.Split(';'), texts);
    }

    [Theory]
    // `#q\ r` is the id "q r", not `#q` with a descendant `r`: it must not be keyed on type `r`.
    [InlineData("#q\\ r")]
    [InlineData("#\\ ")]
    public void An_Escaped_Space_Is_Not_A_Descendant_Combinator_For_The_Rule_Key(string selector)
    {
        Assert.Equal(CssSelectorKey.Universal, CssSelectorParser.GetKey(selector));
    }

    [Fact]
    public void An_Escaped_Space_Does_Not_Split_Specificity_Into_Two_Compounds()
    {
        // One id; before the fix `#q\ r` counted the type `r` as well.
        Assert.Equal(CssSelectorParser.CalculateSpecificity("#q"), CssSelectorParser.CalculateSpecificity("#q\\ r"));
    }
}
