using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

public sealed class CssSelectorMatcherTests
{
    [Fact]
    public void Matches_Compound_Combinator_And_Attribute_Selectors()
    {
        var tree = CreateTree();
        var matcher = new CssSelectorMatcher();

        Assert.True(matcher.Matches(
            tree.First,
            "#host > p.item[data-state='active']:first-child"));
        Assert.True(matcher.Matches(tree.Note, "p.item > span.note"));
        Assert.True(matcher.Matches(tree.Second, "p + p"));
        Assert.True(matcher.Matches(tree.Second, "#host p:last-child"));
        Assert.False(matcher.Matches(tree.Second, "p:first-child"));
    }

    [Fact]
    public void Matches_Level_Four_Functional_Pseudo_Classes()
    {
        var tree = CreateTree();
        var matcher = new CssSelectorMatcher();

        Assert.True(matcher.Matches(tree.First, "p:has(> span.note)"));
        Assert.True(matcher.Matches(tree.First, "p:is(.item, #missing)"));
        Assert.True(matcher.Matches(tree.First, "p:where(.item)"));
        Assert.True(matcher.Matches(tree.First, "p:not(.missing)"));
        Assert.True(matcher.Matches(tree.First, "p:nth-child(1 of .item)"));
        Assert.True(matcher.Matches(tree.Second, "p:nth-last-child(1)"));
    }

    [Fact]
    public void Unknown_PseudoClass_Invalidates_Selector_But_Recognized_Ones_Stay_Lenient()
    {
        var tree = CreateTree();
        var matcher = new CssSelectorMatcher();

        // An unrecognized pseudo-class is an invalid selector; per the Selectors
        // spec the whole rule must be ignored, so it must not match anything.
        // (Previously it matched every element — the WPT "invalid selector is
        // ignored" idiom `:bogus { background: red }` painted red everywhere.)
        Assert.False(matcher.Matches(tree.First, ":unknownpseudo"));
        Assert.False(matcher.Matches(tree.First, "p:unknownpseudo"));
        Assert.False(matcher.Matches(tree.First, "p.item:totally-made-up"));

        // Recognized-but-unmodeled pseudo-classes stay lenient (match as before),
        // so this is a strict narrowing that only rejects genuinely invalid names.
        Assert.True(matcher.Matches(tree.First, "p:defined"));
        Assert.True(matcher.Matches(tree.First, "p:read-only"));

        // Vendor-prefixed pseudo-classes are extensions, not typos — kept lenient.
        Assert.True(matcher.Matches(tree.First, "p:-webkit-anything"));
    }

    [Fact]
    public void Matches_Root_Scope_Empty_Language_And_Form_State()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        html.SetAttribute("lang", "en-US");
        var body = document.CreateElement("body");
        var empty = document.CreateElement("div");
        var checkbox = document.CreateElement("input");
        checkbox.SetAttribute("type", "checkbox");
        document.AppendChild(html);
        html.AppendChild(body);
        body.AppendChild(empty);
        body.AppendChild(checkbox);

        var matcher = new CssSelectorMatcher(new CheckedStateProvider(checkbox));

        Assert.True(matcher.Matches(html, ":root"));
        Assert.False(matcher.Matches(body, ":root"));
        Assert.True(matcher.Matches(body, ":not(:root)"));
        Assert.True(matcher.Matches(empty, ":scope:empty:lang(en)", empty));
        Assert.True(matcher.Matches(empty, ":lang(en-*-US)"));
        Assert.True(matcher.Matches(checkbox, "input:enabled:checked"));
    }

    [Fact]
    public void Has_Matches_Nth_Child_And_Nested_Functions()
    {
        var document = new DomDocument();
        var target = document.CreateElement("div");
        target.Id = "target";
        document.AppendChild(target);
        for (var index = 0; index < 3; index++)
        {
            var item = document.CreateElement("div");
            item.ClassName = "item";
            target.AppendChild(item);
        }

        var matcher = new CssSelectorMatcher();

        Assert.True(matcher.Matches(target, "#target:has(.item:nth-child(3))"));
        Assert.True(matcher.Matches(target, "#target:has(:is(.item + .item + .item))"));
    }

    [Fact]
    public void Specificity_Is_Owned_By_The_Css_Kernel()
    {
        Assert.Equal(
            new CssSpecificity(1, 1, 1),
            CssSelectorParser.CalculateSpecificity("p:nth-child(2 of #featured, .card)"));
    }

    private static TestTree CreateTree()
    {
        var document = new DomDocument();
        var host = document.CreateElement("div");
        host.Id = "host";
        var first = document.CreateElement("p");
        first.Id = "featured";
        first.ClassName = "item card";
        first.SetAttribute("data-state", "active");
        var note = document.CreateElement("span");
        note.ClassName = "note";
        var second = document.CreateElement("p");
        second.ClassName = "item";

        document.AppendChild(host);
        host.AppendChild(first);
        first.AppendChild(note);
        host.AppendChild(second);
        return new TestTree(first, note, second);
    }

    private sealed record TestTree(DomElement First, DomElement Note, DomElement Second);

    private sealed class CheckedStateProvider(DomElement checkedElement) : ICssSelectorStateProvider
    {
        public bool? IsChecked(DomElement element) =>
            ReferenceEquals(element, checkedElement);
    }
}
