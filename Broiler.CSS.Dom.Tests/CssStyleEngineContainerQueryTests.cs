using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

/// <summary>
/// <c>@container</c> size-query evaluation in the cascade (css-conditional-5): a query container is
/// resolved by walking ancestors to the nearest size container, sized from its declared
/// width/height, and the size condition gates whether the rule's declarations apply. Unresolvable
/// containers/sizes and unsupported features fall through to "not applied", matching the prior
/// behaviour of ignoring the rule.
/// </summary>
public sealed class CssStyleEngineContainerQueryTests
{
    // container(container-type,width[,height]) > target; returns the engine + the target element.
    private static (CssStyleEngine Engine, DomElement Target) BuildScoped(
        string css, string containerType, string containerInlineStyle)
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        var container = document.CreateElement("div");
        container.SetAttribute("style", $"container-type: {containerType}; {containerInlineStyle}");
        var target = document.CreateElement("div");
        document.AppendChild(html);
        html.AppendChild(container);
        container.AppendChild(target);

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(new CssParser().ParseStyleSheet(css));
        return (engine, target);
    }

    private static string ColorOf(CssStyleEngine engine, DomElement element) =>
        engine.GetComputedStyle(element).GetPropertyValue("color");

    [Fact]
    public void Matching_Min_Width_Applies_The_Rule()
    {
        var (engine, target) = BuildScoped(
            "@container (min-width: 50px) { div { color: rgb(1, 2, 3); } }",
            "inline-size", "width: 100px;");

        Assert.Equal("rgb(1, 2, 3)", ColorOf(engine, target));
    }

    [Fact]
    public void Non_Matching_Min_Width_Does_Not_Apply()
    {
        var (engine, target) = BuildScoped(
            "div { color: rgb(9, 9, 9); } @container (min-width: 200px) { div { color: rgb(1, 2, 3); } }",
            "inline-size", "width: 100px;");

        // The query fails (100px < 200px), so the outer rule wins.
        Assert.Equal("rgb(9, 9, 9)", ColorOf(engine, target));
    }

    [Fact]
    public void Range_Syntax_Is_Evaluated()
    {
        var (engine, target) = BuildScoped(
            "@container (width > 1px) { div { color: rgb(1, 2, 3); } }",
            "size", "width: 100px; height: 100px;");

        Assert.Equal("rgb(1, 2, 3)", ColorOf(engine, target));
    }

    [Fact]
    public void Max_Width_Colon_Form_Is_Evaluated()
    {
        var (engine, target) = BuildScoped(
            "@container (max-width: 200px) { div { color: rgb(1, 2, 3); } }",
            "inline-size", "width: 100px;");

        Assert.Equal("rgb(1, 2, 3)", ColorOf(engine, target));
    }

    [Fact]
    public void Named_Container_Only_Matches_Its_Name()
    {
        var (engine, target) = BuildScoped(
            "div { color: rgb(9, 9, 9); } @container other (min-width: 1px) { div { color: rgb(1, 2, 3); } }",
            "inline-size", "container-name: main; width: 100px;");

        // The container is named "main"; a query for "other" finds no matching container → not applied.
        Assert.Equal("rgb(9, 9, 9)", ColorOf(engine, target));
    }

    [Fact]
    public void Height_Query_Against_Inline_Size_Container_Does_Not_Apply()
    {
        var (engine, target) = BuildScoped(
            "div { color: rgb(9, 9, 9); } @container (min-height: 1px) { div { color: rgb(1, 2, 3); } }",
            "inline-size", "width: 100px; height: 100px;");

        // inline-size establishes no block-axis containment, so a height feature is unresolved → false.
        Assert.Equal("rgb(9, 9, 9)", ColorOf(engine, target));
    }

    [Fact]
    public void Auto_Width_Container_Is_Unresolved_And_Does_Not_Apply()
    {
        var (engine, target) = BuildScoped(
            "div { color: rgb(9, 9, 9); } @container (min-width: 1px) { div { color: rgb(1, 2, 3); } }",
            "inline-size", "width: auto;");

        // An auto width needs layout to resolve, so the query is conservatively false.
        Assert.Equal("rgb(9, 9, 9)", ColorOf(engine, target));
    }

    [Fact]
    public void Backdrop_Pseudo_Display_None_Via_Container_On_The_Element_Itself()
    {
        // A pseudo-element's query container search includes its originating element, so a dialog that
        // is its own size container can switch its ::backdrop off (WPT dialog-backdrop-remove).
        var document = new DomDocument();
        var html = document.CreateElement("html");
        var dialog = document.CreateElement("dialog");
        dialog.SetAttribute("style", "container-type: size; width: 100px; height: 100px;");
        document.AppendChild(html);
        html.AppendChild(dialog);

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(new CssParser().ParseStyleSheet(
            "dialog::backdrop { display: block; } @container (width > 1px) { dialog::backdrop { display: none; } }"));

        var backdrop = engine.GetCascadedDeclaredValues(dialog, "::backdrop");
        Assert.Equal("none", backdrop.GetValueOrDefault("display"));
    }

    // ──────────── style() queries (WPT issue #1491, problem 6) ────────────
    // css-contain-3 makes EVERY element a style container — no container-type needed — so these
    // build a plain ancestor rather than a sized one.

    private static (CssStyleEngine Engine, DomElement Target) BuildStyleScoped(
        string css, string containerInlineStyle)
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        var container = document.CreateElement("div");
        container.SetAttribute("style", containerInlineStyle);
        var target = document.CreateElement("div");
        document.AppendChild(html);
        html.AppendChild(container);
        container.AppendChild(target);

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(new CssParser().ParseStyleSheet(css));
        return (engine, target);
    }

    [Fact]
    public void Style_Query_Matches_A_Custom_Property()
    {
        var (engine, target) = BuildStyleScoped(
            "@container style(--flag: on) { div { color: rgb(1, 2, 3); } }",
            "--flag: on;");

        Assert.Equal("rgb(1, 2, 3)", ColorOf(engine, target));
    }

    [Fact]
    public void Style_Query_Does_Not_Match_A_Different_Value()
    {
        var (engine, target) = BuildStyleScoped(
            "div { color: rgb(9, 9, 9); } @container style(--flag: on) { div { color: rgb(1, 2, 3); } }",
            "--flag: off;");

        Assert.Equal("rgb(9, 9, 9)", ColorOf(engine, target));
    }

    [Fact]
    public void Style_Query_Does_Not_Match_A_Missing_Property()
    {
        var (engine, target) = BuildStyleScoped(
            "div { color: rgb(9, 9, 9); } @container style(--flag: on) { div { color: rgb(1, 2, 3); } }",
            "color: rgb(9, 9, 9);");

        Assert.Equal("rgb(9, 9, 9)", ColorOf(engine, target));
    }

    // The case from the issue: a <color> property set to contrast-color(#000) computes to white,
    // so `style(--contrast-color: white)` matches even though the tokens differ.
    [Fact]
    public void Style_Query_Matches_A_Resolved_Contrast_Color()
    {
        var (engine, target) = BuildStyleScoped(
            "@container style(--contrast-color: white) { div { color: rgb(1, 2, 3); } }",
            "--contrast-color: contrast-color(#000);");

        Assert.Equal("rgb(1, 2, 3)", ColorOf(engine, target));
    }

    // contrast-color(#fff) is BLACK, so the same query must not match — the discriminating half
    // of the pair, without which "matches" could just mean "always true".
    [Fact]
    public void Style_Query_Does_Not_Match_The_Opposite_Contrast_Color()
    {
        var (engine, target) = BuildStyleScoped(
            "div { color: rgb(9, 9, 9); } @container style(--contrast-color: white) { div { color: rgb(1, 2, 3); } }",
            "--contrast-color: contrast-color(#fff);");

        Assert.Equal("rgb(9, 9, 9)", ColorOf(engine, target));
    }

    // A <color> property compares as a colour, not as a token stream.
    [Fact]
    public void Style_Query_Compares_Colors_Not_Spelling()
    {
        var (engine, target) = BuildStyleScoped(
            "@container style(--tint: rgb(255, 255, 255)) { div { color: rgb(1, 2, 3); } }",
            "--tint: white;");

        Assert.Equal("rgb(1, 2, 3)", ColorOf(engine, target));
    }

    // `style` is a function, not a container name: reading it as one sent the lookup hunting for
    // container-name: style, found nothing, and made every style query false.
    [Fact]
    public void Style_Is_Not_Mistaken_For_A_Container_Name()
    {
        var (engine, target) = BuildStyleScoped(
            "@container style(--flag: on) { div { color: rgb(1, 2, 3); } }",
            "--flag: on; container-name: something-else;");

        Assert.Equal("rgb(1, 2, 3)", ColorOf(engine, target));
    }

    [Fact]
    public void Style_Query_Honours_A_Container_Name()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        var named = document.CreateElement("div");
        named.SetAttribute("style", "container-name: card; --flag: on;");
        var middle = document.CreateElement("div");
        middle.SetAttribute("style", "--flag: off;");
        var target = document.CreateElement("div");
        document.AppendChild(html);
        html.AppendChild(named);
        named.AppendChild(middle);
        middle.AppendChild(target);

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(new CssParser().ParseStyleSheet(
            "@container card style(--flag: on) { div { color: rgb(1, 2, 3); } }"));

        // The named container's --flag wins over the nearer unnamed ancestor's.
        Assert.Equal("rgb(1, 2, 3)", engine.GetComputedStyle(target).GetPropertyValue("color"));
    }

    // Custom properties inherit, so a style query resolves through the ancestor chain.
    [Fact]
    public void Style_Query_Sees_An_Inherited_Custom_Property()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        html.SetAttribute("style", "--flag: on;");
        var container = document.CreateElement("div");
        var target = document.CreateElement("div");
        document.AppendChild(html);
        html.AppendChild(container);
        container.AppendChild(target);

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(new CssParser().ParseStyleSheet(
            "@container style(--flag: on) { div { color: rgb(1, 2, 3); } }"));

        Assert.Equal("rgb(1, 2, 3)", engine.GetComputedStyle(target).GetPropertyValue("color"));
    }

    // A style query on a non-custom property is not supported; it must fall through to
    // "not applied" rather than guess.
    [Fact]
    public void Style_Query_On_A_Standard_Property_Does_Not_Apply()
    {
        var (engine, target) = BuildStyleScoped(
            "div { color: rgb(9, 9, 9); } @container style(display: block) { div { color: rgb(1, 2, 3); } }",
            "display: block;");

        Assert.Equal("rgb(9, 9, 9)", ColorOf(engine, target));
    }
}
