using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

/// <summary>
/// <see cref="CssSelectorMatcher.TryMatch"/> — the answer for a caller that must not act on a
/// guess.
/// <para>
/// <see cref="CssSelectorMatcher.Matches"/> is lenient on purpose: a pseudo-class the specs define
/// but this matcher does not implement, and any vendor-prefixed name, match every element, because
/// for the cascade an over-applied declaration beats a dropped one. The pseudo is then removed from
/// the compound, so a lone <c>:read-only</c> leaves no filter at all and matches the whole
/// document. A caller collecting the <c>animation-*</c> declarations that apply to one element
/// cannot use that — there, <c>:read-only { animation: spin 1s }</c> is not a conservative
/// over-application but an animation attached document-wide — and a plain <see cref="bool"/> gives
/// it no way to tell that answer apart from a real match.
/// </para>
/// <para>The lenient behaviour stays the default; these tests pin both halves.</para>
/// </summary>
public sealed class CssSelectorMatcherStrictTests
{
    private static (CssSelectorMatcher Matcher, DomElement Div, DomElement Span) CreateTree()
    {
        var document = new DomDocument();
        var host = document.CreateElement("section");
        host.Id = "host";
        var div = document.CreateElement("div");
        div.Id = "featured";
        div.ClassName = "item card";
        div.SetAttribute("data-state", "active");
        var span = document.CreateElement("span");
        span.ClassName = "note";

        document.AppendChild(host);
        host.AppendChild(div);
        div.AppendChild(span);
        return (new CssSelectorMatcher(), div, span);
    }

    [Theory]
    [InlineData(":read-only")]
    [InlineData("div:read-only")]
    [InlineData(":indeterminate")]
    [InlineData(":defined")]
    [InlineData(":host")]
    [InlineData(":-webkit-autofill")]
    [InlineData("div:-moz-whatever")]
    [InlineData(":not(:read-only)")]
    [InlineData(":is(span, :read-only)")]
    public void An_Unmodeled_Pseudo_Class_Has_No_Strict_Answer(string selector)
    {
        var (matcher, div, _) = CreateTree();

        Assert.False(matcher.TryMatch(div, selector, out var matches));
        Assert.False(matches);
    }

    /// <summary>
    /// The default is unchanged, which is what the cascade depends on: every one of these still
    /// matches an element that has nothing to do with the pseudo-class.
    /// </summary>
    [Theory]
    [InlineData(":read-only")]
    [InlineData(":host")]
    [InlineData(":-webkit-autofill")]
    public void The_Lenient_Default_Still_Matches_Everything(string selector)
    {
        var (matcher, div, _) = CreateTree();

        Assert.True(matcher.Matches(div, selector));
    }

    /// <summary>
    /// The constructs the caller's own fallback answers <c>false</c> for today — compounds,
    /// combinators, <c>*</c>, attribute and functional selectors — all have strict answers, and
    /// they are the right ones.
    /// </summary>
    [Theory]
    [InlineData("div", true)]
    [InlineData("span", false)]
    [InlineData("*", true)]
    [InlineData(".item.card", true)]
    [InlineData(".item.missing", false)]
    [InlineData("#featured", true)]
    [InlineData("[data-state]", true)]
    [InlineData("[data-state=active]", true)]
    [InlineData("[data-state=idle]", false)]
    [InlineData("section > div", true)]
    [InlineData("section > span", false)]
    [InlineData("section div", true)]
    [InlineData("div:has(> span.note)", true)]
    [InlineData("div:has(> p)", false)]
    [InlineData("div:is(.item, #missing)", true)]
    [InlineData("div:not(.missing)", true)]
    [InlineData("div:not(.item)", false)]
    [InlineData("div:nth-child(1)", true)]
    [InlineData("div:nth-child(2)", false)]
    [InlineData("div:first-child", true)]
    [InlineData("div:hover", false)]
    [InlineData("div:visited", false)]
    public void A_Modeled_Selector_Has_A_Strict_Answer(string selector, bool expected)
    {
        var (matcher, div, _) = CreateTree();

        Assert.True(matcher.TryMatch(div, selector, out var matches));
        Assert.Equal(expected, matches);
    }

    /// <summary>
    /// An unknown pseudo-class is knowable, not unmodelled: it makes the selector invalid, and the
    /// specs say an invalid selector matches nothing.
    /// </summary>
    [Theory]
    [InlineData(":bogus")]
    [InlineData("div:unknownpseudo")]
    [InlineData(":matches(div)")]
    public void An_Invalid_Pseudo_Class_Is_A_Strict_No_Match(string selector)
    {
        var (matcher, div, _) = CreateTree();

        Assert.True(matcher.TryMatch(div, selector, out var matches));
        Assert.False(matches);
    }

    /// <summary>
    /// A pseudo-element is stripped so that its rule reaches the originating element, which is the
    /// cascade's answer and not the element's own. Strictly, this matcher cannot say.
    /// </summary>
    [Theory]
    [InlineData("div::before")]
    [InlineData("div::first-line")]
    public void A_Pseudo_Element_Has_No_Strict_Answer(string selector)
    {
        var (matcher, div, _) = CreateTree();

        Assert.True(matcher.Matches(div, selector));
        Assert.False(matcher.TryMatch(div, selector, out _));
    }

    /// <summary>
    /// Leniency only costs an answer when it took part in one. A selector is matched from the
    /// subject leftwards, so an element that fails the subject compound never reaches the
    /// unmodelled pseudo-class further left, and "no" is then a fact.
    /// </summary>
    [Fact]
    public void Leniency_That_Never_Ran_Does_Not_Cost_The_Answer()
    {
        var (matcher, _, span) = CreateTree();

        Assert.True(matcher.TryMatch(span, "div:read-only > p", out var matches));
        Assert.False(matches);
    }

    /// <summary>
    /// Nor does leniency a logical combinator short-circuited past: <c>:is()</c> stops at its
    /// first hit, so the unmodelled branch behind it never decides anything.
    /// </summary>
    [Fact]
    public void Leniency_Behind_A_Satisfied_Branch_Does_Not_Cost_The_Answer()
    {
        var (matcher, div, _) = CreateTree();

        Assert.True(matcher.TryMatch(div, ":is(div, :read-only)", out var matches));
        Assert.True(matches);
    }

    /// <summary>The verdict is per call: one unanswerable selector does not poison the next.</summary>
    [Fact]
    public void Each_Call_Starts_Fresh()
    {
        var (matcher, div, _) = CreateTree();

        Assert.False(matcher.TryMatch(div, ":read-only", out _));
        Assert.True(matcher.TryMatch(div, "div.item", out var matches));
        Assert.True(matches);
        Assert.False(matcher.TryMatch(div, ":read-only", out _));
    }

    /// <summary>
    /// The scope argument is carried through unchanged, so <c>:scope</c> answers strictly too.
    /// </summary>
    [Fact]
    public void The_Scope_Element_Reaches_The_Strict_Path()
    {
        var (matcher, div, span) = CreateTree();

        Assert.True(matcher.TryMatch(div, ":scope", out var matches, div));
        Assert.True(matches);
        Assert.True(matcher.TryMatch(span, ":scope", out matches, div));
        Assert.False(matches);
    }
}
