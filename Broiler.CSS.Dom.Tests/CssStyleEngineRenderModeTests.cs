using System.Collections.Concurrent;
using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

/// <summary>
/// <see cref="CssPagedMedia"/> and <see cref="CssDocumentMode.QuirksMode"/> are per-thread render
/// levers, but the engine's memo caches and rule index are shared by every thread. Before results
/// were keyed by render mode, whichever mode computed a result first answered for every mode: a
/// screen render served a print render's cascade, and a standards document a quirks document's —
/// on one thread after a mode change, and between threads styling in different modes at once.
/// </summary>
public sealed class CssStyleEngineRenderModeTests
{
    private const string PrintSheet = "div { color: blue } @media print { div { color: red } }";

    [Theory]
    [InlineData("computed", false)]
    [InlineData("computed", true)]
    [InlineData("cascaded", false)]
    [InlineData("cascaded", true)]
    [InlineData("sparse", false)]
    [InlineData("sparse", true)]
    [InlineData("declared", false)]
    [InlineData("declared", true)]
    public void A_Result_Cached_On_One_Surface_Is_Not_Served_On_The_Other(string path, bool printFirst)
    {
        var (engine, div) = Build(PrintSheet);
        var color = ColorReader(engine, div, path);

        string? Screen() => color();
        string? Print()
        {
            using (CssPagedMedia.Pin(480, 288))
                return color();
        }

        if (printFirst)
        {
            Assert.Equal("red", Print());
            Assert.Equal("blue", Screen());
        }
        else
        {
            Assert.Equal("blue", Screen());
            Assert.Equal("red", Print());
        }

        // Each surface keeps answering from its own entry.
        Assert.Equal("blue", Screen());
        Assert.Equal("red", Print());
    }

    [Fact]
    public void Page_Areas_Of_Different_Sizes_Are_Cached_Separately()
    {
        var (engine, div) = Build("div { color: blue } @media (max-width: 500px) { div { color: red } }");

        string? ColorOnPage(int width, int height)
        {
            using (CssPagedMedia.Pin(width, height))
                return engine.GetComputedStyle(div).GetPropertyValue("color");
        }

        Assert.Equal("red", ColorOnPage(480, 288));
        Assert.Equal("blue", ColorOnPage(960, 600));
        Assert.Equal("red", ColorOnPage(480, 288));
    }

    /// <summary>The unitless-length quirk: <c>width: 200</c> is valid only in quirks mode.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_Declaration_Cached_In_One_Document_Mode_Is_Not_Served_In_The_Other(bool quirksFirst)
    {
        var (engine, div) = Build("div { width: 200 }");

        string? Width(bool quirks) => InDocumentMode(quirks, () => engine.GetCascadedStyle(div).GetValueOrDefault("width"));

        if (quirksFirst)
        {
            Assert.Equal("200", Width(quirks: true));
            Assert.Null(Width(quirks: false));
        }
        else
        {
            Assert.Null(Width(quirks: false));
            Assert.Equal("200", Width(quirks: true));
        }
    }

    /// <summary>
    /// <c>@supports</c> is decided when the rule index is built, and it validates its declaration in
    /// the thread's document mode — so the index itself has to be per mode, not only the memos.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_Rule_Index_Built_In_One_Document_Mode_Is_Not_Used_In_The_Other(bool quirksFirst)
    {
        var (engine, div) = Build("div { color: blue } @supports (width: 200) { div { color: red } }");

        string? Color(bool quirks) => InDocumentMode(quirks, () => engine.GetCascadedStyle(div).GetValueOrDefault("color"));

        if (quirksFirst)
        {
            Assert.Equal("red", Color(quirks: true));
            Assert.Equal("blue", Color(quirks: false));
        }
        else
        {
            Assert.Equal("blue", Color(quirks: false));
            Assert.Equal("red", Color(quirks: true));
        }
    }

    [Fact]
    public async Task Threads_Styling_For_Print_And_Screen_At_Once_Each_See_Their_Own_Surface()
    {
        var (engine, div) = Build(PrintSheet);
        var mismatches = new ConcurrentQueue<string>();
        using var start = new Barrier(2);

        void Run(bool print)
        {
            using var pin = print ? CssPagedMedia.Pin(480, 288) : null;
            start.SignalAndWait();
            for (var i = 0; i < 200; i++)
            {
                var color = engine.GetComputedStyle(div).GetPropertyValue("color");
                if (color != (print ? "red" : "blue"))
                    mismatches.Enqueue($"{(print ? "print" : "screen")} thread read {color}");
                if (i % 20 == 0)
                    engine.InvalidateComputedStyleCaches();
            }
        }

        await Task.WhenAll(Task.Run(() => Run(print: true)), Task.Run(() => Run(print: false)));

        Assert.Empty(mismatches);
    }

    private static Func<string?> ColorReader(CssStyleEngine engine, DomElement div, string path) => path switch
    {
        "computed" => () => engine.GetComputedStyle(div).GetPropertyValue("color"),
        "cascaded" => () => engine.GetCascadedStyle(div).GetValueOrDefault("color"),
        "sparse" => () => engine.GetSparseComputedStyle(div, sparseInheritance: true).GetValueOrDefault("color"),
        "declared" => () => engine.GetCascadedDeclaredValues(div).GetValueOrDefault("color"),
        _ => throw new ArgumentOutOfRangeException(nameof(path)),
    };

    private static string? InDocumentMode(bool quirks, Func<string?> query)
    {
        var previous = CssDocumentMode.QuirksMode;
        CssDocumentMode.QuirksMode = quirks;
        try
        {
            return query();
        }
        finally
        {
            CssDocumentMode.QuirksMode = previous;
        }
    }

    private static (CssStyleEngine Engine, DomElement Div) Build(string css)
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        var body = document.CreateElement("body");
        var div = document.CreateElement("div");
        document.AppendChild(html);
        html.AppendChild(body);
        body.AppendChild(div);

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(new CssParser().ParseStyleSheet(css));
        return (engine, div);
    }
}
