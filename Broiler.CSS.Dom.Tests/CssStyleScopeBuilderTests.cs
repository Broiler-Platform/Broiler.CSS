using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

public sealed class CssStyleScopeBuilderTests
{
    private static (DomDocument Document, DomElement Div) NewDocWithDiv()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        var body = document.CreateElement("body");
        var div = document.CreateElement("div");
        document.AppendChild(html);
        html.AppendChild(body);
        body.AppendChild(div);
        return (document, div);
    }

    private static CssStyleScopeBuilder NewBuilder() => new(new CssStyleEngine());

    private static CssStyleScopeBuilder.StyleSource Src(
        string css, string? media = null, CssOrigin origin = CssOrigin.Author) => new(css, origin, media);

    [Fact(Timeout = 600000)]
    public void Sync_Applies_Sheet_With_No_Media()
    {
        var (_, div) = NewDocWithDiv();
        var engine = NewBuilder().Sync([Src("div { color: red; }")], new CssEnvironment(800, 600));
        Assert.Equal("red", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact(Timeout = 600000)]
    public void Sync_Excludes_Print_Media_On_Screen_Viewport()
    {
        var (_, div) = NewDocWithDiv();
        var engine = NewBuilder().Sync(
            [Src("div { color: red; }", media: "print")], new CssEnvironment(800, 600));

        // The print-only sheet is excluded on a (screen) viewport → color stays at its
        // initial value. This is the correctness fix over the old concatenate-everything path.
        Assert.Equal("rgb(0, 0, 0)", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact(Timeout = 600000)]
    public void Sync_Includes_Screen_And_All_Media()
    {
        var (_, div) = NewDocWithDiv();
        var engine = NewBuilder().Sync(
            [Src("div { color: red; }", media: "screen"), Src("div { color: green; }", media: "all")],
            new CssEnvironment(800, 600));

        // Both media-match a screen viewport; equal specificity → later source (all) wins.
        Assert.Equal("green", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact(Timeout = 600000)]
    public void Sync_Filters_By_Viewport_Width_Media_Query_And_Resyncs()
    {
        var (_, div) = NewDocWithDiv();
        var builder = NewBuilder();
        var sources = new[] { Src("div { color: red; }", media: "(min-width: 600px)") };

        var wide = builder.Sync(sources, new CssEnvironment(800, 600));
        Assert.Equal("red", wide.GetComputedStyle(div).GetPropertyValue("color"));

        // Narrow viewport: the (min-width: 600px) sheet no longer matches → excluded on re-sync.
        var narrow = builder.Sync(sources, new CssEnvironment(400, 600));
        Assert.Equal("rgb(0, 0, 0)", narrow.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact(Timeout = 600000)]
    public void Sync_Preserves_Document_Order_Across_Separate_Sheets()
    {
        var (_, div) = NewDocWithDiv();
        var engine = NewBuilder().Sync(
            [Src("div { color: red; }"), Src("div { color: blue; }")], new CssEnvironment(800, 600));

        // Same specificity across two separately-parsed sheets → later source wins.
        Assert.Equal("blue", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact(Timeout = 600000)]
    public void Sync_Respects_Cascade_Origin()
    {
        var (_, div) = NewDocWithDiv();
        var engine = NewBuilder().Sync(
            [Src("div { color: green; }", origin: CssOrigin.Author),
             Src("div { color: red; }", origin: CssOrigin.UserAgent)],
            new CssEnvironment(800, 600));

        // Author beats UserAgent for normal declarations regardless of source order.
        Assert.Equal("green", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact(Timeout = 600000)]
    public void Sync_Reflects_Changed_Source_Text()
    {
        var (_, div) = NewDocWithDiv();
        var builder = NewBuilder();
        var env = new CssEnvironment(800, 600);

        Assert.Equal("red",
            builder.Sync([Src("div { color: red; }")], env).GetComputedStyle(div).GetPropertyValue("color"));
        // A changed source set must re-sync the engine.
        Assert.Equal("blue",
            builder.Sync([Src("div { color: blue; }")], env).GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact(Timeout = 600000)]
    public void Sync_Null_Sources_Throws() =>
        Assert.Throws<ArgumentNullException>(() => NewBuilder().Sync(null!, CssEnvironment.Headless));

    [Fact(Timeout = 600000)]
    public void Constructor_Null_Engine_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new CssStyleScopeBuilder(null!));
}
