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
}
