namespace Broiler.CSS.Dom.Tests;

public sealed class CssComputedDefaultsTests
{
    [Theory]
    [InlineData("display", "inline")]
    [InlineData("position", "static")]
    [InlineData("width", "auto")]
    [InlineData("margin-top", "0px")]
    [InlineData("color", "rgb(0, 0, 0)")]
    [InlineData("text-align-last", "auto")]
    public void InitialValues_Exposes_Canonical_Defaults(string property, string expected)
    {
        Assert.True(CssComputedDefaults.InitialValues.TryGetValue(property, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void InitialValues_Lookup_Is_Case_Insensitive()
    {
        Assert.True(CssComputedDefaults.InitialValues.TryGetValue("DISPLAY", out var value));
        Assert.Equal("inline", value);
    }

    [Theory]
    [InlineData("color", true)]
    [InlineData("font-size", true)]
    [InlineData("text-align-last", true)]
    [InlineData("writing-mode", true)]
    [InlineData("width", false)]
    [InlineData("margin-top", false)]
    [InlineData("display", false)]
    public void InheritedProperties_Marks_Only_The_Inheriting_Properties(string property, bool inherited)
    {
        Assert.Equal(inherited, CssComputedDefaults.InheritedProperties.Contains(property));
    }

    [Theory]
    [InlineData("div", "block")]
    [InlineData("span", null)]          // no UA default — computes to the CSS initial (inline)
    [InlineData("li", "list-item")]
    [InlineData("td", "table-cell")]
    [InlineData("BUTTON", "inline-block")]   // case-insensitive
    [InlineData("script", "none")]
    // HTML Rendering, Flow content: listing, plaintext, pre, xmp { display: block }. The HTML
    // Standard tokenizes xmp as RAWTEXT and plaintext as PLAINTEXT, so their content is one text node.
    [InlineData("pre", "block")]
    [InlineData("xmp", "block")]
    [InlineData("listing", "block")]
    [InlineData("PLAINTEXT", "block")]
    [InlineData("textarea", "inline-block")] // HTML Rendering: the textarea widget is an inline-block box
    [InlineData("noscript", null)]           // display: none only under @media (scripting); the scripting host applies it
    public void UserAgentDisplayDefaults_Match_The_Html_Ua_Stylesheet(string tag, string? expected)
    {
        var present = CssUserAgentDefaults.DisplayValues.TryGetValue(tag, out var value);
        if (expected is null)
            Assert.False(present);
        else
        {
            Assert.True(present);
            Assert.Equal(expected, value);
        }
    }

    // HTML Rendering, Hidden elements: every tag in the rule's selector list except basefont and rp
    // (see the next test) is display: none, with no condition. noframes is hidden outside a frameset
    // too, and noembed is always hidden, so their RAWTEXT fallback markup never paints as literal text.
    [Theory]
    [InlineData("area")]
    [InlineData("base")]
    [InlineData("datalist")]
    [InlineData("head")]
    [InlineData("link")]
    [InlineData("meta")]
    [InlineData("noembed")]
    [InlineData("noframes")]
    [InlineData("param")]
    [InlineData("script")]
    [InlineData("style")]
    [InlineData("template")]
    [InlineData("title")]
    public void UserAgentDisplayDefaults_Hide_The_Html_Hidden_Elements(string tag)
    {
        Assert.True(CssUserAgentDefaults.DisplayValues.TryGetValue(tag, out var value));
        Assert.Equal("none", value);
    }

    // Known departures from the Hidden elements rule, kept in step with Broiler.HTML's default
    // stylesheet so a bridge's computed display agrees with what that renderer paints. Tracked in
    // docs/roadmap.md; hide both in both sheets once the blockers are gone.
    [Theory]
    // Broiler.Dom.Html's HtmlElementNames.VoidElements lacks basefont (and bgsound and keygen), so a
    // <basefont> contains its following siblings and hiding it would hide them too.
    [InlineData("basefont")]
    // Broiler.HTML has no ruby layout, so it paints the rp parentheses as the only separator
    // between an rt and its base text.
    [InlineData("rp")]
    public void UserAgentDisplayDefaults_Leave_Out_Hidden_Elements_The_Renderer_Still_Paints(string tag)
    {
        Assert.False(CssUserAgentDefaults.DisplayValues.ContainsKey(tag));
    }

    [Theory]
    // HTML Rendering, Flow content: listing, plaintext, pre, xmp { font-family: monospace; white-space: pre }
    [InlineData("listing", "font-family", "monospace")]
    [InlineData("listing", "white-space", "pre")]
    [InlineData("plaintext", "font-family", "monospace")]
    [InlineData("plaintext", "white-space", "pre")]
    [InlineData("pre", "font-family", "monospace")]
    [InlineData("pre", "white-space", "pre")]
    [InlineData("XMP", "Font-Family", "monospace")]  // tag and property are case-insensitive
    [InlineData("xmp", "white-space", "pre")]
    // HTML Rendering, Form controls: textarea { white-space: pre-wrap }
    [InlineData("textarea", "white-space", "pre-wrap")]
    [InlineData("textarea", "font-family", null)]    // the HTML UA stylesheet gives the control no font-family
    [InlineData("div", "white-space", null)]         // no UA declarations besides display
    [InlineData("pre", "display", null)]             // display lives in DisplayValues only
    public void UserAgentPropertyDefaults_Match_The_Html_Ua_Stylesheet(string tag, string property, string? expected)
    {
        string? value = null;
        var present = CssUserAgentDefaults.PropertyValues.TryGetValue(tag, out var declarations)
            && declarations.TryGetValue(property, out value);
        if (expected is null)
            Assert.False(present);
        else
        {
            Assert.True(present);
            Assert.Equal(expected, value);
        }
    }

    [Fact]
    public void UserAgentPropertyDefaults_Are_Read_Only()
    {
        // CssDomArchitectureTests only sees the declared member type; this guards the instances,
        // including the nested per-tag maps, against a mutable dictionary slipping in.
        Assert.True(((ICollection<KeyValuePair<string, IReadOnlyDictionary<string, string>>>)CssUserAgentDefaults.PropertyValues).IsReadOnly);
        Assert.All(
            CssUserAgentDefaults.PropertyValues.Values,
            static declarations => Assert.True(((ICollection<KeyValuePair<string, string>>)declarations).IsReadOnly));
    }

    [Fact]
    public void UserAgentPropertyDefaults_Reach_Computed_Style_Through_The_Cascade()
    {
        // font-family and white-space inherit, so the table is applied as user-agent rules through
        // the cascade: descendants inherit the values and author rules still override them.
        var css = string.Join(
            "\n",
            CssUserAgentDefaults.PropertyValues.Select(static rule =>
                $"{rule.Key} {{ {string.Join(" ", rule.Value.Select(static d => $"{d.Key}: {d.Value};"))} }}"));

        var doc = new Broiler.Dom.DomDocument();
        var html = doc.CreateElement("html");
        doc.AppendChild(html);
        var body = doc.CreateElement("body");
        html.AppendChild(body);
        var xmp = doc.CreateElement("xmp");
        body.AppendChild(xmp);
        var pre = doc.CreateElement("pre");
        body.AppendChild(pre);
        var span = doc.CreateElement("span"); // HTML parsing gives an xmp only text, so inherit through pre
        pre.AppendChild(span);
        var textarea = doc.CreateElement("textarea");
        body.AppendChild(textarea);
        var authored = doc.CreateElement("pre");
        authored.Id = "author";
        body.AppendChild(authored);
        var div = doc.CreateElement("div");
        body.AppendChild(div);

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(new CssParser().ParseStyleSheet(css), CssOrigin.UserAgent);
        engine.AddStyleSheet(new CssParser().ParseStyleSheet("#author { white-space: normal }"), CssOrigin.Author);

        Assert.Equal("monospace", engine.GetComputedStyle(xmp).GetPropertyValue("font-family"));
        Assert.Equal("pre", engine.GetComputedStyle(xmp).GetPropertyValue("white-space"));
        Assert.Equal("monospace", engine.GetComputedStyle(span).GetPropertyValue("font-family"));
        Assert.Equal("pre", engine.GetComputedStyle(span).GetPropertyValue("white-space"));
        Assert.Equal("pre-wrap", engine.GetComputedStyle(textarea).GetPropertyValue("white-space"));
        Assert.Equal("normal", engine.GetComputedStyle(authored).GetPropertyValue("white-space"));
        Assert.Equal("monospace", engine.GetComputedStyle(authored).GetPropertyValue("font-family"));
        Assert.Equal("serif", engine.GetComputedStyle(div).GetPropertyValue("font-family"));
        Assert.Equal("normal", engine.GetComputedStyle(div).GetPropertyValue("white-space"));
    }

    [Fact]
    public void ResolveLengthAttrFunctions_Substitutes_From_Element_Attributes()
    {
        var doc = new Broiler.Dom.DomDocument();
        var el = doc.CreateElement("div");
        el.SetAttribute("data-w", "120px");
        doc.AppendChild(el);

        var computed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["width"] = "attr(data-w type(<length>))",
            ["height"] = "attr(missing type(<length>), 40px)",
        };

        CssStyleEngine.ResolveLengthAttrFunctions(computed, el);

        Assert.Equal("120px", computed["width"]);
        Assert.Equal("40px", computed["height"]); // falls back when the attribute is absent
    }

    [Fact]
    public void Shared_Tables_Back_The_Engine_Computed_Style()
    {
        // The engine's getComputedStyle backfills unset properties from the same
        // shared initial-value table, so an undeclared element reports the canonical
        // default (regression guard against the bridge/engine tables drifting apart).
        var doc = new Broiler.Dom.DomDocument();
        var html = doc.CreateElement("html");
        doc.AppendChild(html);
        var div = doc.CreateElement("div");
        html.AppendChild(div);

        var engine = new CssStyleEngine();
        var computed = engine.GetComputedStyle(div);

        Assert.Equal(CssComputedDefaults.InitialValues["text-align-last"], computed.GetPropertyValue("text-align-last"));
        Assert.Equal(CssComputedDefaults.InitialValues["position"], computed.GetPropertyValue("position"));
    }
}
