using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

// Regression coverage for the shorthand-vs-longhand origin-precedence fix: a
// higher-precedence author `background` shorthand must override a lower-precedence
// user-agent `background-color` longhand. This is the cascade half of the native
// `<dialog>` UA box-chrome slice — a UA `dialog { background-color: white }` rule
// must not leak over an author `#target { background: lime }` (WPT
// anchor-position-top-layer-003/004/006), which needs the shorthand to seed its
// longhand into the cascade so origin precedence applies.
public sealed class ShorthandLonghandOriginTests
{
    private static DomElement NewTarget()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        var body = document.CreateElement("body");
        document.AppendChild(html);
        html.AppendChild(body);
        var target = document.CreateElement("dialog");
        target.Id = "target";
        body.AppendChild(target);
        return target;
    }

    [Fact(Timeout = 600000)]
    public void Author_Background_Shorthand_Beats_UA_BackgroundColor_Longhand()
    {
        var target = NewTarget();
        var engine = new CssStyleEngine();
        engine.AddStyleSheet(
            new CssParser().ParseStyleSheet("dialog { background-color: white }"),
            CssOrigin.UserAgent);
        engine.AddStyleSheet(
            new CssParser().ParseStyleSheet("#target { background: lime }"),
            CssOrigin.Author);

        Assert.Equal("lime", engine.GetCascadedStyle(target)["background-color"]);
    }

    [Fact(Timeout = 600000)]
    public void Same_Origin_Later_BackgroundColor_Longhand_Still_Wins_Over_Earlier_Background_Shorthand()
    {
        // Seeding must not invert same-origin source order: a later background-color
        // longhand still overrides an earlier background shorthand.
        var target = NewTarget();
        var engine = new CssStyleEngine();
        engine.AddStyleSheet(
            new CssParser().ParseStyleSheet("#target { background: lime } #target { background-color: red }"),
            CssOrigin.Author);

        Assert.Equal("red", engine.GetCascadedStyle(target)["background-color"]);
    }

    [Fact(Timeout = 600000)]
    public void UA_Background_Shorthand_Still_Beaten_By_Author_Background_Shorthand()
    {
        // Control: shorthand-vs-shorthand already resolved correctly (competes at the
        // shorthand key); the fix must not disturb it.
        var target = NewTarget();
        var engine = new CssStyleEngine();
        engine.AddStyleSheet(
            new CssParser().ParseStyleSheet("dialog { background: white }"),
            CssOrigin.UserAgent);
        engine.AddStyleSheet(
            new CssParser().ParseStyleSheet("#target { background: lime }"),
            CssOrigin.Author);

        Assert.Equal("lime", engine.GetCascadedStyle(target)["background-color"]);
    }
}
