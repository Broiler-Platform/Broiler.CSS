using System;
using System.Collections.Generic;
using Broiler.Dom;
using Xunit;

namespace Broiler.CSS.Dom.Tests;

public sealed class CssCascadeLayerSortingTests
{
    private static (DomDocument Document, DomElement Html, DomElement Body) NewDocument()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        var body = document.CreateElement("body");
        document.AppendChild(html);
        html.AppendChild(body);
        return (document, html, body);
    }

    private static CssStyleEngine EngineWith(string css)
    {
        var engine = new CssStyleEngine();
        engine.AddStyleSheet(new CssParser().ParseStyleSheet(css), CssOrigin.Author);
        return engine;
    }

    // ---- 1. Normal Author Cascade ------------------------------------------

    [Fact]
    public void Normal_Author_Unlayered_Beats_Layered_Regardless_Of_Specificity()
    {
        // CSS Cascade 5 §4.1: Normal unlayered declarations override declarations in layers.
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "target";
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer base {
                #target { color: red; }
            }
            div {
                color: green;
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("green", cascaded["color"]);
    }

    [Fact]
    public void Normal_Author_Later_Layer_Beats_Earlier_Layer_Regardless_Of_Specificity()
    {
        // Later layer beats earlier layer, overriding selector specificity.
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "target";
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer a, b;
            @layer a {
                #target { color: red; }
            }
            @layer b {
                div { color: green; }
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("green", cascaded["color"]);
    }

    [Fact]
    public void Pre_Declaration_Establishes_Layer_Order_For_Normal_Cascade()
    {
        // Even if block for 'b' appears before 'a', statement @layer b, a makes 'a' win.
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "target";
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer b, a;
            @layer a {
                div { color: green; }
            }
            @layer b {
                #target { color: red; }
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("green", cascaded["color"]);
    }

    // ---- 2. Important Author Cascade ---------------------------------------

    [Fact]
    public void Important_Author_Layered_Beats_Unlayered()
    {
        // CSS Cascade 5 §4.1: For !important declarations, layered rules override unlayered rules.
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "target";
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer base {
                div { color: green !important; }
            }
            #target {
                color: red !important;
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("green", cascaded["color"]);
    }

    [Fact]
    public void Important_Author_Earlier_Layer_Beats_Later_Layer()
    {
        // CSS Cascade 5 §4.1: For !important declarations, earlier layers override later layers.
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "target";
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer a, b;
            @layer a {
                div { color: green !important; }
            }
            @layer b {
                #target { color: red !important; }
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("green", cascaded["color"]);
    }

    // ---- 3. Intra-Layer Specificity & Order --------------------------------

    [Fact]
    public void Within_Same_Layer_Higher_Specificity_Wins()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "target";
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer base {
                div { color: red; }
                #target { color: green; }
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("green", cascaded["color"]);
    }

    [Fact]
    public void Within_Same_Layer_Source_Order_Breaks_Tie()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.ClassName = "c1 c2";
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer base {
                .c1 { color: red; }
                .c2 { color: green; }
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("green", cascaded["color"]);
    }

    // ---- 4. Nested & Anonymous Layers --------------------------------------

    [Fact]
    public void Nested_Layers_Follow_Hierarchical_Order()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer framework {
                @layer base { div { color: red; } }
                @layer theme { div { color: green; } }
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("green", cascaded["color"]);
    }

    [Fact]
    public void Anonymous_Layers_Participate_In_Declaration_Order()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer {
                div { color: red; }
            }
            @layer {
                div { color: green; }
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("green", cascaded["color"]);
    }

    // ---- 5. revert-layer Rollback -------------------------------------------

    [Fact]
    public void Revert_Layer_In_Later_Layer_Rolls_Back_To_Earlier_Layer()
    {
        // CSS Cascade 5 §4.3: revert-layer rolls back to previous cascade layer.
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer base {
                div { color: red; }
            }
            @layer theme {
                div { color: green; }
            }
            @layer overrides {
                div { color: revert-layer; }
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("green", cascaded["color"]);
    }

    [Fact]
    public void Revert_Layer_Chained_Rolls_Back_Multiple_Layers()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer base {
                div { color: red; }
            }
            @layer theme {
                div { color: revert-layer; }
            }
            @layer overrides {
                div { color: revert-layer; }
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("red", cascaded["color"]);
    }

    [Fact]
    public void Revert_Layer_In_Unlayered_Rolls_Back_To_Highest_Layer()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer base {
                div { color: red; }
            }
            @layer theme {
                div { color: green; }
            }
            div {
                color: revert-layer;
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("green", cascaded["color"]);
    }

    [Fact]
    public void Revert_Layer_Does_Not_Roll_Back_Within_Same_Layer()
    {
        // revert-layer rolls back to the PREVIOUS cascade layer, not to another
        // declaration in the same layer.
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "special";
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer base {
                div { color: green; }
            }
            @layer theme {
                div { color: red; }
                #special { color: revert-layer; }
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("green", cascaded["color"]);
    }

    [Fact]
    public void Revert_Layer_In_First_Layer_Rolls_Back_To_UserAgent_Origin()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = new CssStyleEngine();
        // UA sheet specifies display: block
        engine.AddStyleSheet(new CssParser().ParseStyleSheet("div { display: block; }"), CssOrigin.UserAgent);
        // Author sheet in layer rolls back display
        engine.AddStyleSheet(new CssParser().ParseStyleSheet("""
            @layer base {
                div { display: revert-layer; }
            }
            """), CssOrigin.Author);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("block", cascaded["display"]);
    }

    [Fact]
    public void Revert_Layer_Resolves_As_Unset_When_No_Prior_Declaration_Exists()
    {
        var (_, _, body) = NewDocument();
        body.SetAttribute("style", "color: rgb(10, 20, 30);");
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer base {
                div { color: revert-layer; }
            }
            """);

        // Inherited property 'color' with no prior declaration behaves as unset -> inherits from parent.
        var computed = engine.GetComputedStyle(div);
        Assert.Equal("rgb(10, 20, 30)", computed.GetPropertyValue("color"));
    }

    // ---- 6. Inline Style Interactions --------------------------------------

    [Fact]
    public void Inline_Style_Beats_All_Author_Layers()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "target";
        div.SetAttribute("style", "color: purple;");
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer theme {
                #target { color: green; }
            }
            #target {
                color: red;
            }
            """);

        var cascaded = engine.GetCascadedStyle(div, includeInlineStyle: true);
        Assert.Equal("purple", cascaded["color"]);
    }

    [Fact]
    public void Inline_Important_Beats_Layered_Important()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "target";
        div.SetAttribute("style", "color: purple !important;");
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer theme {
                #target { color: green !important; }
            }
            """);

        var cascaded = engine.GetCascadedStyle(div, includeInlineStyle: true);
        Assert.Equal("purple", cascaded["color"]);
    }

    [Fact]
    public void Inline_Revert_Layer_Rolls_Back_To_Author_Stylesheet()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.SetAttribute("style", "color: revert-layer;");
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer base {
                div { color: red; }
            }
            @layer theme {
                div { color: green; }
            }
            """);

        var cascaded = engine.GetCascadedStyle(div, includeInlineStyle: true);
        Assert.Equal("green", cascaded["color"]);
    }

    // ---- 7. Important revert-layer Rollback ---------------------------------

    [Fact]
    public void Important_Revert_Layer_Rolls_Back_To_Later_Important_Layer()
    {
        // In !important, layer 'a' has higher priority than layer 'b'.
        // If 'a' declares revert-layer !important, it rolls back to 'b'.
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer a, b;
            @layer a {
                div { color: revert-layer !important; }
            }
            @layer b {
                div { color: green !important; }
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("green", cascaded["color"]);
    }

    [Fact]
    public void Important_Revert_Layer_Rolls_Back_To_Unlayered_Important()
    {
        // Layered !important beats unlayered !important.
        // Rolling back from layered !important reaches unlayered !important.
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer a {
                div { color: revert-layer !important; }
            }
            div {
                color: blue !important;
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("blue", cascaded["color"]);
    }

    [Fact]
    public void Important_Revert_Layer_Rolls_Back_To_Normal_Author_When_No_Other_Important()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer a {
                div { color: revert-layer !important; }
            }
            div {
                color: blue;
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("blue", cascaded["color"]);
    }

    // ---- 8. Shorthand revert-layer -----------------------------------------

    [Fact]
    public void Shorthand_Revert_Layer_Rolls_Back_All_Longhands()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("""
            @layer base {
                div { margin: 10px 20px; }
            }
            @layer overrides {
                div { margin: revert-layer; }
            }
            """);

        var cascaded = engine.GetCascadedStyle(div);
        Assert.Equal("10px", cascaded["margin-top"]);
        Assert.Equal("20px", cascaded["margin-right"]);
        Assert.Equal("10px", cascaded["margin-bottom"]);
        Assert.Equal("20px", cascaded["margin-left"]);
    }

    // ---- 9. Parity: RuleIndex vs LinearScan --------------------------------

    [Fact]
    public void Parity_RuleIndex_Vs_LinearScan_Produces_Identical_Cascaded_Styles()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "main";
        div.ClassName = "card featured";
        div.SetAttribute("style", "padding: 5px;");
        body.AppendChild(div);

        var css = """
            @layer reset, base, components, overrides;

            @layer reset {
                * { margin: 0; padding: 0; box-sizing: border-box; }
            }

            @layer base {
                div { color: black; background-color: white; font-size: 14px; }
                #main { color: navy; }
            }

            @layer components {
                .card { padding: 16px; border: 1px solid gray; }
                .featured { border-color: gold; }
            }

            @layer overrides {
                .featured { color: revert-layer; }
                #main { background-color: lightyellow; }
            }

            /* Unlayered normal author styles */
            div.card { font-weight: bold; }
            """;

        var indexedEngine = new CssStyleEngine { UseRuleIndex = true };
        indexedEngine.AddStyleSheet(new CssParser().ParseStyleSheet(css), CssOrigin.Author);

        var linearEngine = new CssStyleEngine { UseRuleIndex = false };
        linearEngine.AddStyleSheet(new CssParser().ParseStyleSheet(css), CssOrigin.Author);

        var indexedStyle = indexedEngine.GetCascadedStyle(div, includeInlineStyle: true);
        var linearStyle = linearEngine.GetCascadedStyle(div, includeInlineStyle: true);

        Assert.Equal(linearStyle.Count, indexedStyle.Count);
        foreach (var (prop, val) in linearStyle)
        {
            Assert.True(indexedStyle.TryGetValue(prop, out var indexedVal), $"Property '{prop}' missing in indexed cascade");
            Assert.Equal(val, indexedVal);
        }
    }
}
