using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

/// <summary>
/// Covers the cascade rule index (multithreading roadmap item #11), mostly by differential
/// testing: the linear scan the index replaced is still reachable behind
/// <c>CssStyleEngine.UseRuleIndex</c>, so every case here asserts that the indexed cascade and
/// the exhaustive one agree. An optimisation whose claim is "same answers, less work" is worth
/// exactly as much as the evidence for the first half.
/// </summary>
public sealed class CssCascadeRuleIndexTests
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

    private static CssStyleEngine EngineWith(string css, bool useRuleIndex, CssEnvironment? environment = null)
    {
        var engine = new CssStyleEngine { UseRuleIndex = useRuleIndex };
        if (environment is { } env)
            engine.UpdateEnvironment(env);
        engine.AddStyleSheet(new CssParser().ParseStyleSheet(css));
        return engine;
    }

    /// <summary>
    /// Asserts the indexed and exhaustive cascades agree for every element in the document that
    /// <paramref name="build"/> populates.
    /// </summary>
    private static void AssertCascadesAgree(string css, Action<DomElement> build, CssEnvironment? environment = null)
    {
        var (_, _, indexedBody) = NewDocument();
        build(indexedBody);
        var indexed = EngineWith(css, useRuleIndex: true, environment);

        var (_, _, scannedBody) = NewDocument();
        build(scannedBody);
        var scanned = EngineWith(css, useRuleIndex: false, environment);

        var indexedElements = indexedBody.Descendants().OfType<DomElement>().ToList();
        var scannedElements = scannedBody.Descendants().OfType<DomElement>().ToList();
        Assert.Equal(scannedElements.Count, indexedElements.Count);
        Assert.NotEmpty(indexedElements);

        for (var i = 0; i < indexedElements.Count; i++)
        {
            var expected = scanned.GetCascadedStyle(scannedElements[i]);
            var actual = indexed.GetCascadedStyle(indexedElements[i]);

            Assert.Equal(
                expected.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList(),
                actual.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList());
        }
    }

    private static DomElement Add(DomElement parent, string tag, string? id = null, string? className = null)
    {
        var element = parent.OwnerDocument!.CreateElement(tag);
        if (id is not null)
            element.Id = id;
        if (className is not null)
            element.ClassName = className;
        parent.AppendChild(element);
        return element;
    }

    [Fact(Timeout = 600000)]
    public void Id_Class_And_Type_Keyed_Rules_Cascade_As_They_Did()
    {
        AssertCascadesAgree(
            """
            div { color: red; margin: 1px; }
            .c { color: green; }
            #x { color: blue; }
            span { color: purple; }
            .other { color: pink; }
            #absent { color: black; }
            """,
            body =>
            {
                Add(body, "div", id: "x", className: "c");
                Add(body, "div", className: "c");
                Add(body, "span");
                Add(body, "p");
            });
    }

    [Fact(Timeout = 600000)]
    public void Source_Order_Still_Breaks_A_Specificity_Tie()
    {
        // The index visits only candidates, so if it visited them in bucket order rather than
        // document order the loser of a tie would win. This is the case that catches that.
        AssertCascadesAgree(
            ".c { color: red; } div { color: black; } .c { color: green; }",
            body => Add(body, "div", className: "c"));
    }

    [Fact(Timeout = 600000)]
    public void A_Rule_Reachable_Through_Two_Keys_Is_Applied_Once()
    {
        AssertCascadesAgree(
            ".a, .b { color: red; } .a.b { color: green; }",
            body => Add(body, "div", className: "a b"));
    }

    [Fact(Timeout = 600000)]
    public void Descendant_And_Child_Combinators_Key_On_The_Subject()
    {
        AssertCascadesAgree(
            """
            #main .row > td.cell { color: green; }
            .row td { color: red; }
            body div span { color: blue; }
            """,
            body =>
            {
                var main = Add(body, "div", id: "main");
                var row = Add(main, "div", className: "row");
                Add(row, "td", className: "cell");
                Add(row, "td");
                Add(Add(body, "div"), "span");
            });
    }

    [Theory]
    [InlineData("[data-x] { color: green; }")]
    [InlineData("*[hidden] { color: green; }")]
    [InlineData(":not(.c) { color: green; }")]
    [InlineData(":is(.c, .d) { color: green; }")]
    [InlineData("* { color: green; }")]
    [InlineData(":root { color: green; }")]
    public void Selectors_With_No_Narrowing_Key_Still_Reach_Every_Element(string css)
    {
        // These are the ones a too-clever key extractor loses: each can match an element that
        // carries no id, class or tag the index could have filed it under.
        AssertCascadesAgree(
            css + " div { margin: 1px; }",
            body =>
            {
                var div = Add(body, "div");
                div.SetAttribute("data-x", "1");
                div.SetAttribute("hidden", "");
                Add(body, "span", className: "c");
            });
    }

    [Fact(Timeout = 600000)]
    public void A_Pseudo_Element_Rule_Keys_On_The_Element_It_Is_Attached_To()
    {
        AssertCascadesAgree(
            "div::before { color: green; } .c::after { color: red; }",
            body => Add(body, "div", className: "c"));
    }

    [Fact(Timeout = 600000)]
    public void Rules_Inside_A_Matching_Media_Query_Are_Indexed()
    {
        AssertCascadesAgree(
            "@media (min-width: 100px) { div { color: green; } } div { margin: 1px; }",
            body => Add(body, "div"),
            new CssEnvironment(800, 600));
    }

    [Fact(Timeout = 600000)]
    public void Rules_Inside_A_Non_Matching_Media_Query_Are_Dropped()
    {
        AssertCascadesAgree(
            "@media (min-width: 2000px) { div { color: green; } } div { color: red; }",
            body => Add(body, "div"),
            new CssEnvironment(800, 600));
    }

    [Fact(Timeout = 600000)]
    public void Rules_Inside_A_Supports_Condition_Follow_The_Condition()
    {
        AssertCascadesAgree(
            """
            @supports (color: green) { div { color: green; } }
            @supports (unknown-property: 1) { div { color: red; } }
            """,
            body => Add(body, "div"));
    }

    [Fact(Timeout = 600000)]
    public void Nested_Conditional_Groups_Follow_Every_Enclosing_Condition()
    {
        AssertCascadesAgree(
            "@media (min-width: 100px) { @supports (color: green) { .c { color: green; } } }",
            body => Add(body, "div", className: "c"),
            new CssEnvironment(800, 600));
    }

    [Fact(Timeout = 600000)]
    public void A_Sheet_Added_After_The_First_Cascade_Rebuilds_The_Index()
    {
        // The index is memoized against the sheet set; a stale one would silently ignore a sheet
        // added after the first query, which is exactly what a document that loads CSS late does.
        var (_, _, body) = NewDocument();
        var div = Add(body, "div", className: "c");

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(new CssParser().ParseStyleSheet("div { color: red; }"));
        Assert.Equal("red", engine.GetCascadedStyle(div)["color"]);

        engine.AddStyleSheet(new CssParser().ParseStyleSheet(".c { color: green; }"));
        Assert.Equal("green", engine.GetCascadedStyle(div)["color"]);
    }

    [Fact(Timeout = 600000)]
    public void Clearing_The_Sheets_Empties_The_Index()
    {
        var (_, _, body) = NewDocument();
        var div = Add(body, "div");

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(new CssParser().ParseStyleSheet("div { color: red; }"));
        Assert.Equal("red", engine.GetCascadedStyle(div)["color"]);

        engine.ClearStyleSheets();
        Assert.False(engine.GetCascadedStyle(div).ContainsKey("color"));
    }

    [Fact(Timeout = 600000)]
    public void Changing_The_Viewport_Re_Resolves_Media_Queries()
    {
        var (_, _, body) = NewDocument();
        var div = Add(body, "div");

        var engine = new CssStyleEngine();
        engine.UpdateEnvironment(new CssEnvironment(800, 600));
        engine.AddStyleSheet(new CssParser().ParseStyleSheet("@media (min-width: 1000px) { div { color: green; } }"));
        Assert.False(engine.GetCascadedStyle(div).ContainsKey("color"));

        engine.UpdateEnvironment(new CssEnvironment(1200, 600));
        Assert.Equal("green", engine.GetCascadedStyle(div)["color"]);
    }

    [Fact(Timeout = 600000)]
    public void The_Index_Agrees_With_The_Scan_Across_A_Generated_Sheet_And_Document()
    {
        // The shape the measurement used: many rules, few matches. Generated rather than
        // hand-written so the agreement is asserted over hundreds of selector/element pairs
        // instead of the handful anyone would think to write down.
        var css = new System.Text.StringBuilder();
        for (var i = 0; i < 300; i++)
        {
            css.Append(CultureInvariant($".gen-{i} {{ padding: {i}px; }}"));
            css.Append(CultureInvariant($"#id-{i} {{ margin: {i}px; }}"));
            css.Append(CultureInvariant($"div.gen-{i} span {{ color: rgb({i % 256}, 0, 0); }}"));
            css.Append(CultureInvariant($"[data-gen=\"{i}\"] {{ border-width: {i}px; }}"));
        }

        AssertCascadesAgree(css.ToString(), body =>
        {
            for (var i = 0; i < 60; i++)
            {
                var div = Add(body, "div", id: $"id-{i * 5}", className: $"gen-{i * 3} shared");
                div.SetAttribute("data-gen", (i * 7).ToString(System.Globalization.CultureInfo.InvariantCulture));
                Add(div, "span", className: $"gen-{i}");
            }
        });
    }

    private static string CultureInvariant(FormattableString text) =>
        FormattableString.Invariant(text);

    [Fact(Timeout = 600000)]
    public void The_Index_Files_Keyed_Rules_Away_From_The_Universal_Bucket()
    {
        // The performance claim, stated as a property rather than a timing: of these six rules
        // only the two that could match anything are left for every element to test.
        var index = CssCascadeRuleIndex.Build(
            [(new CssParser().ParseStyleSheet(
                """
                div { color: red; }
                .c { color: green; }
                #x { color: blue; }
                #main .row > td.cell { color: black; }
                [data-x] { color: pink; }
                :is(.a, .b) { color: grey; }
                """), CssOrigin.Author)],
            _ => true,
            _ => true);

        Assert.Equal(6, index.RuleCount);
        Assert.Equal(2, index.UniversalRuleCount);
    }

    [Fact(Timeout = 600000)]
    public void An_Element_Only_Sees_Rules_Its_Own_Keys_Reach()
    {
        var index = CssCascadeRuleIndex.Build(
            [(new CssParser().ParseStyleSheet(
                """
                div { color: red; }
                .c { color: green; }
                #x { color: blue; }
                span { color: purple; }
                #other { color: black; }
                .absent { color: pink; }
                """), CssOrigin.Author)],
            _ => true,
            _ => true);

        var (_, _, body) = NewDocument();
        var div = Add(body, "div", id: "x", className: "c");

        var candidates = new List<int>();
        index.CollectCandidates(div, candidates);

        // div, .c and #x — not span, #other or .absent.
        Assert.Equal([0, 1, 2], candidates);
    }

    [Fact(Timeout = 600000)]
    public void Candidates_Come_Back_In_Document_Order()
    {
        var index = CssCascadeRuleIndex.Build(
            [(new CssParser().ParseStyleSheet(
                """
                #x { color: blue; }
                * { color: grey; }
                .c { color: green; }
                div { color: red; }
                """), CssOrigin.Author)],
            _ => true,
            _ => true);

        var (_, _, body) = NewDocument();
        var div = Add(body, "div", id: "x", className: "c");

        var candidates = new List<int>();
        index.CollectCandidates(div, candidates);

        Assert.Equal([0, 1, 2, 3], candidates);
    }
}
