using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

/// <summary>
/// <see cref="CssStyleEngine.GetSparseComputedStyle"/> documents its result as a fresh,
/// caller-owned map. With <c>sparseInheritance</c> it returned the engine's cached sparse map
/// itself — the map descendants inherit from — so a caller editing the map it had been told it
/// owned changed every later answer for that element and for everything below it. A null element
/// returned one static empty map shared by every null query.
/// </summary>
public sealed class CssStyleEngineSparseOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetSparseComputedStyle_Returns_A_Fresh_Caller_Owned_Map(bool sparseInheritance)
    {
        var (engine, parent, child) = Build();

        var first = engine.GetSparseComputedStyle(parent, sparseInheritance: sparseInheritance);
        var second = engine.GetSparseComputedStyle(parent, sparseInheritance: sparseInheritance);
        Assert.NotSame(first, second);
        Assert.Equal("purple", first["COLOR"]);

        var owned = Assert.IsAssignableFrom<IDictionary<string, string>>(first);
        owned["color"] = "tampered";
        owned.Remove("margin-left");

        var again = engine.GetSparseComputedStyle(parent, sparseInheritance: sparseInheritance);
        Assert.Equal("purple", again["color"]);
        Assert.Equal("3px", again["margin-left"]);
        Assert.Equal("purple", engine.GetSparseComputedStyle(child, sparseInheritance: sparseInheritance)["color"]);
    }

    [Fact]
    public void GetSparseComputedStyle_For_A_Null_Element_Returns_A_Fresh_Empty_Map()
    {
        var engine = new CssStyleEngine();

        var owned = Assert.IsAssignableFrom<IDictionary<string, string>>(engine.GetSparseComputedStyle(null!));
        owned["color"] = "red";

        Assert.Empty(engine.GetSparseComputedStyle(null!));
        Assert.Empty(engine.GetCascadedStyle(null!));
    }

    private static (CssStyleEngine Engine, DomElement Parent, DomElement Child) Build()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        var body = document.CreateElement("body");
        var parent = document.CreateElement("div");
        parent.ClassName = "p";
        var child = document.CreateElement("span");
        document.AppendChild(html);
        html.AppendChild(body);
        body.AppendChild(parent);
        parent.AppendChild(child);

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(new CssParser().ParseStyleSheet(".p { color: purple; margin-left: 3px }"));
        return (engine, parent, child);
    }
}
