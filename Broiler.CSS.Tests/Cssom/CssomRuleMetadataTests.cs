using Broiler.CSS.Cssom;

namespace Broiler.CSS.Tests.Cssom;

public sealed class CssomRuleMetadataTests
{
    private static CssRule ParseSingleRule(string css) =>
        new CssParser().ParseStyleSheet(css).Rules[0];

    [Theory]
    [InlineData(".a { color: red }", CssomRuleType.Style)]
    [InlineData("@charset \"utf-8\";", CssomRuleType.Charset)]
    [InlineData("@import url(a.css);", CssomRuleType.Import)]
    [InlineData("@media screen { .a { color: red } }", CssomRuleType.Media)]
    [InlineData("@font-face { font-family: A }", CssomRuleType.FontFace)]
    [InlineData("@page :first { margin: 0 }", CssomRuleType.Page)]
    [InlineData("@keyframes spin { from { top: 0 } }", CssomRuleType.Keyframes)]
    [InlineData("@namespace svg url(http://www.w3.org/2000/svg);", CssomRuleType.Namespace)]
    [InlineData("@counter-style c { system: cyclic }", CssomRuleType.CounterStyle)]
    [InlineData("@supports (display:grid) { .a { color: red } }", CssomRuleType.Supports)]
    [InlineData("@layer base { .a { color: red } }", CssomRuleType.Layer)]
    [InlineData("@property --x { syntax: \"*\"; inherits: false }", CssomRuleType.Property)]
    [InlineData("@container (min-width: 0) { .a { color: red } }", CssomRuleType.Unknown)]
    public void GetRuleType_Maps_The_Model_Kind(string css, CssomRuleType expected)
    {
        Assert.Equal(expected, CssomRuleMetadata.GetRuleType(ParseSingleRule(css)));
        Assert.Equal((int)expected, CssomRuleMetadata.GetCssomTypeNumber(ParseSingleRule(css)));
    }

    [Fact]
    public void GetSelectorText_Joins_Selectors_Like_The_Serializer()
    {
        var rule = Assert.IsType<CssStyleRule>(ParseSingleRule(".card, #hero:hover { color: red }"));
        Assert.Equal(".card, #hero:hover", CssomRuleMetadata.GetSelectorText(rule));
    }

    [Theory]
    [InlineData("@keyframes spin { from { top: 0 } }", "spin")]
    [InlineData("@keyframes \"quoted\" { from { top: 0 } }", "quoted")]
    public void GetKeyframesName_Unquotes(string css, string expected)
    {
        Assert.Equal(expected, CssomRuleMetadata.GetKeyframesName((CssAtRule)ParseSingleRule(css)));
    }

    [Fact]
    public void GetCharsetEncoding_Unquotes()
    {
        Assert.Equal("utf-8", CssomRuleMetadata.GetCharsetEncoding((CssAtRule)ParseSingleRule("@charset \"utf-8\";")));
    }

    [Theory]
    [InlineData("@import url(\"a.css\") screen;", "a.css", "screen")]
    [InlineData("@import 'b.css';", "b.css", "")]
    [InlineData("@import url(c.css) print, tv;", "c.css", "print, tv")]
    public void GetImport_Decomposes_Href_And_Media(string css, string href, string media)
    {
        var import = CssomRuleMetadata.GetImport((CssAtRule)ParseSingleRule(css));
        Assert.Equal(href, import.Href);
        Assert.Equal(media, import.Media);
    }

    [Theory]
    [InlineData("@namespace svg url(http://www.w3.org/2000/svg);", "svg", "http://www.w3.org/2000/svg")]
    [InlineData("@namespace \"http://example.test/ns\";", null, "http://example.test/ns")]
    public void GetNamespace_Decomposes_Prefix_And_Uri(string css, string? prefix, string uri)
    {
        var ns = CssomRuleMetadata.GetNamespace((CssAtRule)ParseSingleRule(css));
        Assert.Equal(prefix, ns.Prefix);
        Assert.Equal(uri, ns.Uri);
    }

    [Fact]
    public void At_Rule_Prelude_And_Declarations_Are_Model_Metadata()
    {
        // media/supports/layer/page/property/counter-style names are the trimmed
        // prelude directly; declaration-bodied at-rules expose their descriptors
        // via the declaration block — no serialization round-trip needed.
        var media = (CssAtRule)ParseSingleRule("@media screen and (min-width: 600px) { .a { color: red } }");
        Assert.Equal("screen and (min-width: 600px)", media.Prelude);

        var property = (CssAtRule)ParseSingleRule("@property --x { syntax: \"<color>\"; inherits: false; initial-value: red }");
        Assert.Equal("--x", property.Prelude);
        Assert.Equal("\"<color>\"", property.Declarations!.GetPropertyValue("syntax"));
        Assert.Equal("false", property.Declarations!.GetPropertyValue("inherits"));
    }
}
