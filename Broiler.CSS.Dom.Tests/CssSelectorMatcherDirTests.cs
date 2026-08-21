using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

/// <summary>
/// <c>:dir()</c> (Selectors 4 §11.2, resolved through HTML's "directionality" concept).
/// <para>
/// It used to be listed as recognised-but-unmodelled, so it fell through to the lenient default
/// and matched <em>every</em> element — <c>:dir(ltr)</c> and <c>:dir(rtl)</c> at once. That is
/// what let one rule in a shadow tree paint a whole canvas in the WPT
/// <c>css/css-shadow/shadow-directionality-001/002</c> pair.
/// </para>
/// </summary>
public sealed class CssSelectorMatcherDirTests
{
    private static DomElement Element(DomDocument document, DomElement parent, string tag, string? dir = null)
    {
        var element = document.CreateElement(tag);
        if (dir is not null)
            element.SetAttribute("dir", dir);
        parent.AppendChild(element);
        return element;
    }

    [Fact(Timeout = 600000)]
    public void Dir_DefaultsToLtr_AndIsNotMatchedByRtl()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        document.AppendChild(html);
        var div = Element(document, html, "div");

        var matcher = new CssSelectorMatcher();
        Assert.True(matcher.Matches(div, "div:dir(ltr)"));
        Assert.False(matcher.Matches(div, "div:dir(rtl)"));
    }

    [Fact(Timeout = 600000)]
    public void Dir_ReadsTheNearestAncestorsDirAttribute()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        document.AppendChild(html);
        var rtl = Element(document, html, "div", "rtl");
        var inherited = Element(document, rtl, "span");
        var overridden = Element(document, rtl, "span", "ltr");
        var deeper = Element(document, overridden, "b");

        var matcher = new CssSelectorMatcher();
        Assert.True(matcher.Matches(inherited, ":dir(rtl)"));
        Assert.False(matcher.Matches(inherited, ":dir(ltr)"));
        Assert.True(matcher.Matches(overridden, ":dir(ltr)"));
        Assert.True(matcher.Matches(deeper, ":dir(ltr)"));
    }

    [Fact(Timeout = 600000)]
    public void Dir_IgnoresAnInvalidAttributeValue()
    {
        // `dir="sideways"` is not a valid value, so the element inherits rather than adopting it.
        var document = new DomDocument();
        var html = document.CreateElement("html");
        document.AppendChild(html);
        var rtl = Element(document, html, "div", "rtl");
        var bogus = Element(document, rtl, "span", "sideways");

        var matcher = new CssSelectorMatcher();
        Assert.True(matcher.Matches(bogus, ":dir(rtl)"));
    }

    [Fact(Timeout = 600000)]
    public void Dir_Auto_ResolvesFromTheFirstStrongCharacter()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        document.AppendChild(html);

        var arabic = Element(document, html, "p", "auto");
        arabic.AppendChild(document.CreateTextNode("123 مرحبا"));

        var latin = Element(document, html, "p", "auto");
        latin.AppendChild(document.CreateTextNode("123 hello"));

        var neutral = Element(document, html, "p", "auto");
        neutral.AppendChild(document.CreateTextNode("123 456"));

        var matcher = new CssSelectorMatcher();
        Assert.True(matcher.Matches(arabic, ":dir(rtl)"));
        Assert.True(matcher.Matches(latin, ":dir(ltr)"));
        // No strong character at all falls back to ltr rather than to "neither".
        Assert.True(matcher.Matches(neutral, ":dir(ltr)"));
    }

    [Fact(Timeout = 600000)]
    public void Dir_Auto_SkipsADescendantWithItsOwnDir()
    {
        // HTML's auto-directionality ignores text inside a descendant that declares its own
        // direction, so the Hebrew inside the ltr <span> must not flip the paragraph.
        var document = new DomDocument();
        var html = document.CreateElement("html");
        document.AppendChild(html);

        var paragraph = Element(document, html, "p", "auto");
        var inner = Element(document, paragraph, "span", "ltr");
        inner.AppendChild(document.CreateTextNode("שלום"));
        paragraph.AppendChild(document.CreateTextNode("hello"));

        var matcher = new CssSelectorMatcher();
        Assert.True(matcher.Matches(paragraph, ":dir(ltr)"));
    }

    [Fact(Timeout = 600000)]
    public void Dir_RejectsAnArgumentThatIsNeitherLtrNorRtl()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        document.AppendChild(html);
        var div = Element(document, html, "div");

        var matcher = new CssSelectorMatcher();
        Assert.False(matcher.Matches(div, ":dir(auto)"));
        Assert.False(matcher.Matches(div, ":dir(sideways)"));
        Assert.False(matcher.Matches(div, ":dir()"));
    }

    [Fact(Timeout = 600000)]
    public void Bdi_DefaultsToAutoDirectionality()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        document.AppendChild(html);
        var ltrParent = Element(document, html, "div", "ltr");
        var bdi = Element(document, ltrParent, "bdi");
        bdi.AppendChild(document.CreateTextNode("שלום"));

        var matcher = new CssSelectorMatcher();
        // <bdi> isolates: it resolves from its own contents, not from the ltr parent.
        Assert.True(matcher.Matches(bdi, ":dir(rtl)"));
    }
}
