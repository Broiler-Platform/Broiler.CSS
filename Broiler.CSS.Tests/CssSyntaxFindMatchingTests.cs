namespace Broiler.CSS.Tests;

/// <summary>
/// <see cref="CssSyntax.FindMatching"/> answers <c>-1</c> when the closing character never arrives.
/// It used to answer <c>text.Length - 1</c>, which is a real index into every non-empty input: the
/// <c>index &lt; 0</c> test that any search invites could not fire, so an unterminated function
/// looked like a successful match ending at the last character.
/// </summary>
public sealed class CssSyntaxFindMatchingTests
{
    [Theory]
    [InlineData("translateY(20px)", 10, '(', ')', 15)]
    [InlineData("translate(calc(1px + 2px), 0)", 9, '(', ')', 28)]
    [InlineData("[title=\"a)b\"]", 0, '[', ']', 12)]
    [InlineData("(/* ) */)", 0, '(', ')', 8)]
    [InlineData("(a\\)b)", 0, '(', ')', 5)]
    public void Finds_The_Matching_Close_Past_Nesting_Strings_Comments_And_Escapes(
        string text,
        int openingIndex,
        char open,
        char close,
        int expected)
    {
        Assert.Equal(expected, CssSyntax.FindMatching(text, openingIndex, open, close));
    }

    [Theory]
    [InlineData("translateY(", 10)]
    [InlineData("translateY(20px", 10)]
    [InlineData("translate(calc(1px", 9)]
    [InlineData("(\"unterminated string", 0)]
    [InlineData("(/* unterminated comment", 0)]
    [InlineData("", 0)]
    public void Answers_Minus_One_When_Nothing_Matches(string text, int openingIndex)
    {
        Assert.Equal(-1, CssSyntax.FindMatching(text, openingIndex, '(', ')'));
    }

    /// <summary>
    /// The specificity scan advances past an attribute selector by the returned index, so a failure
    /// answering <c>-1</c> would send it back to the start of the compound and spin there. These
    /// selectors are malformed; what is pinned is that they terminate, and that the simple selectors
    /// before the unterminated one are still counted.
    /// </summary>
    [Theory]
    [InlineData("[", 0, 1, 0)]
    [InlineData("[title", 0, 1, 0)]
    [InlineData("div.a[title=\"x", 0, 2, 1)]
    [InlineData("#hero [data-x", 1, 1, 0)]
    [InlineData("a[href][target", 0, 2, 1)]
    public void An_Unterminated_Attribute_Selector_Terminates_The_Specificity_Scan(
        string selector,
        int ids,
        int classes,
        int types)
    {
        Assert.Equal(new CssSpecificity(ids, classes, types), CssSelectorParser.CalculateSpecificity(selector));
    }

    /// <summary>
    /// An unterminated pseudo-class function takes the whole remainder as its argument. The
    /// <c>text.Length - 1</c> landing used to cut the last character off it, so <c>:is(div p</c>
    /// weighed its argument as <c>div </c> — one type selector rather than two.
    /// </summary>
    [Theory]
    [InlineData(":is(div p", 0, 0, 2)]
    [InlineData(":not(.a .bc", 0, 2, 0)]
    [InlineData(":is(#hero", 1, 0, 0)]
    [InlineData(":where(#hero", 0, 0, 0)]
    public void An_Unterminated_Pseudo_Function_Keeps_Its_Whole_Argument(
        string selector,
        int ids,
        int classes,
        int types)
    {
        Assert.Equal(new CssSpecificity(ids, classes, types), CssSelectorParser.CalculateSpecificity(selector));
    }
}
