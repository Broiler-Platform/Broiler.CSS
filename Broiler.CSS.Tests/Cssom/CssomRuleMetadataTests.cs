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
    [InlineData("@import url(\"a.css\") screen;", "a.css", CssImportLayer.None, null, null, "screen")]
    [InlineData("@import 'b.css';", "b.css", CssImportLayer.None, null, null, "")]
    [InlineData("@import url(c.css) print, tv;", "c.css", CssImportLayer.None, null, null, "print, tv")]
    [InlineData("@import url(\"a.css\") layer;", "a.css", CssImportLayer.Anonymous, null, null, "")]
    [InlineData("@import url(\"a.css\") layer(base);", "a.css", CssImportLayer.Named, "base", null, "")]
    [InlineData("@import url('a.css') layer(base.reset);", "a.css", CssImportLayer.Named, "base.reset", null, "")]
    [InlineData("@import url(a.css) supports(display: grid);", "a.css", CssImportLayer.None, null, "display: grid", "")]
    [InlineData("@import url(\"a.css\") layer(framework.grid) supports(display: grid) screen and (min-width: 600px);", "a.css", CssImportLayer.Named, "framework.grid", "display: grid", "screen and (min-width: 600px)")]
    [InlineData("@import \"a.css\" layer supports((display: flex) and (display: grid));", "a.css", CssImportLayer.Anonymous, null, "(display: flex) and (display: grid)", "")]
    [InlineData("@import url(\"nested(1).css\");", "nested(1).css", CssImportLayer.None, null, null, "")]
    [InlineData("@import url(\"a\\\"b.css\");", "a\"b.css", CssImportLayer.None, null, null, "")]
    [InlineData("@import url(\\31 .css);", "1.css", CssImportLayer.None, null, null, "")]
    public void GetImport_Decomposes_Cascade5_Prelude(
        string css,
        string href,
        CssImportLayer layer,
        string? layerName,
        string? supports,
        string media)
    {
        var import = CssomRuleMetadata.GetImport((CssAtRule)ParseSingleRule(css));
        Assert.Equal(href, import.Href);
        Assert.Equal(layer, import.Layer);
        Assert.Equal(layerName, import.LayerName);
        Assert.Equal(supports, import.Supports);
        Assert.Equal(media, import.Media);

        // Verify two-tuple deconstruction compatibility
        var (h, m) = import;
        Assert.Equal(href, h);
        Assert.Equal(media, m);
    }

    [Theory]
    [InlineData("@import url(a.css) layer(1bad) screen;", "a.css", CssImportLayer.None, null, null, "layer(1bad) screen")]
    [InlineData("@import url(a.css) supports(display: grid) layer(base);", "a.css", CssImportLayer.None, null, "display: grid", "layer(base)")]
    public void GetImport_Leaves_Malformed_Or_OutOfOrder_Parts_In_Media(
        string css,
        string href,
        CssImportLayer layer,
        string? layerName,
        string? supports,
        string media)
    {
        var import = CssomRuleMetadata.GetImport((CssAtRule)ParseSingleRule(css));
        Assert.Equal(href, import.Href);
        Assert.Equal(layer, import.Layer);
        Assert.Equal(layerName, import.LayerName);
        Assert.Equal(supports, import.Supports);
        Assert.Equal(media, import.Media);
    }

    /// <summary>
    /// The prelude's parenthesis scan is <see cref="CssSyntax.FindMatching"/> now that the two
    /// agree on <c>-1</c> for no match. A <c>layer(</c> or <c>supports(</c> that never closes is
    /// still not a layer or a feature query, and what follows stays in the media list.
    /// </summary>
    [Theory]
    [InlineData("@import url(a.css) layer(base", CssImportLayer.None, null, null, "layer(base")]
    [InlineData("@import url(a.css) layer(base.reset screen", CssImportLayer.None, null, null, "layer(base.reset screen")]
    [InlineData("@import url(a.css) supports(display: grid", CssImportLayer.None, null, null, "supports(display: grid")]
    public void GetImport_Ignores_An_Unterminated_Layer_Or_Supports(
        string css,
        CssImportLayer layer,
        string? layerName,
        string? supports,
        string media)
    {
        var import = CssomRuleMetadata.GetImport((CssAtRule)ParseSingleRule(css + ";"));
        Assert.Equal("a.css", import.Href);
        Assert.Equal(layer, import.Layer);
        Assert.Equal(layerName, import.LayerName);
        Assert.Equal(supports, import.Supports);
        Assert.Equal(media, import.Media);
    }

    [Theory]
    [InlineData("base", true)]
    [InlineData("reset", true)]
    [InlineData("base.reset", true)]
    [InlineData("a.b.c", true)]
    [InlineData("-custom", true)]
    [InlineData("--theme", true)]
    [InlineData("_private", true)]
    [InlineData("\\31 st", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData(".base", false)]
    [InlineData("base.", false)]
    [InlineData("base..reset", false)]
    [InlineData("base . reset", false)]
    [InlineData("base reset", false)]
    [InlineData("1bad", false)]
    [InlineData("-1bad", false)]
    [InlineData("initial", false)]
    [InlineData("inherit", false)]
    [InlineData("unset", false)]
    [InlineData("revert", false)]
    [InlineData("revert-layer", false)]
    [InlineData("default", false)]
    [InlineData("base.initial", false)]
    [InlineData("default.reset", false)]
    public void CssLayerNameMetadata_Validates_Layer_Names(string? name, bool expectedValid)
    {
        Assert.Equal(expectedValid, CssLayerNameMetadata.IsValidLayerName(name));
    }

    [Theory]
    [InlineData("base", true)]
    [InlineData("-custom", true)]
    [InlineData("--theme", true)]
    [InlineData("_foo", true)]
    [InlineData("\\31 st", true)]
    [InlineData("base.reset", false)]
    [InlineData("initial", false)]
    [InlineData("default", false)]
    [InlineData("1bad", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CssLayerNameMetadata_Validates_Segments(string? segment, bool expectedValid)
    {
        Assert.Equal(expectedValid, CssLayerNameMetadata.IsValidSegment(segment));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("base", false)]
    public void CssLayerNameMetadata_Identifies_Anonymous_Layers(string? name, bool expectedAnonymous)
    {
        Assert.Equal(expectedAnonymous, CssLayerNameMetadata.IsAnonymous(name));
    }

    [Fact]
    public void CssLayerNameMetadata_Splits_And_Normalizes_Segments()
    {
        Assert.Equal(["base", "reset"], CssLayerNameMetadata.GetSegments("base.reset"));
        Assert.Equal(["a", "b", "c"], CssLayerNameMetadata.GetSegments("a.b.c"));
        Assert.Empty(CssLayerNameMetadata.GetSegments("invalid..name"));
        Assert.Empty(CssLayerNameMetadata.GetSegments(null));

        Assert.Equal("base.reset", CssLayerNameMetadata.NormalizeLayerName("base.reset"));
        Assert.Equal("base.reset", CssLayerNameMetadata.NormalizeLayerName("\\62 ase.\\72 eset"));
        Assert.Equal(string.Empty, CssLayerNameMetadata.NormalizeLayerName(""));
    }

    [Fact]
    public void CssLayerNameMetadata_Parses_Statement_Layer_Names()
    {
        var names = CssLayerNameMetadata.ParseStatementLayerNames("reset, base, framework.grid;");
        Assert.Equal(["reset", "base", "framework.grid"], names);

        var withCommentsAndInvalid = CssLayerNameMetadata.ParseStatementLayerNames("reset, /* comment */ base, 1bad, theme;");
        Assert.Equal(["reset", "base", "theme"], withCommentsAndInvalid);

        Assert.Empty(CssLayerNameMetadata.ParseStatementLayerNames(null));
        Assert.Empty(CssLayerNameMetadata.ParseStatementLayerNames("  ; "));
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
