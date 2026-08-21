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

    [Fact(Timeout = 600000)]
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

    [Fact(Timeout = 600000)]
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

    [Fact(Timeout = 600000)]
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
