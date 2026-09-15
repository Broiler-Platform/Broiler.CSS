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
}
