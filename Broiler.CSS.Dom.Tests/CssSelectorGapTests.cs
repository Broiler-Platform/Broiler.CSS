using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

/// <summary>
/// <see cref="CssSelectorMatcher.DescribeGaps"/> — which parts of a style rule's selector the cascade
/// does not model as written, answered from the selector's text alone.
/// <para>
/// A page analysis needs this per rule, not per element: <c>button:-moz-focusring { outline: 1px
/// dotted }</c> (normalize.css) outlines every button here, because a vendor-prefixed pseudo-class is
/// a guess that matches everything, and the question "which rules of this sheet are guesses" has to
/// be answerable without a document to match against.
/// </para>
/// </summary>
public sealed class CssSelectorGapTests
{
    private static string[] Gaps(string selector) =>
        CssSelectorMatcher.DescribeGaps(selector).Select(gap => $"{gap.Kind} {gap.Text}").ToArray();

    [Theory]
    [InlineData("div")]
    [InlineData("#main > .item:first-child a[href^='http']")]
    [InlineData("li:nth-child(2n+1 of .item):not(.hidden), p:is(.a, .b) ~ span")]
    [InlineData("details[open] > summary, input:checked + label, :root")]
    [InlineData("p::before, p:after, li::marker, ::selection, dialog::backdrop")]
    [InlineData("a:any-link:has(> img)")]
    public void A_Modeled_Selector_Has_No_Gaps(string selector) => Assert.Empty(Gaps(selector));

    [Theory]
    [InlineData("button:-moz-focusring", "Guessed :-moz-focusring")]
    [InlineData("input:-webkit-autofill", "Guessed :-webkit-autofill")]
    [InlineData("input:read-only", "Guessed :read-only")]
    [InlineData(":host(.dark) p", "Guessed :host(.dark)")]
    [InlineData("p:bogus", "Invalid :bogus")]
    [InlineData(":matches(h1, h2)", "Invalid :matches(h1, h2)")]
    [InlineData("a:hover", "Interactive :hover")]
    [InlineData("a:visited", "Interactive :visited")]
    [InlineData("input:placeholder-shown + label", "NotModeled :placeholder-shown")]
    [InlineData("section:target", "NotModeled :target")]
    [InlineData("input::placeholder", "UnstyledPseudoElement ::placeholder")]
    [InlineData("::-webkit-scrollbar-thumb", "UnstyledPseudoElement ::-webkit-scrollbar-thumb")]
    [InlineData("p::before:hover", "UnstyledPseudoElement ::before:hover")]
    public void A_Gap_Is_Named_With_How_The_Cascade_Answers(string selector, string gap) =>
        Assert.Equal([gap], Gaps(selector));

    [Fact]
    public void Selector_Arguments_Are_Looked_Into_In_Source_Order()
    {
        Assert.Equal(
            ["Guessed :read-only", "Interactive :focus", "Guessed :indeterminate", "Invalid :bogus"],
            Gaps("input:not(:read-only):is(:focus, .x), li:nth-child(odd of :indeterminate), p:has(> :bogus)"));
    }

    [Fact]
    public void Every_Selector_Of_A_List_Is_Reported()
    {
        Assert.Equal(
            ["Guessed :-moz-focusring", "Guessed :-moz-focusring"],
            Gaps("button:-moz-focusring,\n[type=\"button\"]:-moz-focusring"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Nothing_Has_No_Gaps(string? selector) => Assert.Empty(CssSelectorMatcher.DescribeGaps(selector));

    /// <summary>
    /// The classification mirrors the pseudo-class switch rather than driving it, so this pins the
    /// mirror to the matcher's own answers, for every name the matcher recognizes and the explicit
    /// arms outside that set: a guessed name is exactly one <see cref="CssSelectorMatcher.TryMatch"/>
    /// declines to answer for, and an invalid, interactive or not-modeled one is a definite "no".
    /// </summary>
    [Fact]
    public void The_Classification_Agrees_With_The_Matcher_For_Every_Recognized_Name()
    {
        var document = new DomDocument();
        var div = document.CreateElement("div");
        div.SetAttribute("lang", "en");
        document.AppendChild(div);
        div.AppendChild(document.CreateElement("span"));
        var matcher = new CssSelectorMatcher();

        var names = CssSelectorMatcher.RecognizedPseudoClassNames
            .Concat(["-webkit-any", "matches", "any", "-moz-any", "-moz-focusring", "-webkit-autofill", "bogus"])
            .Distinct()
            .ToArray();
        Assert.Contains("read-only", names);

        var disagreements = new List<string>();
        foreach (var name in names)
        {
            var kind = CssSelectorMatcher.ClassifyPseudoClass(name);
            // The legacy single-colon pseudo-elements are the one known difference: the matcher
            // answers them leniently as pseudo-classes, and the cascade never asks it to, because it
            // styles them as pseudo-elements.
            if (name is "before" or "after" or "first-line" or "first-letter")
            {
                Assert.Null(kind);
                continue;
            }

            var selector = "div:" + name + Argument(name);
            var answered = matcher.TryMatch(div, selector, out var matches);
            var expected = kind switch
            {
                CssSelectorGapKind.Guessed => !answered,
                CssSelectorGapKind.Invalid or CssSelectorGapKind.Interactive or CssSelectorGapKind.NotModeled => answered && !matches,
                _ => answered,
            };
            if (!expected)
                disagreements.Add($"{selector}: classified {kind?.ToString() ?? "modeled"}, TryMatch answered={answered} matches={matches}");
        }

        Assert.Empty(disagreements);
    }

    private static string Argument(string name) => name switch
    {
        "nth-child" or "nth-last-child" or "nth-of-type" or "nth-last-of-type" or "nth-col" or "nth-last-col" => "(1)",
        "not" or "is" or "where" or "has" or "-webkit-any" or "matches" or "any" or "-moz-any" or "host" or "host-context" => "(span)",
        "lang" => "(en)",
        "dir" => "(ltr)",
        "state" => "(x)",
        _ => string.Empty,
    };
}
