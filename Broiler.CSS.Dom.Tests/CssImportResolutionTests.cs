using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.CSS.Cssom;
using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

public sealed class CssImportResolutionTests
{
    private sealed class MockLoader : ICssStyleSheetLoader
    {
        private readonly Dictionary<string, string?> _sheets = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Href, string? Referrer)> Requests { get; } = [];

        public MockLoader Add(string href, string? css)
        {
            _sheets[href] = css;
            return this;
        }

        public string? LoadStyleSheet(string href, string? referrerUrl = null)
        {
            Requests.Add((href, referrerUrl));
            if (_sheets.TryGetValue(href, out var css))
                return css;
            return null;
        }
    }

    private static (DomDocument Document, DomElement Div) NewDocWithDiv(string? className = null, string? id = null)
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        var body = document.CreateElement("body");
        var div = document.CreateElement("div");
        if (className is not null)
            div.SetAttribute("class", className);
        if (id is not null)
            div.SetAttribute("id", id);
        document.AppendChild(html);
        html.AppendChild(body);
        body.AppendChild(div);
        return (document, div);
    }

    [Fact]
    public void Basic_Import_Expands_Rules_In_Order()
    {
        var loader = new MockLoader()
            .Add("sub.css", "div { margin: 10px; color: red; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            @import "sub.css";
            div { color: blue; }
            """);

        var resolved = CssImportResolver.ResolveImports(sheet, loader);
        Assert.Equal(2, resolved.Rules.Count);

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(resolved);

        var (_, div) = NewDocWithDiv();
        var style = engine.GetComputedStyle(div);

        Assert.Equal("10px", style.GetPropertyValue("margin-top"));
        // Later parent rule overrides earlier imported rule at equal specificity
        Assert.Equal("blue", style.GetPropertyValue("color"));
    }

    [Fact]
    public void Import_With_Named_Layer_Subordinates_To_Unlayered_Rules()
    {
        var loader = new MockLoader()
            .Add("reset.css", "div#target { color: red; font-size: 20px; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            @import "reset.css" layer(reset);
            div { color: blue; }
            """);

        var resolved = CssImportResolver.ResolveImports(sheet, loader);
        var engine = new CssStyleEngine();
        engine.AddStyleSheet(resolved);

        var (_, div) = NewDocWithDiv(id: "target");
        var style = engine.GetComputedStyle(div);

        // Unlayered 'div { color: blue }' beats layered 'div#target { color: red }'
        // despite id specificity (CSS Cascade 5 §6.1).
        Assert.Equal("blue", style.GetPropertyValue("color"));
        Assert.Equal("20px", style.GetPropertyValue("font-size"));

        var layerOrder = engine.GetLayerOrder();
        Assert.True(layerOrder.GetLayerIndex("reset") >= 0);
    }

    [Fact]
    public void Import_With_Anonymous_Layer_Wraps_In_Anonymous_Block()
    {
        var loader = new MockLoader()
            .Add("base.css", "div.card { color: red; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            @import "base.css" layer;
            div { color: green; }
            """);

        var resolved = CssImportResolver.ResolveImports(sheet, loader);
        Assert.Single(resolved.Rules.OfType<CssAtRule>());
        var layerRule = resolved.Rules.OfType<CssAtRule>().First();
        Assert.True(layerRule.HasBlock);
        Assert.True(string.IsNullOrEmpty(layerRule.Prelude));

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(resolved);

        var (_, div) = NewDocWithDiv(className: "card");
        var style = engine.GetComputedStyle(div);

        // Unlayered beats anonymous layer
        Assert.Equal("green", style.GetPropertyValue("color"));
    }

    [Fact]
    public void Import_With_Supports_Condition_True_Fetches_And_Inlines()
    {
        var loader = new MockLoader()
            .Add("flex.css", "div { display: flex; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            @import "flex.css" supports(display: flex);
            """);

        var resolved = CssImportResolver.ResolveImports(sheet, loader);

        Assert.Single(loader.Requests);
        Assert.Equal("flex.css", loader.Requests[0].Href);
        Assert.Single(resolved.Rules);
    }

    [Fact]
    public void Import_With_Supports_Condition_False_Skips_Fetch()
    {
        var loader = new MockLoader()
            .Add("fake.css", "div { display: none; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            @import "fake.css" supports(unknown-property-xyz: 123);
            """);

        var resolved = CssImportResolver.ResolveImports(sheet, loader);

        Assert.Empty(loader.Requests);
        Assert.Empty(resolved.Rules);
    }

    [Fact]
    public void Import_With_Supports_Condition_False_Preserves_Named_Layer()
    {
        var loader = new MockLoader();
        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            @import "future.css" layer(future-feature) supports(hologram: enabled);
            """);

        var resolved = CssImportResolver.ResolveImports(sheet, loader);

        Assert.Empty(loader.Requests);
        // Cascade 5 §2: layer name is declared at import's position even if not fetched
        Assert.Single(resolved.Rules);
        var atRule = Assert.IsType<CssAtRule>(resolved.Rules[0]);
        Assert.Equal("layer", atRule.Name);
        Assert.Equal("future-feature", atRule.Prelude);
        Assert.False(atRule.HasBlock);

        var layerOrder = CssCascadeLayerOrder.Build(resolved.Rules);
        Assert.True(layerOrder.GetLayerIndex("future-feature") >= 0);
    }

    [Fact]
    public void Import_With_Media_Wraps_In_Media_Block()
    {
        var loader = new MockLoader()
            .Add("desktop.css", "div { color: purple; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            @import "desktop.css" (min-width: 1000px);
            """);

        var resolved = CssImportResolver.ResolveImports(sheet, loader);
        Assert.Single(resolved.Rules);
        var mediaRule = Assert.IsType<CssAtRule>(resolved.Rules[0]);
        Assert.Equal("media", mediaRule.Name);
        Assert.Equal("(min-width: 1000px)", mediaRule.Prelude);
        Assert.Single(mediaRule.Rules);

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(resolved);

        var (_, div) = NewDocWithDiv();

        // 800px viewport -> does not match
        engine.UpdateEnvironment(new CssEnvironment(800, 600));
        Assert.Equal("rgb(0, 0, 0)", engine.GetComputedStyle(div).GetPropertyValue("color"));

        // 1200px viewport -> matches
        engine.UpdateEnvironment(new CssEnvironment(1200, 600));
        Assert.Equal("purple", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact]
    public void Import_With_Combined_Layer_Supports_And_Media()
    {
        var loader = new MockLoader()
            .Add("grid.css", "div { display: grid; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            @import "grid.css" layer(layout) supports(display: grid) screen;
            """);

        var resolved = CssImportResolver.ResolveImports(sheet, loader);
        Assert.Single(resolved.Rules);

        // Conditions outside, layer inside: @media screen { @layer layout { ... } }
        var mediaRule = Assert.IsType<CssAtRule>(resolved.Rules[0]);
        Assert.Equal("media", mediaRule.Name);
        Assert.Equal("screen", mediaRule.Prelude);

        Assert.Single(mediaRule.Rules);
        var layerRule = Assert.IsType<CssAtRule>(mediaRule.Rules[0]);
        Assert.Equal("layer", layerRule.Name);
        Assert.Equal("layout", layerRule.Prelude);
        Assert.Single(layerRule.Rules);
    }

    [Fact]
    public void Failed_Import_Preserves_Named_Layer_Declaration()
    {
        var loader = new MockLoader(); // Returns null for all URLs
        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            @import "missing.css" layer(components);
            div { color: black; }
            """);

        var resolved = CssImportResolver.ResolveImports(sheet, loader);

        Assert.Equal(2, resolved.Rules.Count);
        var atRule = Assert.IsType<CssAtRule>(resolved.Rules[0]);
        Assert.Equal("layer", atRule.Name);
        Assert.Equal("components", atRule.Prelude);
        Assert.False(atRule.HasBlock);

        var layerOrder = CssCascadeLayerOrder.Build(resolved.Rules);
        Assert.True(layerOrder.GetLayerIndex("components") >= 0);
    }

    [Fact]
    public void Failed_Import_Without_Layer_Emits_Nothing()
    {
        var loader = new MockLoader();
        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            @import "missing.css";
            div { color: black; }
            """);

        var resolved = CssImportResolver.ResolveImports(sheet, loader);
        Assert.Single(resolved.Rules);
        Assert.IsType<CssStyleRule>(resolved.Rules[0]);
    }

    [Fact]
    public void Direct_Self_Cycle_Is_Prevented()
    {
        var loader = new MockLoader()
            .Add("self.css", "@import 'self.css'; div { color: red; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("@import 'self.css';");

        var resolved = CssImportResolver.ResolveImports(sheet, loader);

        // Loaded once, cycle broken on the nested @import
        Assert.Single(loader.Requests);
        Assert.Single(resolved.Rules);
        Assert.IsType<CssStyleRule>(resolved.Rules[0]);
    }

    [Fact]
    public void Mutual_Cycle_Is_Prevented_And_Terminates()
    {
        var loader = new MockLoader()
            .Add("a.css", "@import 'b.css'; div { color: red; }")
            .Add("b.css", "@import 'a.css'; div { margin: 5px; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("@import 'a.css';");

        var resolved = CssImportResolver.ResolveImports(sheet, loader);

        Assert.Equal(2, loader.Requests.Count);
        Assert.Equal("a.css", loader.Requests[0].Href);
        Assert.Equal("b.css", loader.Requests[1].Href);

        // Both rules present, cycle avoided
        var engine = new CssStyleEngine();
        engine.AddStyleSheet(resolved);
        var (_, div) = NewDocWithDiv();
        var style = engine.GetComputedStyle(div);
        Assert.Equal("5px", style.GetPropertyValue("margin-top"));
        Assert.Equal("red", style.GetPropertyValue("color"));
    }

    [Fact]
    public void Cycle_With_Named_Layer_Preserves_Layer_Statement()
    {
        var loader = new MockLoader()
            .Add("a.css", "@import 'b.css' layer(layerB);")
            .Add("b.css", "@import 'a.css' layer(layerA); div { color: green; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("@import 'a.css' layer(layerA);");

        var resolved = CssImportResolver.ResolveImports(sheet, loader);

        var layerOrder = CssCascadeLayerOrder.Build(resolved.Rules);
        Assert.True(layerOrder.GetLayerIndex("layerA") >= 0);
        // Since b.css was imported inside layerA, its layerB is nested as layerA.layerB
        Assert.True(layerOrder.GetLayerIndex("layerA.layerB") >= 0);
    }

    [Fact]
    public void Recursion_Depth_Limit_Halts_Deep_Imports()
    {
        var loader = new MockLoader()
            .Add("1.css", "@import '2.css';")
            .Add("2.css", "@import '3.css';")
            .Add("3.css", "@import '4.css';")
            .Add("4.css", "div { color: magenta; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("@import '1.css';");

        var resolved = CssImportResolver.ResolveImports(sheet, loader, maxDepth: 2);

        // 1.css (depth 0), 2.css (depth 1), halts before fetching 3.css (depth 2 == maxDepth)
        Assert.Equal(2, loader.Requests.Count);
        Assert.Empty(resolved.Rules);
    }

    [Fact]
    public void Misplaced_Import_After_Style_Rule_Is_Ignored()
    {
        var loader = new MockLoader()
            .Add("late.css", "div { color: blue; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            div { color: red; }
            @import "late.css";
            """);

        var resolved = CssImportResolver.ResolveImports(sheet, loader);

        // CSS Cascade 5 §2: @import after style rule is invalid; ignored.
        Assert.Empty(loader.Requests);
        Assert.Single(resolved.Rules);
        Assert.IsType<CssStyleRule>(resolved.Rules[0]);
    }

    [Fact]
    public void Misplaced_Import_After_Layer_Block_Is_Ignored()
    {
        var loader = new MockLoader()
            .Add("late.css", "p { color: blue; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            @layer base { div { color: red; } }
            @import "late.css";
            """);

        var resolved = CssImportResolver.ResolveImports(sheet, loader);

        Assert.Empty(loader.Requests);
        Assert.Single(resolved.Rules);
        Assert.IsType<CssAtRule>(resolved.Rules[0]);
    }

    [Fact]
    public void Preceding_Layer_Statements_Do_Not_Invalidate_Imports()
    {
        var loader = new MockLoader()
            .Add("reset.css", "* { margin: 0; }")
            .Add("theme.css", "div { color: teal; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            @layer reset, theme;
            @layer overrides;
            @import "reset.css" layer(reset);
            @import "theme.css" layer(theme);
            div { font-weight: bold; }
            """);

        var resolved = CssImportResolver.ResolveImports(sheet, loader);

        Assert.Equal(2, loader.Requests.Count);
        // Cascade 5 §2: preceding @layer statements do not terminate the leading import sequence
        Assert.Equal(5, resolved.Rules.Count);
    }

    [Fact]
    public void Layer_Statement_After_Import_Terminates_Import_Sequence()
    {
        var loader = new MockLoader()
            .Add("reset.css", "* { margin: 0; }")
            .Add("late.css", "div { color: red; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            @import "reset.css";
            @layer late_layer;
            @import "late.css";
            """);

        var resolved = CssImportResolver.ResolveImports(sheet, loader);

        // late.css is ignored because it follows an @layer statement that appeared after an @import
        Assert.Single(loader.Requests);
        Assert.Equal("reset.css", loader.Requests[0].Href);
    }

    [Fact]
    public void CssStyleEngine_AddStyleSheet_Overload_Resolves_Imports()
    {
        var loader = new MockLoader()
            .Add("shared.css", "div { border: 1px solid black; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            @import "shared.css";
            div { color: navy; }
            """);

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(sheet, loader);

        var (_, div) = NewDocWithDiv();
        var style = engine.GetComputedStyle(div);

        Assert.Equal("navy", style.GetPropertyValue("color"));
        Assert.Equal("1px", style.GetPropertyValue("border-top-width"));
    }

    [Fact]
    public void CssStyleScopeBuilder_Resolves_Imports_When_Loader_Set()
    {
        var loader = new MockLoader()
            .Add("theme.css", "div { background-color: yellow; }");

        var engine = new CssStyleEngine();
        var builder = new CssStyleScopeBuilder(engine, loader);

        builder.Sync([
            new CssStyleScopeBuilder.StyleSource(
                "@import 'theme.css'; div { color: darkblue; }",
                CssOrigin.Author)
        ], new CssEnvironment(800, 600));

        var (_, div) = NewDocWithDiv();
        var style = engine.GetComputedStyle(div);

        Assert.Equal("yellow", style.GetPropertyValue("background-color"));
        Assert.Equal("darkblue", style.GetPropertyValue("color"));
    }

    [Fact]
    public void Parity_Test_Indexed_Vs_Linear_With_Imported_Layers()
    {
        var loader = new MockLoader()
            .Add("reset.css", "div#target { color: red; margin: 5px; }")
            .Add("theme.css", "div.active { color: orange; padding: 10px; }");

        var parser = new CssParser();
        var sheet = parser.ParseStyleSheet("""
            @layer reset, theme;
            @import "reset.css" layer(reset);
            @import "theme.css" layer(theme);
            div { color: green; }
            """);

        var resolved = CssImportResolver.ResolveImports(sheet, loader);

        var linearEngine = new CssStyleEngine();
        linearEngine.AddStyleSheet(resolved);

        var indexedEngine = new CssStyleEngine();
        indexedEngine.AddStyleSheet(resolved);

        var (_, div) = NewDocWithDiv(className: "active", id: "target");

        var linearStyle = linearEngine.GetCascadedStyle(div);
        var indexedStyle = indexedEngine.GetCascadedStyle(div);

        Assert.Equal(linearStyle.Count, indexedStyle.Count);
        foreach (var (prop, linearVal) in linearStyle)
        {
            Assert.True(indexedStyle.TryGetValue(prop, out var indexedVal), $"Missing property {prop}");
            Assert.Equal(linearVal, indexedVal);
        }

        // Unlayered 'div { color: green }' beats both reset and theme layers
        Assert.Equal("green", linearStyle["color"]);
        Assert.Equal("green", indexedStyle["color"]);

        // Theme layer padding beats unlayered defaults
        Assert.Equal("10px", linearStyle["padding-top"]);
        Assert.Equal("10px", indexedStyle["padding-top"]);

        // Reset layer margin applies
        Assert.Equal("5px", linearStyle["margin-top"]);
        Assert.Equal("5px", indexedStyle["margin-top"]);
    }
}
