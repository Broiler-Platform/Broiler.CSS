namespace Broiler.CSS;

/// <summary>
/// The used color scheme a system color is resolved against (CSS Color Adjust
/// Module Level 1). Defaults to <see cref="Light"/>: a document that says
/// nothing about <c>color-scheme</c> renders against the light palette, which is
/// what a reference browser does and therefore what the WPT references show.
/// </summary>
public enum CssColorScheme
{
    Light,
    Dark,
}

/// <summary>
/// Resolves the CSS Color Module Level 4 <em>system colors</em> (§6) — the
/// <c>&lt;color&gt;</c> keywords that name a part of the UA/platform palette,
/// e.g. <c>Canvas</c>, <c>CanvasText</c>, <c>ButtonFace</c>, <c>LinkText</c>.
/// <para>
/// DIAGNOSTIC NOTE (WPT issue #1491, problem 28): this table used to carry only
/// <c>Field</c> and <c>FieldText</c>. Every other system color fell through to
/// the named-color lookup, which does not know them, so each resolved to the
/// unknown-color fallback — black. <c>forced-colors-mode-20.html</c> paints
/// <c>body { background-color: Canvas }</c> and rendered a 98% black canvas
/// against Chromium's 98% white, and the whole system-color family failed the
/// same way. If a system color paints black again, this table is the place to
/// look.
/// </para>
/// <para>
/// The light values match what Chromium reports for a default document, because
/// the WPT references are Chromium screenshots — a divergence here is a
/// whole-canvas pixel mismatch. Forced-colors mode is a separate palette that
/// nothing currently emulates, so <c>forced-colors</c> keeps computing to
/// <c>none</c> and these values stand.
/// </para>
/// </summary>
public static class CssSystemColors
{
    /// <summary>
    /// Resolves <paramref name="colorName"/> against the light palette. This is
    /// the default for a document that does not use a dark <c>color-scheme</c>.
    /// </summary>
    public static bool TryResolve(string colorName, out CssColor color) =>
        TryResolve(colorName, CssColorScheme.Light, out color);

    /// <summary>
    /// Resolves <paramref name="colorName"/> (case-insensitive, surrounding
    /// whitespace ignored) against the palette for <paramref name="scheme"/>.
    /// Returns <see langword="false"/> when the name is not a system color, so
    /// callers fall through to their named-color lookup unchanged.
    /// </summary>
    public static bool TryResolve(string colorName, CssColorScheme scheme, out CssColor color)
    {
        if (string.IsNullOrWhiteSpace(colorName))
        {
            color = default;
            return false;
        }

        var name = colorName.Trim().ToLowerInvariant();
        var dark = scheme == CssColorScheme.Dark;

        switch (name)
        {
            // ── CSS Color 4 §6.1: the current system colors ──────────────────
            case "canvas":
                // rgb(18, 18, 18) is the same dark backdrop the canvas-background
                // paint path uses, so a dark document stays self-consistent.
                color = dark ? Rgb(18, 18, 18) : Rgb(255, 255, 255);
                return true;
            case "canvastext":
                color = dark ? Rgb(255, 255, 255) : Rgb(0, 0, 0);
                return true;
            case "linktext":
                color = dark ? Rgb(158, 158, 255) : Rgb(0, 0, 238);
                return true;
            case "visitedtext":
                color = dark ? Rgb(208, 173, 240) : Rgb(85, 26, 139);
                return true;
            case "activetext":
                color = dark ? Rgb(255, 158, 158) : Rgb(255, 0, 0);
                return true;
            case "buttonface":
                color = dark ? Rgb(111, 111, 111) : Rgb(239, 239, 239);
                return true;
            case "buttontext":
                color = dark ? Rgb(255, 255, 255) : Rgb(0, 0, 0);
                return true;
            case "buttonborder":
                color = dark ? Rgb(133, 133, 133) : Rgb(118, 118, 118);
                return true;
            case "field":
                color = dark ? Rgb(59, 59, 59) : Rgb(255, 255, 255);
                return true;
            case "fieldtext":
                color = dark ? Rgb(255, 255, 255) : Rgb(0, 0, 0);
                return true;
            case "highlight":
            case "selecteditem":
                color = dark ? Rgb(63, 81, 181) : Rgb(0, 117, 255);
                return true;
            case "highlighttext":
            case "selecteditemtext":
                color = Rgb(255, 255, 255);
                return true;
            case "mark":
                // The highlight-marker pair is scheme-independent in Chromium.
                color = Rgb(255, 255, 0);
                return true;
            case "marktext":
                color = Rgb(0, 0, 0);
                return true;
            case "graytext":
                color = dark ? Rgb(170, 170, 170) : Rgb(128, 128, 128);
                return true;
            case "accentcolor":
                color = dark ? Rgb(99, 154, 255) : Rgb(0, 117, 255);
                return true;
            case "accentcolortext":
                color = Rgb(255, 255, 255);
                return true;

            // ── CSS Color 4 §6.2: deprecated system colors ───────────────────
            // The spec keeps these for compatibility and defines each as an
            // alias of a current system color, so they are mapped rather than
            // given palette values of their own.
            case "activeborder":        // → ButtonBorder
            case "inactiveborder":      // → ButtonBorder
            case "threeddarkshadow":    // → ButtonBorder
            case "threedhighlight":     // → ButtonBorder
            case "threedlightshadow":   // → ButtonBorder
            case "threedshadow":        // → ButtonBorder
            case "windowframe":         // → ButtonBorder
                return TryResolve("buttonborder", scheme, out color);

            case "activecaption":       // → Canvas
            case "appworkspace":        // → Canvas
            case "background":          // → Canvas
            case "inactivecaption":     // → Canvas
            case "infobackground":      // → Canvas
            case "menu":                // → Canvas
            case "scrollbar":           // → Canvas
            case "window":              // → Canvas
                return TryResolve("canvas", scheme, out color);

            case "captiontext":         // → CanvasText
            case "infotext":            // → CanvasText
            case "menutext":            // → CanvasText
            case "windowtext":          // → CanvasText
                return TryResolve("canvastext", scheme, out color);

            case "buttonhighlight":     // → ButtonFace
            case "buttonshadow":        // → ButtonFace
            case "threedface":          // → ButtonFace
                return TryResolve("buttonface", scheme, out color);

            case "inactivecaptiontext": // → GrayText
                return TryResolve("graytext", scheme, out color);

            default:
                color = default;
                return false;
        }
    }

    /// <summary>
    /// Reports whether <paramref name="colorName"/> names a CSS system color,
    /// without resolving it — for callers that only need to classify a keyword.
    /// </summary>
    public static bool IsSystemColor(string colorName) =>
        TryResolve(colorName, CssColorScheme.Light, out _);

    private static CssColor Rgb(byte r, byte g, byte b) => new(r, g, b);
}
