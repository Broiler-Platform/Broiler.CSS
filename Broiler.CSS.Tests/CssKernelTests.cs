namespace Broiler.CSS.Tests;

public sealed class CssKernelTests
{
    [Fact]
    public void Parser_Preserves_Rules_AtRules_And_Raw_Values()
    {
        const string css = """
            @charset "utf-8";
            @font-face { font-family: "Phase Zero"; src: url(font.woff2); }
            @media screen {
              .card, #hero:hover { margin: 1px 2px; color: RED !important; }
            }
            """;

        var sheet = new CssParser().ParseStyleSheet(css);

        Assert.Empty(sheet.Diagnostics);
        Assert.Equal(3, sheet.Rules.Count);
        var charset = Assert.IsType<CssAtRule>(sheet.Rules[0]);
        Assert.Equal("charset", charset.Name);
        Assert.False(charset.HasBlock);

        var fontFace = Assert.IsType<CssAtRule>(sheet.Rules[1]);
        Assert.Equal("Phase Zero", fontFace.Declarations!.GetPropertyValue("font-family")!.Trim('"'));
        Assert.Equal("url(font.woff2)", fontFace.Declarations.GetPropertyValue("src"));

        var media = Assert.IsType<CssAtRule>(sheet.Rules[2]);
        var style = Assert.IsType<CssStyleRule>(Assert.Single(media.Rules));
        Assert.Equal([".card", "#hero:hover"], style.Selectors.Selectors.Select(static selector => selector.Text));
        Assert.Equal("1px 2px", style.Declarations.GetPropertyValue("margin"));
        Assert.True(style.Declarations.GetLastDeclaration("color")!.Important);
        Assert.Equal("RED", style.Declarations.GetPropertyValue("color"));
    }

    [Fact]
    public void Declaration_Parser_Recovers_And_Last_Declaration_Wins()
    {
        var declarations = new CssParser().ParseDeclarations(
            "color red; color: blue; --Token: 12px; background-image: url(data:image/png;base64,AAAA); color: red !important");

        Assert.Equal(4, declarations.Declarations.Count);
        Assert.Equal("red", declarations.GetPropertyValue("color"));
        Assert.True(declarations.GetLastDeclaration("color")!.Important);
        Assert.Equal("12px", declarations.GetPropertyValue("--Token"));
        Assert.Equal("url(data:image/png;base64,AAAA)", declarations.GetPropertyValue("background-image"));
    }

    [Theory]
    [InlineData("#host > p.item[data-state='active']:first-child", 1, 3, 1)]
    [InlineData(":is(.card, #featured)", 1, 0, 0)]
    [InlineData(":where(#featured, .card)", 0, 0, 0)]
    [InlineData("p:nth-child(2 of #featured, .card)", 1, 1, 1)]
    public void Selector_Specificity_Handles_Level_Four_Functions(
        string selector,
        int ids,
        int classes,
        int types)
    {
        Assert.Equal(
            new CssSpecificity(ids, classes, types),
            CssSelectorParser.CalculateSpecificity(selector));
    }

    [Theory]
    [InlineData("12px", CssUnit.Px)]
    [InlineData("25%", CssUnit.Percent)]
    [InlineData("-1.5rem", CssUnit.Rem)]
    [InlineData("500ms", CssUnit.Ms)]
    public void Value_Parser_Recognizes_Numeric_Units(
        string source,
        CssUnit unit)
    {
        var value = CssValueParser.Parse(source);

        // A recognised numeric value carries its parsed magnitude and unit; the
        // unit alone distinguishes number (None) / percentage (Percent) /
        // dimension (any other unit), so it fully captures the value's kind.
        Assert.NotNull(value.Numeric);
        Assert.Equal(unit, value.Numeric!.Value.Unit);
    }

    [Theory]
    [InlineData("#0f08", 0, 255, 0, 136)]
    [InlineData("rgba(255, 0, 0, 0.5)", 255, 0, 0, 128)]
    [InlineData("hsl(120, 100%, 50%)", 0, 255, 0, 255)]
    [InlineData("transparent", 0, 0, 0, 0)]
    public void Value_Parser_Recognizes_Colors(
        string source,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        Assert.True(CssValueParser.TryParseColor(source, out var color));
        Assert.Equal(new CssColor(red, green, blue, alpha), color);
    }

    [Fact]
    public void Serialization_Is_Deterministic_And_RoundTrips()
    {
        const string source = """
            .card,#hero { color: RED!important; margin: 1px 2px; }
            @supports (display: grid) { .grid { display: grid; } }
            """;
        var parser = new CssParser();
        var first = CssSerializer.Serialize(parser.ParseStyleSheet(source));
        var second = CssSerializer.Serialize(parser.ParseStyleSheet(first));

        Assert.Equal(first, second);
        Assert.Contains(".card, #hero", first, StringComparison.Ordinal);
        Assert.Contains("color: RED !important;", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Individual_Rules_And_Declaration_Blocks_Can_Be_Serialized()
    {
        var parser = new CssParser();
        var rule = Assert.Single(parser.ParseStyleSheet(".card { color: red !important; }").Rules);
        var styleRule = Assert.IsType<CssStyleRule>(rule);

        Assert.Equal(
            ".card {\n  color: red !important;\n}",
            CssSerializer.Serialize(rule).ReplaceLineEndings("\n"));
        Assert.Equal(
            "color: red !important;\n",
            CssSerializer.Serialize(styleRule.Declarations).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Parser_Reports_Unterminated_And_Malformed_Input()
    {
        var sheet = new CssParser().ParseStyleSheet(
            ".valid { color: red; broken declaration; } .unterminated { width: 10px");

        Assert.Contains(sheet.Diagnostics, static diagnostic => diagnostic.Code == "CSS2001");
        Assert.Contains(sheet.Diagnostics, static diagnostic => diagnostic.Code == "CSS1002");
        Assert.Equal(2, sheet.Rules.Count);
    }

    [Fact]
    public void Phase_Zero_Corpus_Parses_And_Serializes_Stably()
    {
        var corpusPath = FindCorpusPath();
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(corpusPath));
        var parser = new CssParser();

        foreach (var item in json.RootElement.EnumerateArray())
        {
            var css = item.GetProperty("css").GetString();
            var first = parser.ParseStyleSheet(css);
            var serialized = CssSerializer.Serialize(first);
            var second = parser.ParseStyleSheet(serialized);

            Assert.NotEmpty(first.Rules);
            Assert.Equal(serialized, CssSerializer.Serialize(second));
        }
    }

    private static string FindCorpusPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var corpus = Path.Combine(
                directory.FullName,
                "tests",
                "css",
                "phase0",
                "css-engine-differential-corpus.json");
            if (File.Exists(corpus))
                return corpus;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the CSS Phase 0 corpus.");
    }
}
