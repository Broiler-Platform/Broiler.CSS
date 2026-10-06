namespace Broiler.CSS.Dom.Tests;

/// <summary>
/// What a stylesheet set can see of an element's attributes — multithreading item #14's second
/// half.
/// </summary>
/// <remarks>
/// The asymmetry these are written around: a set that says "affects" when it does not costs a
/// rebuild nobody needed, and a set that says "does not affect" when it does renders a stale page.
/// So every case that could plausibly be missed by a flat scan — a class named only inside
/// <c>:not()</c>, an attribute read only by <c>attr()</c>, a selector reachable only through a
/// combinator, one buried in a conditional group — is asserted on the "affects" side, and the
/// elidable side is kept to what is demonstrably outside the sheet.
/// </remarks>
public class CascadeInvalidationSetTests
{
    private static CascadeInvalidationSet Set(params string[] css)
    {
        var parser = new CssParser();
        var sheets = new List<CssStyleSheet?>(css.Length);
        foreach (var text in css)
            sheets.Add(parser.ParseStyleSheet(text));

        return CascadeInvalidationSet.Build(sheets);
    }

    [Fact]
    public void An_attribute_no_selector_mentions_does_not_affect_style()
    {
        var set = Set(".a{color:red}div p{margin:0}");

        Assert.False(set.IsConservative);
        Assert.False(set.AffectsStyle("data-probe", null, "1"));
    }

    [Fact]
    public void An_attribute_a_selector_filters_on_does()
    {
        var set = Set("[data-k=\"3\"]{color:red}");

        Assert.True(set.AffectsStyle("data-k", "2", "3"));

        // The value is not modelled: `[data-k="3"]` and a write of `9` still says "affects". One
        // unnecessary rebuild, in the direction that cannot render the wrong page.
        Assert.True(set.AffectsStyle("data-k", "8", "9"));
    }

    [Fact]
    public void An_attribute_only_a_declaration_reads_does()
    {
        // attr() is the case with no selector to find: nothing in this sheet mentions data-label as
        // a filter, and a write to it still changes what the page renders.
        var set = Set("p::before{content:attr(data-label)}");

        Assert.True(set.AffectsStyle("data-label", null, "x"));
        Assert.False(set.AffectsStyle("data-other", null, "x"));
    }

    [Fact]
    public void An_attr_with_a_type_argument_is_read_the_same_way()
    {
        var set = Set("img{width:attr(data-w type(<length>), 100px)}");

        Assert.True(set.AffectsStyle("data-w", null, "40px"));
    }

    [Theory]
    // Every one of these is a class that changes what matches from somewhere other than the
    // subject compound, which is why the scan is flat rather than keyed on the subject.
    [InlineData(".target span{color:red}")]
    [InlineData("div.target > p{color:red}")]
    [InlineData(".target + p{color:red}")]
    [InlineData("p:not(.target){color:red}")]
    [InlineData("p:is(.target, .other){color:red}")]
    [InlineData("div:has(.target){color:red}")]
    [InlineData("@media screen{.target{color:red}}")]
    [InlineData("@supports (display:grid){@media print{.target{color:red}}}")]
    public void A_class_named_anywhere_in_a_selector_affects_style(string css)
    {
        var set = Set(css);

        Assert.True(set.AffectsStyle("class", "", "target"));
    }

    [Fact]
    public void A_class_write_is_answered_by_the_tokens_that_changed()
    {
        var set = Set(".styled{color:red}");

        Assert.True(set.AffectsStyle("class", "", "styled"));
        Assert.True(set.AffectsStyle("class", "styled", ""));
        Assert.False(set.AffectsStyle("class", "", "unstyled"));
        Assert.False(set.AffectsStyle("class", "unstyled", "other-unstyled"));

        // A token the sheet names but that both sides carry has not changed, so it cannot have
        // changed a match — the token diff is what makes this different from a name test.
        Assert.False(set.AffectsStyle("class", "styled a", "styled b"));

        // ...and reordering is not a change at all.
        Assert.False(set.AffectsStyle("class", "styled a", "a styled"));
    }

    [Fact]
    public void An_attribute_selector_on_class_defeats_the_token_test()
    {
        // `[class*="btn"]` cannot be answered token by token, so filing the *name* has to
        // short-circuit the diff. Without this, adding "btn-primary" would look elidable.
        var set = Set("[class*=\"btn\"]{color:red}");

        Assert.True(set.AffectsStyle("class", "", "btn-primary"));
    }

