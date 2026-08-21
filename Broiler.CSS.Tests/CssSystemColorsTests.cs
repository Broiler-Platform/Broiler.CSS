namespace Broiler.CSS.Tests;

/// <summary>
/// Pins <see cref="CssSystemColors"/> — the CSS Color 4 §6 system colors.
/// <para>
/// REGRESSION GUARD (WPT issue #1491, problem 28): the table used to carry only
/// <c>Field</c>/<c>FieldText</c>, so every other system color fell through to
/// the named-color lookup and resolved to black.
/// <c>forced-colors-mode-20.html</c> paints <c>background-color: Canvas</c> and
/// rendered 98% black against Chromium's 98% white. The light palette is the
/// default because that is what the Chromium references are screenshots of.
/// </para>
/// </summary>
public sealed class CssSystemColorsTests
{
    [Theory]
    // The whole-canvas failure from the issue: Canvas is white, not black.
    [InlineData("Canvas", 255, 255, 255)]
    [InlineData("CanvasText", 0, 0, 0)]
    [InlineData("LinkText", 0, 0, 238)]
    [InlineData("VisitedText", 85, 26, 139)]
    [InlineData("ActiveText", 255, 0, 0)]
    [InlineData("ButtonFace", 239, 239, 239)]
    [InlineData("ButtonText", 0, 0, 0)]
    [InlineData("ButtonBorder", 118, 118, 118)]
    [InlineData("Field", 255, 255, 255)]
    [InlineData("FieldText", 0, 0, 0)]
    [InlineData("HighlightText", 255, 255, 255)]
    [InlineData("Mark", 255, 255, 0)]
    [InlineData("MarkText", 0, 0, 0)]
    [InlineData("GrayText", 128, 128, 128)]
    public void Resolves_Light_Palette_By_Default(string name, byte r, byte g, byte b)
    {
        Assert.True(CssSystemColors.TryResolve(name, out var color));
        Assert.Equal(new CssColor(r, g, b), color);
    }

    [Theory]
    [InlineData("canvas")]
    [InlineData("CANVAS")]
    [InlineData("  Canvas  ")]
    public void Resolution_Is_Case_And_Whitespace_Insensitive(string name)
    {
        Assert.True(CssSystemColors.TryResolve(name, out var color));
        Assert.Equal(new CssColor(255, 255, 255), color);
    }

    // The color-scheme: dark switch the exit gate asks for. Canvas/CanvasText
    // invert, and Canvas uses the same rgb(18, 18, 18) the canvas-background
    // paint path already paints for a dark used color scheme.
    [Theory]
    [InlineData("Canvas", 18, 18, 18)]
    [InlineData("CanvasText", 255, 255, 255)]
    [InlineData("Field", 59, 59, 59)]
    [InlineData("FieldText", 255, 255, 255)]
    [InlineData("ButtonText", 255, 255, 255)]
    public void Resolves_Dark_Palette_When_Scheme_Is_Dark(string name, byte r, byte g, byte b)
    {
        Assert.True(CssSystemColors.TryResolve(name, CssColorScheme.Dark, out var color));
        Assert.Equal(new CssColor(r, g, b), color);
    }

    [Fact(Timeout = 600000)]
    public void Light_And_Dark_Disagree_On_Canvas()
    {
        Assert.True(CssSystemColors.TryResolve("Canvas", CssColorScheme.Light, out var light));
        Assert.True(CssSystemColors.TryResolve("Canvas", CssColorScheme.Dark, out var dark));
        Assert.NotEqual(light, dark);
    }

    // CSS Color 4 §6.2 defines the deprecated keywords as aliases of current
    // system colors rather than as palette entries of their own.
    [Theory]
    [InlineData("Window", "Canvas")]
    [InlineData("Background", "Canvas")]
    [InlineData("Menu", "Canvas")]
    [InlineData("Scrollbar", "Canvas")]
    [InlineData("WindowText", "CanvasText")]
    [InlineData("MenuText", "CanvasText")]
    [InlineData("InfoText", "CanvasText")]
    [InlineData("ThreeDFace", "ButtonFace")]
    [InlineData("ButtonHighlight", "ButtonFace")]
    [InlineData("WindowFrame", "ButtonBorder")]
    [InlineData("ActiveBorder", "ButtonBorder")]
    [InlineData("InactiveCaptionText", "GrayText")]
    public void Deprecated_Keywords_Alias_Current_System_Colors(string deprecated, string current)
    {
        Assert.True(CssSystemColors.TryResolve(deprecated, out var aliased));
        Assert.True(CssSystemColors.TryResolve(current, out var target));
        Assert.Equal(target, aliased);
    }

    [Theory]
    [InlineData("Window", "Canvas")]
    [InlineData("WindowText", "CanvasText")]
    public void Deprecated_Aliases_Follow_The_Scheme(string deprecated, string current)
    {
        Assert.True(CssSystemColors.TryResolve(deprecated, CssColorScheme.Dark, out var aliased));
        Assert.True(CssSystemColors.TryResolve(current, CssColorScheme.Dark, out var target));
        Assert.Equal(target, aliased);
    }

    // Non-system names must still report false so callers fall through to their
    // own named-color lookup unchanged.
    [Theory]
    [InlineData("rebeccapurple")]
    [InlineData("white")]
    [InlineData("transparent")]
    [InlineData("notacolor")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Returns_False_For_Non_System_Colors(string? name)
    {
        Assert.False(CssSystemColors.TryResolve(name!, out _));
        Assert.False(CssSystemColors.IsSystemColor(name!));
    }

    [Fact(Timeout = 600000)]
    public void IsSystemColor_Recognises_Current_And_Deprecated_Keywords()
    {
        Assert.True(CssSystemColors.IsSystemColor("Canvas"));
        Assert.True(CssSystemColors.IsSystemColor("AccentColor"));
        Assert.True(CssSystemColors.IsSystemColor("ThreeDShadow"));
    }
}
