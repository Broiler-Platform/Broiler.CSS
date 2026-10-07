using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

public sealed class CssSelectorMatcherEscapeTests
{
    /// <summary>
    /// CSS Syntax 3 §4.3.7 in selectors: zero, a surrogate, or a value above U+10FFFF decodes to
    /// U+FFFD. The matcher used to pass every escape to <see cref="char.ConvertFromUtf32"/>, so a
    /// surrogate or out-of-range escape in a stylesheet threw during matching.
    /// </summary>
    [Theory]
    [InlineData(@".\D800", "�")]
    [InlineData(@".\110000", "�")]
    [InlineData(@".\0", "�")]
    [InlineData(@".\000041b", "Ab")]
    public void Class_Selector_Escapes_Decode_And_Replace_Invalid_Code_Points(string selector, string className)
    {
        var document = new DomDocument();
        var element = document.CreateElement("div");
        element.ClassName = className;
        document.AppendChild(element);

        Assert.True(new CssSelectorMatcher().Matches(element, selector));
    }

    /// <summary>
    /// An escape keeps the whitespace it escapes: <c>#\ </c> is the id <c>" "</c> (Acid3 test 28),
    /// and the space of <c>#a\ b</c> is part of the id, not a descendant combinator. The first used to
    /// be lost to a plain <c>Trim()</c>, the second to a splitter that cut the selector at the space.
    /// </summary>
    [Theory]
    [InlineData(@"#\ ", " ")]
    [InlineData(@"#\  ", " ")]
    [InlineData(@"#a\ b", "a b")]
    [InlineData(@"#\20 x", " x")]
    [InlineData(@"#\20 ", " ")]
    [InlineData(@"div#\ ", " ")]
    [InlineData(@"body > #\ ", " ")]
    [InlineData(@":is(#\ )", " ")]
    [InlineData(@":not(#\ )", "x")]
    public void Escaped_Whitespace_Belongs_To_The_Id(string selector, string id)
    {
        var document = new DomDocument();
        var body = document.CreateElement("body");
        var element = document.CreateElement("div");
        element.Id = id;
        document.AppendChild(body);
        body.AppendChild(element);

        Assert.True(new CssSelectorMatcher().Matches(element, selector));
    }

    [Fact]
    public void An_Escaped_Space_Is_Not_A_Descendant_Combinator()
    {
        // <div id="a"><b></b></div>: `#a\ b` is the id "a b", so it matches neither element.
        var document = new DomDocument();
        var parent = document.CreateElement("div");
        parent.Id = "a";
        var child = document.CreateElement("b");
        document.AppendChild(parent);
        parent.AppendChild(child);
        var matcher = new CssSelectorMatcher();

        Assert.False(matcher.Matches(child, @"#a\ b"));
        Assert.False(matcher.Matches(parent, @"#a\ b"));
        Assert.True(matcher.Matches(child, @"#a b"));
    }
}