    [Fact]
    public void An_id_write_is_answered_by_the_value()
    {
        var set = Set("#main{color:red}");

        Assert.True(set.AffectsStyle("id", null, "main"));
        Assert.True(set.AffectsStyle("id", "main", "other"));
        Assert.False(set.AffectsStyle("id", "other", "another"));
    }

    [Fact]
    public void Case_is_ignored_which_over_approximates_in_the_safe_direction()
    {
        var set = Set(".Row{color:red}[Data-K]{color:blue}");

        Assert.True(set.AffectsStyle("class", "", "row"));
        Assert.True(set.AffectsStyle("DATA-k", null, "1"));
    }

    [Theory]
    // Each of these is something the scanner does not model. The whole set goes conservative
    // rather than the one selector being dropped, because a dropped selector is a name missing
    // from the set and a missing name is a stale render.
    [InlineData(".a\\.b{color:red}")]
    [InlineData("[svg|href]{color:red}")]
    public void Anything_the_scanner_does_not_model_makes_the_whole_set_conservative(string css)
    {
        var set = Set(css);

        Assert.True(set.IsConservative);
        Assert.True(set.AffectsStyle("data-probe", null, "1"));
        Assert.True(set.AffectsStyle("class", "", "anything"));
    }

    [Theory]
    [InlineData("[unclosed{color:red}")]
    [InlineData("p[a=\"x{color:red}")]
    public void An_unbalanced_selector_never_reaches_the_scanner(string css)
    {
        // Worth pinning rather than assuming: the scanner has a branch for an unterminated `[` or
        // string, and it is unreachable through this parser — a rule whose selector does not close
        // is dropped, so there is no selector text to scan and nothing for the cascade to match
        // either. The guard stays because the scanner's contract is about the text it is handed,
        // not about who hands it over; this says what today's parser does with it.
        var sheet = new CssParser().ParseStyleSheet(css);
        Assert.Empty(sheet.Rules);

        var set = Set(css);
        Assert.False(set.IsConservative);
    }

    [Fact]
    public void An_attr_whose_argument_cannot_be_read_does_too()
    {
        var set = Set("p::before{content:attr()}");

        Assert.True(set.IsConservative);
    }

    [Fact]
    public void Quoted_text_files_nothing()
    {
        // The scan is flat, so a '.' or a '#' inside a string is the way it could file a class or an
        // id that does not exist. Filing junk is harmless for correctness and would quietly make
        // real writes non-elidable, which is exactly the kind of miss that never shows up as a bug.
        var set = Set("[title=\"a.b#c\"]{color:red}");

        Assert.False(set.IsConservative);
        Assert.Equal(0, set.ClassNameCount);
        Assert.Equal(0, set.IdCount);
        Assert.True(set.AffectsStyle("title", null, "x"));
    }

    [Fact]
    public void No_sheets_offered_is_not_no_sheets_exist()
    {
        Assert.True(CascadeInvalidationSet.Build(null).IsConservative);
        Assert.True(CascadeInvalidationSet.Build([]).IsConservative);
        Assert.True(CascadeInvalidationSet.Build([null]).IsConservative);
        Assert.True(CascadeInvalidationSet.Everything.AffectsStyle("data-probe", null, "1"));
    }

    [Fact]
    public void An_empty_sheet_is_scannable_and_names_nothing()
    {
        var set = Set(string.Empty);

        Assert.False(set.IsConservative);
        Assert.False(set.AffectsStyle("data-probe", null, "1"));
        Assert.False(set.AffectsStyle("class", "", "anything"));
    }

    [Fact]
    public void Every_sheet_in_the_set_is_scanned()
    {
        // The user-agent sheet and the author sheet are separate objects in the container, and a
        // set built from only one of them would elide writes the other can see.
        var set = Set("[hidden]{display:none}", ".author{color:red}");

        Assert.True(set.AffectsStyle("hidden", null, ""));
        Assert.True(set.AffectsStyle("class", "", "author"));
        Assert.False(set.AffectsStyle("data-probe", null, "1"));
    }

    [Fact]
    public void A_missing_attribute_name_is_never_elidable()
    {
        var set = Set(".a{color:red}");

        Assert.True(set.AffectsStyle(null, null, "1"));
        Assert.True(set.AffectsStyle("", null, "1"));
    }
}
