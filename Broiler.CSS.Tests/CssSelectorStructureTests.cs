namespace Broiler.CSS.Tests;

/// <summary>
/// <see cref="CssSelector"/> reports the compounds it was parsed from as offsets into its own
/// <see cref="CssSelector.Text"/>, together with the combinators between them. Offsets rather than
/// re-serialised pieces, because the point is to rewrite one part of a selector and leave every
/// other part character for character as it was written.
/// </summary>
public sealed class CssSelectorStructureTests
{
    private static CssSelector Parse(string selector) =>
        Assert.Single(CssSelectorParser.Parse(selector).Selectors);

    private static string[] CompoundTexts(CssSelector selector) =>
        selector.Compounds.Select(compound => selector.Text[compound.Start..compound.End]).ToArray();

    [Theory]
    [InlineData("div", "div")]
    [InlineData("#main .row > td.cell", "#main;.row;td.cell")]
    [InlineData("a + b ~ c", "a;b;c")]
    [InlineData("col || td", "col;td")]
    [InlineData("a[title~=\"x > y\"] + b", "a[title~=\"x > y\"];b")]
    [InlineData("li:nth-child(2n + 1) p", "li:nth-child(2n + 1);p")]
    [InlineData(":is(a > b) span", ":is(a > b);span")]
    [InlineData(".a\\ b .c", ".a\\ b;.c")]
    [InlineData("div/* c */.a", "div/* c */.a")]
    [InlineData("svg|rect", "svg|rect")]
    [InlineData("*", "*")]
    public void Compounds_Are_The_Original_Text_Sliced(string selector, string expected)
    {
        Assert.Equal(expected.Split(';'), CompoundTexts(Parse(selector)));
    }

    [Theory]
    [InlineData("div p", CssCombinator.Descendant)]
    [InlineData("div>p", CssCombinator.Child)]
    [InlineData("div > p", CssCombinator.Child)]
    [InlineData("div\n\t>\n\tp", CssCombinator.Child)]
    [InlineData("div + p", CssCombinator.NextSibling)]
    [InlineData("div ~ p", CssCombinator.SubsequentSibling)]
    [InlineData("div || p", CssCombinator.Column)]
    public void The_Combinator_Between_Two_Compounds_Is_Reported(string selector, CssCombinator expected)
    {
        var parsed = Parse(selector);

        Assert.Equal(2, parsed.Compounds.Count);
        Assert.Equal(expected, Assert.Single(parsed.Combinators));
    }

    [Theory]
    [InlineData("div")]
    [InlineData("#main .row > td.cell:hover")]
    [InlineData("a[title~=\"x > y\"] + b || c")]
    [InlineData("> div")]
    [InlineData("div > ")]
    [InlineData("div >> p")]
    public void There_Is_Always_One_Fewer_Combinator_Than_Compound(string selector)
    {
        var parsed = Parse(selector);

        Assert.Equal(parsed.Compounds.Count - 1, parsed.Combinators.Count);
    }

    [Theory]
    [InlineData("div", "div")]
    [InlineData("div.a", "div")]
    [InlineData("*", "*")]
    [InlineData("*.a", "*")]
    [InlineData("svg|rect.a", "svg|rect")]
    [InlineData("*|rect", "*|rect")]
    [InlineData("|rect", "|rect")]
    [InlineData("svg|*", "svg|*")]
    [InlineData("div\\ box.a", "div\\ box")]
    [InlineData(".a", "")]
    [InlineData("#id", "")]
    [InlineData("[title]", "")]
    [InlineData(":hover", "")]
    [InlineData("::before", "")]
    public void A_Compounds_Type_Selector_Is_Delimited(string selector, string expected)
    {
        var parsed = Parse(selector);
        var compound = Assert.Single(parsed.Compounds);

        Assert.Equal(expected, parsed.Text[compound.Start..compound.TypeSelectorEnd]);
    }

