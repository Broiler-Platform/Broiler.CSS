using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

public sealed class CssStyleEngineTests
{
    [Theory]
    [InlineData("screen and (min-width: 600px)", 800, 600, true)]
    [InlineData("screen and (max-width: 600px)", 800, 600, false)]
    [InlineData("not screen and (min-height: 700px)", 800, 600, true)]
    public void MatchesMediaQuery_Uses_Engine_Environment(
        string query,
        int viewportWidth,
        int viewportHeight,
        bool expected)
    {
        Assert.Equal(
            expected,
            CssStyleEngine.MatchesMediaQuery(
                query,
                new CssEnvironment(viewportWidth, viewportHeight)));
    }

    private static CssStyleEngine EngineWith(string css, ICssSelectorStateProvider? state = null)
    {
        var engine = new CssStyleEngine(state);
        engine.AddStyleSheet(new CssParser().ParseStyleSheet(css));
        return engine;
    }

    // Splits a CSS value on commas that sit outside any parenthesised group —
    // used to inspect per-layer background longhands in assertions.
    private static List<string> SplitTopLevelCommas(string value)
    {
        var parts = new List<string>();
        var sb = new System.Text.StringBuilder();
        int depth = 0;
        foreach (char c in value)
        {
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;

            if (c == ',' && depth == 0)
            {
                parts.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0)
            parts.Add(sb.ToString().Trim());
        return parts;
    }

    private static (DomDocument Document, DomElement Html, DomElement Body) NewDocument()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        var body = document.CreateElement("body");
        document.AppendChild(html);
        html.AppendChild(body);
        return (document, html, body);
    }

    [Fact]
    public void Cascade_Resolves_By_Specificity_Then_Source_Order()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "x";
        div.ClassName = "c";
        body.AppendChild(div);

        var engine = EngineWith("div { color: red; } .c { color: green; } #x { color: blue; }");

        Assert.Equal("blue", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact]
    public void Cascade_Later_Declaration_Wins_On_Specificity_Tie()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.ClassName = "c";
        body.AppendChild(div);

        var engine = EngineWith(".c { color: red; } .c { color: green; }");

        Assert.Equal("green", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact]
    public void Important_Declaration_Beats_Higher_Specificity_Normal()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "x";
        div.ClassName = "c";
        body.AppendChild(div);

        var engine = EngineWith("#x { color: blue; } .c { color: green !important; }");

        Assert.Equal("green", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact]
    public void Inline_Style_Beats_Selector_Declarations()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.SetAttribute("style", "color: orange;");
        body.AppendChild(div);

        var engine = EngineWith("div { color: red; }");

        Assert.Equal("orange", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact]
    public void Inherited_Property_Flows_To_Child_Without_Own_Declaration()
    {
        var (_, _, body) = NewDocument();
        var parent = body.OwnerDocument.CreateElement("div");
        parent.ClassName = "p";
        var child = body.OwnerDocument.CreateElement("span");
        body.AppendChild(parent);
        parent.AppendChild(child);

        var engine = EngineWith(".p { color: purple; }");

        Assert.Equal("purple", engine.GetComputedStyle(child).GetPropertyValue("color"));
    }

    [Fact]
    public void Non_Inherited_Property_Falls_Back_To_Initial_Value()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("body { display: flex; }");
        var style = engine.GetComputedStyle(div);

