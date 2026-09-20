using System;
using System.Linq;
using Broiler.Dom;
using Xunit;

namespace Broiler.CSS.Dom.Tests;

public sealed class CssCascadeLayerOrderTests
{
    private static CssStyleSheet Parse(string css) =>
        new CssParser().ParseStyleSheet(css);

    [Fact]
    public void Statement_Rule_Establishes_Layer_Precedence()
    {
        var sheet = Parse("@layer base, reset, components;");
        var order = CssCascadeLayerOrder.Build(sheet.Rules);

        Assert.Equal(3, order.LayerCount);
        Assert.Equal(["base", "reset", "components"], order.OrderedLayerNames);
        Assert.Equal(0, order.GetLayerIndex("base"));
        Assert.Equal(1, order.GetLayerIndex("reset"));
        Assert.Equal(2, order.GetLayerIndex("components"));
        Assert.Equal(CssCascadeLayerOrder.UnlayeredIndex, order.GetLayerIndex((string?)null));
        Assert.Equal(CssCascadeLayerOrder.UnlayeredIndex, order.GetLayerIndex(""));
    }

    [Fact]
    public void First_Declaration_Wins_Precedence()
    {
        // "base" appears first, then "reset". Later mentions do not alter order.
        var sheet = Parse("""
            @layer base;
            @layer reset;
            @layer base;
            @layer components, reset;
            """);

        var order = CssCascadeLayerOrder.Build(sheet.Rules);

        Assert.Equal(3, order.LayerCount);
        Assert.Equal(["base", "reset", "components"], order.OrderedLayerNames);
        Assert.True(order.GetLayerIndex("base") < order.GetLayerIndex("reset"));
        Assert.True(order.GetLayerIndex("reset") < order.GetLayerIndex("components"));
    }

    [Fact]
    public void Pre_Declaration_Overrides_Block_Source_Order()
    {
        // Statement declares reset < base. Blocks appear in reverse (base then reset).
        // Statement order must win.
        var sheet = Parse("""
            @layer reset, base;
            @layer base { p { color: red; } }
            @layer reset { p { color: green; } }
            """);

        var order = CssCascadeLayerOrder.Build(sheet.Rules);

        Assert.Equal(["reset", "base"], order.OrderedLayerNames);
        Assert.Equal(0, order.GetLayerIndex("reset"));
        Assert.Equal(1, order.GetLayerIndex("base"));
    }

    [Fact]
    public void Nested_Dotted_Layers_Sort_Hierarchically()
    {
        // Per CSS Cascade 5 §6.4.3:
        // Top-level layers: framework < custom.
        // Inside framework: grid < buttons < direct rules in framework.
        var sheet = Parse("""
            @layer framework.grid, framework.buttons;
            @layer custom;
            @layer framework { div { margin: 0; } }
            """);

        var order = CssCascadeLayerOrder.Build(sheet.Rules);

        // Child sublayers sort first, then direct styles in the parent layer, then sibling top-level layers.
        Assert.Equal(["framework.grid", "framework.buttons", "framework", "custom"], order.OrderedLayerNames);
        Assert.True(order.GetLayerIndex("framework.grid") < order.GetLayerIndex("framework.buttons"));
        Assert.True(order.GetLayerIndex("framework.buttons") < order.GetLayerIndex("framework"));
        Assert.True(order.GetLayerIndex("framework") < order.GetLayerIndex("custom"));
    }

    [Fact]
    public void Sublayers_Across_Different_Parents_Respect_Parent_Order()
    {
        // A is declared before B. All sublayers of A must sort before all sublayers of B.
        var sheet = Parse("""
            @layer A, B;
            @layer B.sub2, B.sub1;
            @layer A.sub2, A.sub1;
            """);

        var order = CssCascadeLayerOrder.Build(sheet.Rules);

        Assert.Equal(["A.sub2", "A.sub1", "A", "B.sub2", "B.sub1", "B"], order.OrderedLayerNames);
        Assert.True(order.GetLayerIndex("A.sub1") < order.GetLayerIndex("B.sub2"));
    }

    [Fact]
    public void Block_Nesting_Resolves_To_Canonical_Sublayer_Names()
    {
        var sheet = Parse("""
            @layer parent {
                @layer child1, child2;
                @layer child2 { p { color: blue; } }
                @layer child1 { p { color: green; } }
            }
            """);

        var order = CssCascadeLayerOrder.Build(sheet.Rules);

        Assert.Equal(["parent.child1", "parent.child2", "parent"], order.OrderedLayerNames);
        Assert.Equal(0, order.GetLayerIndex("parent.child1"));
        Assert.Equal(1, order.GetLayerIndex("parent.child2"));
        Assert.Equal(2, order.GetLayerIndex("parent"));
    }

    [Fact]
    public void Anonymous_Layers_Are_Unique_And_Maintain_Source_Precedence()
    {
        var sheet = Parse("""
            @layer base;
            @layer { p { color: yellow; } }
            @layer reset;
            @layer { p { color: purple; } }
            """);

        var order = CssCascadeLayerOrder.Build(sheet.Rules);

        Assert.Equal(4, order.LayerCount);
        Assert.Equal("base", order.OrderedLayerNames[0]);
        Assert.StartsWith("$anon_", order.OrderedLayerNames[1], StringComparison.Ordinal);
        Assert.Equal("reset", order.OrderedLayerNames[2]);
        Assert.StartsWith("$anon_", order.OrderedLayerNames[3], StringComparison.Ordinal);

        Assert.Equal(0, order.GetLayerIndex("base"));
        Assert.Equal(2, order.GetLayerIndex("reset"));
    }