    [Theory]
    [InlineData("div::before", 3)]
    [InlineData("::before", 0)]
    [InlineData("div.a::first-line", 5)]
    [InlineData("div", -1)]
    [InlineData("div:hover", -1)]
    [InlineData("div:before", -1)]
    [InlineData("a[href=\"::x\"]", -1)]
    [InlineData("a:not(b::before)", -1)]
    public void A_Compounds_Pseudo_Element_Is_Located(string selector, int expected)
    {
        var compound = Assert.Single(Parse(selector).Compounds);

        Assert.Equal(expected, compound.PseudoElementStart);
    }

    [Fact]
    public void The_Subject_Is_The_Rightmost_Compound()
    {
        var parsed = Parse("#main .row > td.cell:hover");
        Assert.True(parsed.Subject.HasValue);

        var subject = parsed.Subject!.Value;
        Assert.Equal("td.cell:hover", parsed.Text[subject.Start..subject.End]);
        Assert.Equal(parsed.Compounds[^1], subject);
    }

    [Fact]
    public void A_Selector_With_No_Compound_Has_No_Subject()
    {
        var selector = new CssSelector("/* nothing here */", default);

        Assert.Empty(selector.Compounds);
        Assert.Empty(selector.Combinators);
        Assert.Null(selector.Subject);
    }

    /// <summary>
    /// The case the model exists for: appending a marker to the subject compound of every selector
    /// in a list, with everything else — white space, comments, quoting, the combinators — left
    /// exactly as written.
    /// </summary>
    [Theory]
    [InlineData("#main .row > td.cell::before", "#main .row > td[data-scope].cell::before")]
    [InlineData(".a", "[data-scope].a")]
    [InlineData("svg|rect", "svg|rect[data-scope]")]
    [InlineData("a[title~=\"x , y\"] + b", "a[title~=\"x , y\"] + b[data-scope]")]
    [InlineData("li:nth-child(2n + 1)", "li[data-scope]:nth-child(2n + 1)")]
    public void A_Subject_Compound_Can_Be_Narrowed_Without_Disturbing_The_Rest(
        string selector,
        string expected)
    {
        var scoped = CssSelectorParser.Parse(selector).Selectors
            .Select(static parsed =>
            {
                var subject = parsed.Subject!.Value;
                return parsed.Text[..subject.TypeSelectorEnd] + "[data-scope]" + parsed.Text[subject.TypeSelectorEnd..];
            });

        Assert.Equal(expected, string.Join(", ", scoped));
    }

    /// <summary>
    /// The rewrite the other way round: the pieces a caller builds go back into a rule without a
    /// round trip through text, which the public <see cref="CssStyleRule"/> constructor could not
    /// be handed before.
    /// </summary>
    [Fact]
    public void A_Rewritten_Selector_List_Can_Be_Given_Back_To_A_Rule()
    {
        var rule = Assert.IsType<CssStyleRule>(new CssParser().ParseStyleSheet(".a > .b { color: red }").Rules[0]);
        var scoped = rule.Selectors.Selectors.Select(static selector =>
        {
            var subject = selector.Subject!.Value;
            var text = selector.Text[..subject.TypeSelectorEnd] + "[data-scope]" + selector.Text[subject.TypeSelectorEnd..];
            return new CssSelector(text, CssSelectorParser.CalculateSpecificity(text));
        });

        var serialized = CssSerializer.Serialize(new CssStyleRule(new CssSelectorList(scoped), rule.Declarations, rule.Range));

        Assert.StartsWith(".a > [data-scope].b {", serialized, StringComparison.Ordinal);
        Assert.Contains("color: red;", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Selector_List_Keeps_Each_Selectors_Own_Offsets()
    {
        var selectors = CssSelectorParser.Parse(".a > .b, div  p").Selectors;

        Assert.Equal(2, selectors.Count);
        Assert.Equal([".a", ".b"], CompoundTexts(selectors[0]));
        Assert.Equal(CssCombinator.Child, Assert.Single(selectors[0].Combinators));
        Assert.Equal(["div", "p"], CompoundTexts(selectors[1]));
        Assert.Equal(CssCombinator.Descendant, Assert.Single(selectors[1].Combinators));
    }
}