        // display is not inherited, so the child keeps its initial value.
        Assert.Equal("inline", style.GetPropertyValue("display"));
        Assert.Equal("rgb(0, 0, 0)", style.GetPropertyValue("color"));
    }

    [Fact]
    public void Shorthand_Margin_Expands_To_Longhands()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("div { margin: 10px 20px; }");
        var style = engine.GetComputedStyle(div);

        Assert.Equal("10px", style.GetPropertyValue("margin-top"));
        Assert.Equal("20px", style.GetPropertyValue("margin-right"));
        Assert.Equal("10px", style.GetPropertyValue("margin-bottom"));
        Assert.Equal("20px", style.GetPropertyValue("margin-left"));
    }

    [Fact]
    public void Shorthand_Background_Single_Layer_Expands_To_Longhands()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("div { background: red no-repeat fixed left top / 50px 60px; }");
        var style = engine.GetComputedStyle(div);

        Assert.Equal("red", style.GetPropertyValue("background-color"));
        Assert.Equal("no-repeat", style.GetPropertyValue("background-repeat"));
        Assert.Equal("fixed", style.GetPropertyValue("background-attachment"));
        Assert.Equal("left top", style.GetPropertyValue("background-position"));
        Assert.Equal("50px 60px", style.GetPropertyValue("background-size"));
    }

    [Fact]
    public void Shorthand_Background_Preserves_All_Comma_Separated_Layers()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        // Regression: the multi-layer `background` shorthand must keep every
        // layer and emit a clean comma-joined background-image. Dropping layers
        // or leaving a trailing comma corrupts the value the renderer's paint
        // walker splits back into per-layer gradients
        // (background-attachment-margin-root WPT tests).
        var engine = EngineWith(
            "div { background: linear-gradient(rgba(0,255,0,0.5), rgba(0,0,255,0.5)), " +
            "linear-gradient(rgba(0,0,0,1), rgba(0,0,0,1)); }");
        var style = engine.GetComputedStyle(div);
        var image = style.GetPropertyValue("background-image");

        // Both gradient layers survive, split cleanly on the top-level comma.
        var layers = SplitTopLevelCommas(image);
        Assert.Equal(2, layers.Count);
        Assert.All(layers, layer => Assert.StartsWith("linear-gradient(", layer));
        // No phantom "none" layer and no stray trailing comma artifact.
        Assert.DoesNotContain("none", image);
        Assert.False(image.TrimEnd().EndsWith(","));
        Assert.Equal("transparent", style.GetPropertyValue("background-color"));
    }

    [Fact]
    public void Shorthand_Background_Does_Not_Override_Explicit_Attachment_Longhand()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        // The `background` shorthand resets attachment to its initial value, but
        // a later `background-attachment` longhand must win (matches the
        // background-attachment-margin-root tests, where the per-layer
        // scroll/fixed split is supplied as a longhand after the shorthand).
        var engine = EngineWith(
            "div { background: linear-gradient(red, blue), linear-gradient(black, black); " +
            "background-attachment: scroll, fixed; }");
        var style = engine.GetComputedStyle(div);

        Assert.Equal("scroll, fixed", style.GetPropertyValue("background-attachment"));
    }

    [Fact]
    public void Custom_Property_Is_Inherited_And_Var_Is_Resolved()
    {
        var (_, html, body) = NewDocument();
        html.SetAttribute("style", "--accent: teal;");
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("div { color: var(--accent); }");

        Assert.Equal("teal", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact]
    public void Var_Falls_Back_When_Custom_Property_Is_Missing()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("div { color: var(--missing, crimson); }");

        Assert.Equal("crimson", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact]
    public void Env_Unknown_Name_Falls_Back_To_Provided_Default()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        // env(test) is not a UA-defined variable, so the comma-separated fallback
        // is substituted (WPT css-env/fallback-nested-var, with the fallback here
        // a plain value rather than a nested var()).
        var engine = EngineWith("div { color: env(test, crimson); }");

        Assert.Equal("crimson", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact]
    public void Env_Unknown_Name_Resolves_Nested_Var_Fallback()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        // The env() fallback may itself contain a var() reference, which must be
        // resolved (WPT css-env/fallback-nested-var).
        var engine = EngineWith(
            "div { --main: rgb(0, 128, 0); color: env(test, var(--main)); }");

        Assert.Equal("rgb(0, 128, 0)", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact]
    public void Env_Unknown_Name_Without_Fallback_Is_Invalid_And_Overrides_Previous()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        // An unknown env() with no fallback is invalid at computed-value time. The
        // second declaration still wins the cascade over `green`, so the property
        // resets to its initial value rather than reviving the earlier declaration
        // (WPT css-env/unknown-env-names-override-previous).
        var engine = EngineWith(
            "div { background-color: green; background-color: env(unknown); }");

        var bg = engine.GetComputedStyle(div).GetPropertyValue("background-color");
        Assert.DoesNotContain("green", bg);
        Assert.DoesNotContain("env(", bg);
    }

    [Fact]
    public void Env_Reference_Is_A_Supported_Feature_Query()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        // @supports (property: env(name)) is true: env() is valid declaration
        // syntax regardless of whether the name resolves, so the guarded rule
        // applies (WPT css-env/at-supports).
        var engine = EngineWith(
            "@supports (background-color: env(test)) { div { color: rgb(0, 128, 0); } }");

        Assert.Equal("rgb(0, 128, 0)", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact]
    public void Cyclic_Custom_Properties_Resolve_To_Invalid_Without_Exhausting_Memory()
    {
        var (_, html, body) = NewDocument();
        // Branching mutual cycle: --a references --b twice and vice-versa. Without
        // cycle detection each resolution pass doubles the value, blowing up to
        // gigabytes and aborting the process (WPT #1136 shard SIGABRT / OOM).
        html.SetAttribute("style", "--a: var(--b) var(--b); --b: var(--a) var(--a);");
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("div { color: var(--a, fallback); }");

        // The point of the test is that this returns at all (no OOM / no hang) and
        // never emits a multi-megabyte expansion of the cyclic value.
        var color = engine.GetComputedStyle(div).GetPropertyValue("color");
        Assert.DoesNotContain("var(", color);
        Assert.True(color.Length < 64);
    }

    [Fact]
    public void Acyclic_Exponential_Custom_Property_Chain_Falls_Back_Without_Exhausting_Memory()
    {
        var (_, html, body) = NewDocument();
        // Non-cyclic "billion laughs": each property references the one below it
        // twice, so a naive substitution doubles per level and reaches gigabytes
        // by --prop30 (WPT css-variables/variable-exponential-blowup → SIGABRT).
        // No cycle exists, so cycle detection alone does not help — the length
        // bound must kick in and make the overflowing property guaranteed-invalid.
        var chain = new System.Text.StringBuilder("--prop0: lol;");
        for (var i = 1; i <= 30; i++)
            chain.Append($"--prop{i}: var(--prop{i - 1}) var(--prop{i - 1});");
        html.SetAttribute("style", chain.ToString());
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("div { background-color: var(--prop30, green); }");

        // The deep property overflows → guaranteed-invalid → the var() uses its
        // fallback. The point is this returns at all (no OOM / no hang).
        var bg = engine.GetComputedStyle(div).GetPropertyValue("background-color");
        Assert.Equal("green", bg);
    }

    [Fact]
    public void Self_Referential_Custom_Property_Does_Not_Recurse_Forever()
    {
        var (_, html, body) = NewDocument();
        html.SetAttribute("style", "--loop: var(--loop);");
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("div { width: var(--loop, 10px); }");

        // --loop is cyclic → guaranteed-invalid, so the var() falls back to 10px.
        var width = engine.GetComputedStyle(div).GetPropertyValue("width");
        Assert.Equal("10px", width);
    }

    [Fact]
    public void Media_Query_Applies_Only_When_Environment_Matches()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("div { color: red; } @media (min-width: 500px) { div { color: green; } }");

        Assert.Equal("red", engine.GetComputedStyle(div, new CssEnvironment(300, 600)).GetPropertyValue("color"));
        Assert.Equal("green", engine.GetComputedStyle(div, new CssEnvironment(800, 600)).GetPropertyValue("color"));
    }

    [Fact]
    public void Pseudo_Element_Rules_Match_Only_For_That_Pseudo()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("div::before { content: \"hi\"; }");

        Assert.Equal("\"hi\"", engine.GetComputedStyle(div, pseudoElement: "::before").GetPropertyValue("content"));
        Assert.NotEqual("\"hi\"", engine.GetComputedStyle(div).GetPropertyValue("content"));
    }

    [Fact]
    public void Relative_Font_Weight_Resolves_Against_Inherited_Weight()
    {
        var (_, _, body) = NewDocument();
        var parent = body.OwnerDocument.CreateElement("div");
        parent.ClassName = "p";
        var child = body.OwnerDocument.CreateElement("span");
        child.ClassName = "c";
        body.AppendChild(parent);
        parent.AppendChild(child);

        var engine = EngineWith(".p { font-weight: bold; } .c { font-weight: bolder; }");

        Assert.Equal("700", engine.GetComputedStyle(parent).GetPropertyValue("font-weight"));
        Assert.Equal("900", engine.GetComputedStyle(child).GetPropertyValue("font-weight"));
    }

    [Theory]
    // The size/line-height slash may carry white space on either or both sides
    // (CSS Fonts `font` shorthand). Every spacing must expand to the same
    // longhands — the family must never be swallowed into font-family.
    [InlineData("50px/1 Ahem")]
    [InlineData("50px / 1 Ahem")]
    [InlineData("50px /1 Ahem")]
    [InlineData("50px/ 1 Ahem")]
    public void Font_Shorthand_Expands_With_Whitespace_Around_LineHeight_Slash(string fontValue)
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.ClassName = "t";
        body.AppendChild(div);

        var engine = EngineWith($".t {{ font: {fontValue}; }}");
        var style = engine.GetComputedStyle(div);

        Assert.Equal("50px", style.GetPropertyValue("font-size"));
        Assert.Equal("1", style.GetPropertyValue("line-height"));
        Assert.Equal("Ahem", style.GetPropertyValue("font-family"));
    }

    [Fact]
    public void Font_Shorthand_Without_LineHeight_Keeps_Family()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.ClassName = "t";
        body.AppendChild(div);

        var engine = EngineWith(".t { font: italic bold 20px Ahem; }");
        var style = engine.GetComputedStyle(div);

        Assert.Equal("20px", style.GetPropertyValue("font-size"));
        Assert.Equal("Ahem", style.GetPropertyValue("font-family"));
    }

    [Fact]
    public void Computed_Style_Is_Recomputed_After_Attribute_Mutation()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith(".active { color: green; } div { color: red; }");

        Assert.Equal("red", engine.GetComputedStyle(div).GetPropertyValue("color"));

        div.ClassName = "active";

        Assert.Equal("green", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact]
    public void Adding_A_Stylesheet_Invalidates_Cached_Results()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = new CssStyleEngine();
        engine.AddStyleSheet(new CssParser().ParseStyleSheet("div { color: red; }"));
        Assert.Equal("red", engine.GetComputedStyle(div).GetPropertyValue("color"));

        engine.AddStyleSheet(new CssParser().ParseStyleSheet("div { color: blue; }"));
        Assert.Equal("blue", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact]
    public void Declared_Cascade_Is_Memoized_But_Invalidated_By_Mutation_And_Stylesheet_Changes()
    {
        // Guards the declared-cascade memo (CollectCascadedDeclarations cache): the
        // hot path behind GetCascadedStyle / anchor positioning must return the same
        // result on repeated queries yet still reflect DOM and stylesheet changes.
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith(".active { color: green; } div { color: red; }");

        // First query populates the memo; the immediate repeat must hit it and agree.
        Assert.Equal("red", engine.GetCascadedStyle(div)["color"]);
        Assert.Equal("red", engine.GetCascadedStyle(div)["color"]);

        // An attribute mutation raises document.Mutated -> InvalidateAll -> the memo
        // is dropped, so the higher-specificity rule now wins.
        div.ClassName = "active";
        Assert.Equal("green", engine.GetCascadedStyle(div)["color"]);

        // Registering another sheet must likewise invalidate the memo.
        engine.AddStyleSheet(new CssParser().ParseStyleSheet(".active { color: blue; }"));
        Assert.Equal("blue", engine.GetCascadedStyle(div)["color"]);
    }

    [Fact]
    public void Registered_Custom_Property_Honours_Inherits_False()
    {
        var (_, html, body) = NewDocument();
        html.SetAttribute("style", "--g: 10px;");
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("@property --g { syntax: \"<length>\"; inherits: false; initial-value: 2px; }");
        var style = engine.GetComputedStyle(div);

        // Non-inheriting registered property resets to its registered initial value.
        Assert.Equal("2px", style.GetPropertyValue("--g"));
    }

    [Fact]
    public void Missing_Element_Returns_Empty_Style()
    {
        var engine = EngineWith("div { color: red; }");
        Assert.Same(CssComputedStyle.Empty, engine.GetComputedStyle(null!));
    }

    [Fact]
    public void Invalid_Closed_Keyword_Value_Is_Discarded_Previous_Wins()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "t";
        body.AppendChild(div);

        // CSS error recovery: the invalid second declaration is dropped, so the
        // earlier valid value remains the cascade winner.
        var engine = EngineWith("#t { display: inline-block; display: supergrid; }");

        Assert.Equal("inline-block", engine.GetComputedStyle(div).GetPropertyValue("display"));
    }

    [Theory]
    // The layout engine renders inline-table; the renderer cascades through this
    // engine (Phase 5), so dropping it here makes such boxes lose their display
    // and content collapse (WPT MissingContent cluster, issue #1103).
    [InlineData("inline-table")]
    [InlineData("flow")]
    [InlineData("ruby")]
    [InlineData("ruby-text")]
    public void Valid_Display_Keyword_Is_Kept(string display)
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "t";
        body.AppendChild(div);

        // The second declaration is valid CSS Display 3 and must win over the first.
        var engine = EngineWith($"#t {{ display: inline-block; display: {display}; }}");

        Assert.Equal(display, engine.GetComputedStyle(div).GetPropertyValue("display"));
    }

    [Theory]
    [InlineData("visibility", "visible", "bogus")]
    [InlineData("white-space", "nowrap", "supernowrap")]
    [InlineData("overflow", "hidden", "everywhere")]
    [InlineData("position", "relative", "levitating")]
    public void Invalid_Keyword_Declaration_Is_Ignored(string property, string valid, string invalid)
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "t";
        body.AppendChild(div);

        var engine = EngineWith($"#t {{ {property}: {valid}; {property}: {invalid}; }}");

        Assert.Equal(valid, engine.GetComputedStyle(div).GetPropertyValue(property));
    }

    [Theory]
    // CSS Text 4: white-space is a shorthand for white-space-collapse and
    // text-wrap-mode. The modern single keywords and the two-longhand form are
    // valid and must not be dropped as invalid (WPT css-text; issue #1272 lists
    // "white-space: preserve-breaks" and "white-space: break-spaces nowrap" among
    // the most-dropped declarations).
    [InlineData("preserve-breaks")]
    [InlineData("preserve")]
    [InlineData("break-spaces")]
    [InlineData("break-spaces nowrap")]
    [InlineData("preserve-breaks nowrap")]
    [InlineData("collapse wrap")]
    public void Valid_WhiteSpace_Shorthand_Is_Kept(string whiteSpace)
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "t";
        body.AppendChild(div);

        // The second declaration is a valid CSS Text 4 white-space value and must
        // win over the first rather than being discarded by error recovery.
        var engine = EngineWith($"#t {{ white-space: pre; white-space: {whiteSpace}; }}");

        Assert.Equal(whiteSpace, engine.GetComputedStyle(div).GetPropertyValue("white-space"));
    }

    [Fact]
    public void Invalid_Vendor_Color_Is_Rejected_But_Standard_Prefixes_Pass()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "t";
        body.AppendChild(div);

        // -acid3-bogus is an unknown vendor color and must be dropped, leaving red.
        var engine = EngineWith("#t { color: red; color: -acid3-bogus; }");

        Assert.Equal("red", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact]
    public void Invalid_Inline_Declaration_Is_Discarded()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.SetAttribute("style", "display: bogusvalue;");
        body.AppendChild(div);

        // The invalid inline value is dropped; display falls back to the rule value.
        var engine = EngineWith("div { display: flex; }");

        Assert.Equal("flex", engine.GetComputedStyle(div).GetPropertyValue("display"));
    }

    [Fact]
    public void Valid_Custom_Property_With_Closed_Keyword_Name_Is_Not_Validated()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "t";
        body.AppendChild(div);

        // Custom properties accept arbitrary values regardless of any longhand name.
        var engine = EngineWith("#t { --display: supergrid; }");

        Assert.Equal("supergrid", engine.GetComputedStyle(div).GetPropertyValue("--display"));
    }

    // ---- GetCascadedStyle (renderer projection view) ----------------------

    [Fact]
    public void GetCascadedStyle_Returns_Cascaded_Value()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "t";
        body.AppendChild(div);

        var engine = EngineWith("#t { color: red; }");

        Assert.Equal("red", engine.GetCascadedStyle(div)["color"]);
    }

    [Fact]
    public void GetCascadedStyle_Does_Not_Backfill_Initials()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "t";
        body.AppendChild(div);

        var engine = EngineWith("#t { color: red; }");
        var cascaded = engine.GetCascadedStyle(div);

        // Only the declared property is present; undeclared properties are absent rather
        // than backfilled to their initial values (so the renderer keeps its own defaults).
        Assert.True(cascaded.ContainsKey("color"));
        Assert.False(cascaded.ContainsKey("display"));
        Assert.False(cascaded.ContainsKey("margin-top"));
    }

    [Fact]
    public void GetCascadedStyle_Expands_Shorthands()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "t";
        body.AppendChild(div);

        var engine = EngineWith("#t { margin: 1px 2px 3px 4px; }");
        var cascaded = engine.GetCascadedStyle(div);

        Assert.Equal("1px", cascaded["margin-top"]);
        Assert.Equal("2px", cascaded["margin-right"]);
        Assert.Equal("3px", cascaded["margin-bottom"]);
        Assert.Equal("4px", cascaded["margin-left"]);
    }

    [Fact]
    public void GetCascadedStyle_Excludes_Inline_Style()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "t";
        div.SetAttribute("style", "color: orange;");
        body.AppendChild(div);

        var engine = EngineWith("#t { background-color: blue; }");
        var cascaded = engine.GetCascadedStyle(div);

        // Inline style is applied separately by the renderer to preserve its existing
        // presentational-attribute ordering, so it must not appear here.
        Assert.Equal("blue", cascaded["background-color"]);
        Assert.False(cascaded.ContainsKey("color"));
    }

    [Fact]
    public void GetCascadedStyle_Can_Include_Inline_Style_In_The_Author_Cascade()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "t";
        div.SetAttribute("style", "color: orange; margin: 1px 2px;");
        body.AppendChild(div);

        var engine = EngineWith("#t { color: red; } #t { margin: 9px !important; }");
        var cascaded = engine.GetCascadedStyle(div, includeInlineStyle: true);

        Assert.Equal("orange", cascaded["color"]);
        Assert.Equal("9px", cascaded["margin-top"]);
        Assert.Equal("9px", cascaded["margin-right"]);
    }

    [Fact]
    public void GetCascadedStyle_Inline_Important_Beats_Author_Important()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "t";
        div.SetAttribute("style", "color: orange !important;");
        body.AppendChild(div);

        var engine = EngineWith("#t { color: red !important; }");

        Assert.Equal("orange", engine.GetCascadedStyle(div, includeInlineStyle: true)["color"]);
    }

    [Theory]
    [InlineData("::selection")]
    [InlineData("::backdrop")]
    [InlineData("::marker")]
    public void GetCascadedStyle_Projects_Generic_Pseudo_Elements(string pseudoElement)
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "t";
        body.AppendChild(div);

        var engine = EngineWith($"#t {{ color: red; }} #t{pseudoElement} {{ color: blue; }}");
        var cascaded = engine.GetCascadedStyle(div, pseudoElement);

        Assert.Equal("blue", cascaded["color"]);
    }

    [Theory]
    [InlineData("::backdrop")]
    [InlineData("::before")]
    [InlineData("::marker")]
    public void GetCascadedStyle_Matches_Bare_Pseudo_Element_As_Universal(string pseudoElement)
    {
        // A bare pseudo-element selector (e.g. `::backdrop { … }`) is equivalent
        // to `*::backdrop` and must match the pseudo-element of any element. The
        // empty base selector was previously rejected, so author `::backdrop`
        // rules (WPT css-position replaced-object-backdrop) were dropped.
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith($"{pseudoElement} {{ color: blue; }}");
        var cascaded = engine.GetCascadedStyle(div, pseudoElement);

        Assert.Equal("blue", cascaded["color"]);
    }

    [Fact]
    public void GetCascadedStyle_Expands_MultiValue_Pseudo_Element_Borders()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.SetAttribute("class", "trick");
        body.AppendChild(div);

        var engine = EngineWith(".trick::before { content: ''; border-style: none solid solid; border-width: 20px; }");
        var cascaded = engine.GetCascadedStyle(div, "::before");

        Assert.Equal("none", cascaded["border-top-style"]);
        Assert.Equal("solid", cascaded["border-right-style"]);
        Assert.Equal("solid", cascaded["border-bottom-style"]);
        Assert.Equal("solid", cascaded["border-left-style"]);
    }

    [Fact]
    public void GetCascadedStyle_Folds_Inherit_To_Parent_Computed()
    {
        var (_, _, body) = NewDocument();
        var parent = body.OwnerDocument.CreateElement("div");
        parent.Id = "p";
        var child = body.OwnerDocument.CreateElement("div");
        child.Id = "c";
        body.AppendChild(parent);
        parent.AppendChild(child);

        var engine = EngineWith("#p { color: green; } #c { color: inherit; }");
        var cascaded = engine.GetCascadedStyle(child);

        // `inherit` resolves to the parent's computed value (its used meaning) so the
        // renderer projects a concrete value.
        Assert.Equal("green", cascaded["color"]);
    }

    [Fact]
    public void GetCascadedStyle_Border_Shorthand_Resets_Omitted_Color_To_Initial()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        div.Id = "t";
        body.AppendChild(div);

        // An important `border: 1px solid` must reset border-color even though the
        // shorthand omits it, so it overrides the earlier `border: 2px dotted red`.
        var engine = EngineWith("#t { border: 2px dotted red; } #t { border: 1px solid !important; }");
        var cascaded = engine.GetCascadedStyle(div);

        Assert.Equal("1px", cascaded["border-top-width"]);
        Assert.Equal("solid", cascaded["border-top-style"]);
        // Omitted color is reset to the initial, not left as the prior red.
        Assert.Equal("rgb(0, 0, 0)", cascaded["border-top-color"]);
    }

    [Fact]
    public void CssEngineDiagnostics_Reports_Dropped_Declarations_Only()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        // position:wobble is rejected (unknown keyword); color:red is accepted.
        div.SetAttribute("style", "position: wobble; color: red");
        body.AppendChild(div);

        var engine = EngineWith("");
        var rejected = new List<(string Property, string Value)>();
        CssEngineDiagnostics.DeclarationRejected = (p, v) => rejected.Add((p, v));
        try
        {
            engine.GetComputedStyle(div);
        }
        finally
        {
            CssEngineDiagnostics.DeclarationRejected = null;
        }

        Assert.Contains(("position", "wobble"), rejected);
        Assert.DoesNotContain(rejected, e => e.Property == "color");
    }

    // ── @supports condition validity ───────────────────────────────────────
    // A malformed <supports-condition> has a false result and its rules must not
    // apply; a well-formed one is evaluated optimistically (assumed supported).

    [Theory]
    // Well-formed feature queries and combinations (valid).
    [InlineData("(color: green)", true)]
    [InlineData("(color: green) and (color: blue)", true)]
    [InlineData("(color: rainbow) or (color: green)", true)]
    [InlineData("not (color: green)", true)]
    [InlineData("not (not (color: green))", true)]
    [InlineData("((margin: 0) and (display: inline-block !important))", true)]
    [InlineData("selector(div, div)", true)]
    [InlineData("not unknown()", true)]
    [InlineData("()", true)]                 // empty general-enclosed: valid but false
    [InlineData("not ()", true)]
    [InlineData("() or (color: green)", true)]
    [InlineData("(--custom: whatever)", true)]
    // Malformed conditions (invalid → rules must not apply).
    [InlineData("color: green", false)]      // declaration missing its parentheses
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("(color: green) and (color: green) or (color: green)", false)] // mixed and/or
    [InlineData("(color: green) or (color: green) and (color: green)", false)] // mixed or/and
    [InlineData("not not (color: green)", false)]
    [InlineData("not (color: green) and not (color: green)", false)] // trailing after `not`
    [InlineData("not (color: green) or (color: green)", false)]
    [InlineData("(color: green) or(color: blue)", false)]  // `or(` is a function, not a combinator
    [InlineData("[margin: 0]", false)]                     // brackets are not a supports-in-parens
    [InlineData("(color: green", false)]                   // unclosed group
    [InlineData("(a [b)", false)]                          // unmatched bracket inside general-enclosed
    public void IsValidSupportsCondition_Matches_Grammar(string condition, bool expected)
    {
        Assert.Equal(expected, CssStyleEngine.IsValidSupportsCondition(condition));
    }

    [Fact]
    public void Supports_Rule_With_Invalid_Condition_Does_Not_Apply()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        // "@supports color: green" is invalid (missing parentheses), so the inner
        // rule must be ignored and the base green must win.
        var engine = EngineWith(
            "div { color: green; } @supports color: green { div { color: red; } }");

        Assert.Equal("green", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    [Fact]
    public void Supports_Rule_With_Valid_Condition_Applies()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        // A well-formed, supported feature query evaluates true, so the inner rule applies.
        var engine = EngineWith(
            "div { color: red; } @supports (color: green) { div { color: green; } }");

        Assert.Equal("green", engine.GetComputedStyle(div).GetPropertyValue("color"));
    }

    // ── @supports condition *evaluation* (truth) ───────────────────────────
    // A valid condition applies its rules only when it evaluates to true. These
    // mirror the WPT css-conditional/css-supports-* family: an unsupported feature
    // query (unknown property, invalid value) and a <general-enclosed> block are
    // false, and boolean combinators (and/or/not) fold those results. `true` means
    // the inner rule must win (condition evaluates true); `false` means it must not.
    [Theory]
    [InlineData("(color: green)", true)]                                   // 001
    [InlineData("((color: green))", true)]                                 // 003 nested condition
    [InlineData("(color: green !important)", true)]                        // 004 !important ignored
    [InlineData("(color: rainbow)", false)]                                // 005 invalid <color>
    [InlineData("(color: rainbow) or (color: green)", true)]               // 006
    [InlineData("(color: green) or (color: rainbow)", true)]               // 007
    [InlineData("(color: green) and (color: blue)", true)]                 // 008
    [InlineData("(color: rainbow) and (color: blue)", false)]             // 009
    [InlineData("(color: blue) and (color: rainbow)", false)]            // 010
    [InlineData("(color: rainbow) or (color: iridescent) or (color: green)", true)] // 011
    [InlineData("(color: red) and (color: green) and (color: blue)", true)]         // 012
    [InlineData("(color: green) and (color: green) or (color: green)", false)] // 013 mixed and/or
    [InlineData("(color: green) or (color: green) and (color: green)", false)] // 014 mixed or/and
    [InlineData("not (color: rainbow)", true)]                             // 016
    [InlineData("not not (color: green)", false)]                          // 017 invalid
    [InlineData("not (not (color: green))", true)]                         // 018
    [InlineData("not (color: rainbow) and not (color: iridescent)", false)] // 019 invalid
    [InlineData("(unknown: green)", false)]                                // 020 unknown property
    [InlineData("(unknown: green) or (color: green)", true)]               // 021
    [InlineData("(unknown:) or (color: green)", true)]                     // 022 empty value
    [InlineData("(unknown) or (color: green)", true)]                      // 023 general-enclosed
    [InlineData("(color:) or (color: green)", true)]                       // 031 empty value
    [InlineData("not (color: rainbow) or (color: green)", false)]          // 029 mixed not/or
    [InlineData("(not (color: rainbow) or (color: green))", false)]        // 030 general-enclosed
    [InlineData("not (@page)", true)]                                      // 032 not(general-enclosed)
    [InlineData("an-extension(of some kind) or (color: green)", true)]     // 036 unknown fn or true
    [InlineData("(color: green) or an-extension(that is [unbalanced)", false)] // 037 unbalanced
    [InlineData("not(unknown: unknown)", false)]                           // 038 not( is a function
    [InlineData("(color: green) or(color: blue)", false)]                  // 039 or( is a function
    [InlineData("not ()", true)]                                           // 040 not(empty)
    public void Supports_Rule_Applies_Only_When_Condition_Evaluates_True(string condition, bool shouldApply)
    {
        var (_, html, _) = NewDocument();
        var engine = EngineWith(
            $"html {{ color: green; }} @supports {condition} {{ html {{ color: red; }} }}");

        var expected = shouldApply ? "red" : "green";
        Assert.Equal(expected, engine.GetComputedStyle(html).GetPropertyValue("color"));
    }

    // CSS Text 4 §text-align / §text-align-last (issue #1276): the value validator
    // must accept 'justify-all' for text-align and the text-align-last keywords, so
    // the declaration survives the cascade and reaches the renderer instead of being
    // dropped as invalid.
    [Theory]
    [InlineData("justify-all")]
    [InlineData("justify")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("match-parent")]
    public void TextAlign_Accepts_JustifyAll_And_Standard_Values(string value)
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith($"div {{ text-align: {value}; }}");
        Assert.Equal(value, engine.GetCascadedStyle(div)["text-align"]);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("center")]
    [InlineData("justify")]
    public void TextAlignLast_Accepts_Standard_Values(string value)
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith($"div {{ text-align-last: {value}; }}");
        Assert.Equal(value, engine.GetCascadedStyle(div)["text-align-last"]);
    }

    [Fact]
    public void TextAlignLast_Drops_Invalid_Value()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("div { text-align-last: bogus; }");
        Assert.False(engine.GetCascadedStyle(div).ContainsKey("text-align-last"));
    }

    [Fact]
    public void TextAlignLast_Is_Inherited()
    {
        var (_, _, body) = NewDocument();
        var parent = body.OwnerDocument.CreateElement("div");
        var child = body.OwnerDocument.CreateElement("span");
        body.AppendChild(parent);
        parent.AppendChild(child);

        var engine = EngineWith("div { text-align-last: justify; }");
        Assert.Equal("justify", engine.GetComputedStyle(child).GetPropertyValue("text-align-last"));
    }

    // ---- GetSparseComputedStyle: computed pipeline without initial-value backfill ----

    [Fact]
    public void GetSparseComputedStyle_Omits_Undeclared_NonInherited_Property()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        // No UA sheet is loaded by EngineWith, so `display` has no author or UA rule.
        var engine = EngineWith("div { color: red; }");
        var sparse = engine.GetSparseComputedStyle(div);

        // The declared property is present; the undeclared, non-inherited ones stay
        // ABSENT (read back as null) rather than resolving to their initial value —
        // the null-for-undeclared contract the anchor/layout consumers depend on.
        Assert.Equal("red", sparse["color"]);
        Assert.False(sparse.ContainsKey("display"));
        Assert.False(sparse.ContainsKey("position"));

        // The one documented exception: ApplyLogicalSizeAliases always materialises the
        // physical/logical size pair (to "auto" when undeclared), exactly as the bridge's
        // GetComputedProps does — so the sparse contract deliberately carries width/height
        // even when undeclared. Parity with the bridge, not a leak of the initial backfill.
        Assert.Equal("auto", sparse["width"]);

        // GetComputedStyle (full initials) would instead report the initial values,
        // which is exactly why those consumers cannot use it directly.
        var full = engine.GetComputedStyle(div);
        Assert.Equal("inline", full.GetPropertyValue("display"));
    }

    [Fact]
    public void GetSparseComputedStyle_Backfills_Inherited_Property_From_Parent()
    {
        var (_, _, body) = NewDocument();
        var parent = body.OwnerDocument.CreateElement("div");
        parent.ClassName = "p";
        var child = body.OwnerDocument.CreateElement("span");
        body.AppendChild(parent);
        parent.AppendChild(child);

        var engine = EngineWith(".p { color: purple; }");

        // `color` is inherited: the sparse projection backfills it onto the child even
        // though the child declares nothing. This is the difference from
        // GetCascadedStyle, which is stylesheet-declared-only (no inheritance backfill).
        Assert.Equal("purple", engine.GetSparseComputedStyle(child)["color"]);
        Assert.False(engine.GetCascadedStyle(child).ContainsKey("color"));

        // Still no initial backfill: an undeclared non-inherited property is absent.
        Assert.False(engine.GetSparseComputedStyle(child).ContainsKey("display"));
    }

    [Fact]
    public void GetSparseComputedStyle_Expands_Shorthands_Without_Initial_Longhands()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("div { margin: 10px 20px; }");
        var sparse = engine.GetSparseComputedStyle(div);

        // The declared shorthand expands to longhands...
        Assert.Equal("10px", sparse["margin-top"]);
        Assert.Equal("20px", sparse["margin-right"]);
        Assert.Equal("10px", sparse["margin-bottom"]);
        Assert.Equal("20px", sparse["margin-left"]);

        // ...but an unrelated, undeclared longhand is NOT backfilled to its initial.
        Assert.False(sparse.ContainsKey("padding-top"));
        Assert.Equal("0px", engine.GetComputedStyle(div).GetPropertyValue("padding-top"));
    }

    [Fact]
    public void GetSparseComputedStyle_Is_A_Subset_Of_GetComputedStyle_Agreeing_On_Shared_Keys()
    {
        var (_, _, body) = NewDocument();
        var div = body.OwnerDocument.CreateElement("div");
        body.AppendChild(div);

        var engine = EngineWith("div { color: red; margin: 5px; }");
        var sparse = engine.GetSparseComputedStyle(div);
        var full = engine.GetComputedStyle(div);

        // For a plain (non-form-control, no-logical-property) element the sparse map is
        // exactly the full computed map minus the initial-value backfill: every sparse
        // key is present in full with an equal value, and full carries strictly more
        // keys (the undeclared-initials the sparse view deliberately omits).
        foreach (var kv in sparse)
            Assert.Equal(kv.Value, full.GetPropertyValue(kv.Key));

        Assert.False(sparse.ContainsKey("display"));
        Assert.Equal("inline", full.GetPropertyValue("display"));
    }

    [Fact]
    public void GetSparseComputedStyle_Returns_Empty_For_Null_Element()
    {
        var engine = EngineWith("div { color: red; }");
        Assert.Empty(engine.GetSparseComputedStyle(null!));
    }

    [Fact]
    public void GetSparseComputedStyle_SparseInheritance_Omits_Nowhere_Declared_Inherited_Property()
    {
        var (_, _, body) = NewDocument();
        var parent = body.OwnerDocument.CreateElement("div");
        var child = body.OwnerDocument.CreateElement("span");
        body.AppendChild(parent);
        parent.AppendChild(child);

        // Only `color` is declared (on the parent); `visibility` is inherited but declared
        // nowhere on the ancestor chain.
        var engine = EngineWith("div { color: purple; }");

        var full = engine.GetSparseComputedStyle(child);                          // default: full inheritance
        var sparse = engine.GetSparseComputedStyle(child, sparseInheritance: true);

        // An inherited property DECLARED on an ancestor propagates in both modes.
        Assert.Equal("purple", full["color"]);
        Assert.Equal("purple", sparse["color"]);

        // A nowhere-declared inherited property materialises under FULL inheritance (from the
        // parent's full computed style, which carries root initials) but stays ABSENT under
        // SPARSE inheritance — the class-2 distinction the bridge's GetComputedProps relies on.
        Assert.True(full.ContainsKey("visibility"));
        Assert.False(sparse.ContainsKey("visibility"));

        // Sparse inheritance still omits the initial backfill for non-inherited props.
        Assert.False(sparse.ContainsKey("display"));
    }

    [Fact]
    public void GetSparseComputedStyle_SparseInheritance_Propagates_Declared_Value_Down_Ancestor_Chain()
    {
        var (_, _, body) = NewDocument();
        var grandparent = body.OwnerDocument.CreateElement("div");
        grandparent.ClassName = "gp";
        var parent = body.OwnerDocument.CreateElement("div");
        var child = body.OwnerDocument.CreateElement("span");
        body.AppendChild(grandparent);
        grandparent.AppendChild(parent);
        parent.AppendChild(child);

        var engine = EngineWith(".gp { color: teal; }");

        // `color` declared on the grandparent propagates two levels down under sparse
        // inheritance (exercising the cached sparse recursion up the ancestor chain),
        // while a nowhere-declared inherited property stays absent.
        var sparse = engine.GetSparseComputedStyle(child, sparseInheritance: true);
        Assert.Equal("teal", sparse["color"]);
        Assert.False(sparse.ContainsKey("visibility"));
    }
}