    [Fact]
    public void Anonymous_Layer_Inside_Named_Layer_Sorts_As_Sublayer()
    {
        var sheet = Parse("""
            @layer base {
                @layer sub;
                @layer { p { color: pink; } }
            }
            """);

        var order = CssCascadeLayerOrder.Build(sheet.Rules);

        Assert.Equal(3, order.LayerCount);
        Assert.Equal("base.sub", order.OrderedLayerNames[0]);
        Assert.StartsWith("base.$anon_", order.OrderedLayerNames[1], StringComparison.Ordinal);
        Assert.Equal("base", order.OrderedLayerNames[2]);
    }

    [Fact]
    public void Import_Layers_Are_Recognized_During_Scan()
    {
        var sheet = Parse("""
            @import url(reset.css) layer(reset);
            @import url(anon.css) layer;
            @import url(base.css) layer(base);
            """);

        var order = CssCascadeLayerOrder.Build(sheet.Rules);

        Assert.Equal(3, order.LayerCount);
        Assert.Equal("reset", order.OrderedLayerNames[0]);
        Assert.StartsWith("$anon_", order.OrderedLayerNames[1], StringComparison.Ordinal);
        Assert.Equal("base", order.OrderedLayerNames[2]);
    }

    [Fact]
    public void Layers_Inside_Conditional_Groups_Participate_In_Global_Order()
    {
        var sheet = Parse("""
            @media (min-width: 1000px) {
                @layer wide;
            }
            @supports (display: grid) {
                @layer grid;
            }
            """);

        var order = CssCascadeLayerOrder.Build(sheet.Rules);

        Assert.Equal(["wide", "grid"], order.OrderedLayerNames);
    }

    [Fact]
    public void Rule_Index_Includes_Rules_Inside_Layer_Blocks_With_Correct_Layer_Index()
    {
        var sheet = Parse("""
            @layer reset, base;
            p { color: black; }
            @layer base {
                p.base-item { color: blue; }
            }
            @layer reset {
                p.reset-item { color: green; }
            }
            """);

        var index = CssCascadeRuleIndex.Build(
            [(sheet, CssOrigin.Author)],
            _ => true,
            _ => true);

        // Previously, rules inside @layer were discarded and RuleCount was only 1 (the unlayered 'p').
        // Now, all 3 style rules must be indexed.
        Assert.Equal(3, index.RuleCount);

        var layerOrder = index.GetLayerOrder(CssOrigin.Author);
        Assert.Equal(["reset", "base"], layerOrder.OrderedLayerNames);

        var resetIndex = layerOrder.GetLayerIndex("reset");
        var baseIndex = layerOrder.GetLayerIndex("base");

        Assert.Equal(0, resetIndex);
        Assert.Equal(1, baseIndex);

        // Find candidate rules for <p class="base-item reset-item">
        var doc = new DomDocument();
        var p = doc.CreateElement("p");
        p.SetAttribute("class", "base-item reset-item");

        var candidates = new System.Collections.Generic.List<int>();
        index.CollectCandidates(p, candidates);

        Assert.Equal(3, candidates.Count);

        // Verify that each candidate entry carries its proper LayerIndex:
        // Entry 0: p { color: black } => UnlayeredIndex
        // Entry 1: p.base-item => baseIndex (1)
        // Entry 2: p.reset-item => resetIndex (0)
        var entries = candidates.Select(c => index[c]).ToList();

        var unlayeredEntry = entries.Single(e => e.Rule.Selectors.Selectors[0].Text == "p");
        Assert.Equal(CssCascadeLayerOrder.UnlayeredIndex, unlayeredEntry.LayerIndex);

        var baseEntry = entries.Single(e => e.Rule.Selectors.Selectors[0].Text == "p.base-item");
        Assert.Equal(baseIndex, baseEntry.LayerIndex);

        var resetEntry = entries.Single(e => e.Rule.Selectors.Selectors[0].Text == "p.reset-item");
        Assert.Equal(resetIndex, resetEntry.LayerIndex);
    }

    [Fact]
    public void Rule_Index_Propagates_Layer_Index_Through_Nested_Conditional_Groups()
    {
        var sheet = Parse("""
            @layer base {
                @media screen {
                    @supports (display: flex) {
                        span { display: flex; }
                    }
                }
            }
            """);

        var index = CssCascadeRuleIndex.Build(
            [(sheet, CssOrigin.Author)],
            _ => true,
            _ => true);

        Assert.Equal(1, index.RuleCount);

        var doc = new DomDocument();
        var span = doc.CreateElement("span");
        var candidates = new System.Collections.Generic.List<int>();
        index.CollectCandidates(span, candidates);

        var entry = index[Assert.Single(candidates)];
        Assert.Equal("span", entry.Rule.Selectors.Selectors[0].Text);
        Assert.Equal(0, entry.LayerIndex); // base layer index is 0
    }
}
