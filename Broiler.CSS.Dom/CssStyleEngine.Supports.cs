using System;
using System.Collections.Generic;

namespace Broiler.CSS.Dom;

// @supports feature-query support oracle.
//
// An @supports feature query "( property: value )" is *true* only when a
// reference browser would understand the declaration. Broiler previously
// evaluated feature queries optimistically (any well-formed query was assumed
// supported), which made genuinely-unsupported queries — an unknown property
// ("unknown: green"), an invalid value ("color: rainbow"), or a
// <general-enclosed> block — wrongly apply their rules (WPT css-conditional
// css-supports-005/009/010/020/030, "valid syntax but a failing condition must
// not apply").
//
// The oracle models what the reference (Chromium) browser that generates the WPT
// references supports, NOT what Broiler's layout engine implements — the two agree
// on the negative cases these tests probe (neither supports "color: rainbow" or an
// "unknown" property) while staying optimistic for real properties Broiler may not
// yet fully render, so switching @supports to real evaluation matches the
// reference without regressing feature queries for shipped CSS features.
public sealed partial class CssStyleEngine
{
    /// <summary>
    /// Resolves an <c>@supports</c> feature query <c>(property: value)</c>: returns
    /// <c>true</c> only when the property is a recognised CSS property and the value
    /// is acceptable for it. Unknown properties, empty values, and values that fail
    /// property-specific validation (a bare word that is not a <c>&lt;color&gt;</c>,
    /// an invalid keyword, …) are unsupported.
    /// </summary>
    internal static bool IsFeatureQuerySupported(string property, string value)
    {
        property = property.Trim().ToLowerInvariant();
        // A feature query may carry !important; it does not affect whether the
        // underlying declaration is supported, so strip it before validating.
        value = StripImportantFlag(value).Trim();
        if (value.Length == 0)
            return false;

        // Custom properties (and the var() that references them) are always a
        // supported declaration per CSS Properties & Values.
        if (property.StartsWith("--", StringComparison.Ordinal))
            return true;
        if (value.Contains("var(", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IsKnownCssProperty(property))
            return false;

        // Reuse the cascade's value validator for the properties it models with a
        // closed value set (display, position, border-style, …). It intentionally
        // default-accepts values for properties it does not model, so it is a
        // necessary-but-not-sufficient gate.
        if (!IsAcceptableDeclarationValue(property, value))
            return false;

        // The cascade validator only rejects vendor-prefixed junk for <color>
        // properties, so "color: rainbow" slips through it. @supports needs a real
        // <color> check.
        if (IsColorProperty(property) && !IsColorValue(value))
            return false;

        return true;
    }

    private static string StripImportantFlag(string value)
    {
        var trimmed = value.TrimEnd();
        const string important = "!important";
        // Tolerate whitespace between '!' and 'important' is uncommon in feature
        // queries; match the canonical form (optionally spaced before '!').
        if (trimmed.EndsWith(important, StringComparison.OrdinalIgnoreCase))
            return trimmed[..^important.Length];
        return value;
    }

    private static bool IsColorProperty(string property) => property switch
    {
        "color" or "background-color" or "border-color"
            or "border-top-color" or "border-right-color"
            or "border-bottom-color" or "border-left-color"
            or "border-block-color" or "border-inline-color"
            or "border-block-start-color" or "border-block-end-color"
            or "border-inline-start-color" or "border-inline-end-color"
            or "outline-color" or "text-decoration-color"
            or "column-rule-color" or "caret-color"
            or "text-emphasis-color" or "-webkit-text-fill-color"
            or "-webkit-text-stroke-color" or "flood-color"
            or "lighting-color" or "stop-color" or "fill" or "stroke" => true,
        _ => false,
    };

    /// <summary>
    /// True when <paramref name="value"/> is a valid CSS <c>&lt;color&gt;</c>: a
    /// named color, a hex color, a color function (rgb/hsl/hwb/lab/lch/oklab/oklch/
    /// color/color-mix/light-dark/…), <c>currentcolor</c>/<c>transparent</c>, a
    /// system color, or a CSS-wide keyword. A bare identifier that is not one of
    /// these (e.g. <c>rainbow</c>) is not a color.
    /// </summary>
    private static bool IsColorValue(string value)
    {
        var v = value.Trim().ToLowerInvariant();
        if (v.Length == 0)
            return false;

        if (v is "inherit" or "initial" or "unset" or "revert" or "revert-layer")
            return true;
        if (v is "currentcolor" or "transparent")
            return true;
        if (v[0] == '#')
            return true;

        int paren = v.IndexOf('(');
        if (paren > 0)
        {
            var fn = v[..paren];
            return ColorFunctions.Contains(fn);
        }

        return NamedColors.Contains(v) || SystemColors.Contains(v);
    }

    private static bool IsKnownCssProperty(string property)
    {
        if (property.StartsWith("--", StringComparison.Ordinal))
            return true;
        // Optimistically accept vendor-prefixed properties: reference browsers ship
        // many, and Broiler's cascade already treats them leniently.
        if (property.StartsWith("-webkit-", StringComparison.Ordinal) ||
            property.StartsWith("-moz-", StringComparison.Ordinal) ||
            property.StartsWith("-ms-", StringComparison.Ordinal) ||
            property.StartsWith("-o-", StringComparison.Ordinal))
        {
            return true;
        }
        return KnownProperties.Contains(property);
    }

    private static readonly HashSet<string> ColorFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "rgb", "rgba", "hsl", "hsla", "hwb", "lab", "lch", "oklab", "oklch",
        "color", "color-mix", "light-dark", "device-cmyk",
    };

    // The CSS Color 4 named colors (the extended set, including "rebeccapurple")
    // plus the CSS-wide "transparent"/"currentcolor" handled above. Used to decide
    // whether an @supports (color: <ident>) query names a real color.
    private static readonly HashSet<string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige",
        "bisque", "black", "blanchedalmond", "blue", "blueviolet", "brown",
        "burlywood", "cadetblue", "chartreuse", "chocolate", "coral",
        "cornflowerblue", "cornsilk", "crimson", "cyan", "darkblue", "darkcyan",
        "darkgoldenrod", "darkgray", "darkgreen", "darkgrey", "darkkhaki",
        "darkmagenta", "darkolivegreen", "darkorange", "darkorchid", "darkred",
        "darksalmon", "darkseagreen", "darkslateblue", "darkslategray",
        "darkslategrey", "darkturquoise", "darkviolet", "deeppink", "deepskyblue",
        "dimgray", "dimgrey", "dodgerblue", "firebrick", "floralwhite",
        "forestgreen", "fuchsia", "gainsboro", "ghostwhite", "gold", "goldenrod",
        "gray", "green", "greenyellow", "grey", "honeydew", "hotpink", "indianred",
        "indigo", "ivory", "khaki", "lavender", "lavenderblush", "lawngreen",
        "lemonchiffon", "lightblue", "lightcoral", "lightcyan",
        "lightgoldenrodyellow", "lightgray", "lightgreen", "lightgrey",
        "lightpink", "lightsalmon", "lightseagreen", "lightskyblue",
        "lightslategray", "lightslategrey", "lightsteelblue", "lightyellow",
        "lime", "limegreen", "linen", "magenta", "maroon", "mediumaquamarine",
        "mediumblue", "mediumorchid", "mediumpurple", "mediumseagreen",
        "mediumslateblue", "mediumspringgreen", "mediumturquoise",
        "mediumvioletred", "midnightblue", "mintcream", "mistyrose", "moccasin",
        "navajowhite", "navy", "oldlace", "olive", "olivedrab", "orange",
        "orangered", "orchid", "palegoldenrod", "palegreen", "paleturquoise",
        "palevioletred", "papayawhip", "peachpuff", "peru", "pink", "plum",
        "powderblue", "purple", "rebeccapurple", "red", "rosybrown", "royalblue",
        "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell", "sienna",
        "silver", "skyblue", "slateblue", "slategray", "slategrey", "snow",
        "springgreen", "steelblue", "tan", "teal", "thistle", "tomato",
        "turquoise", "violet", "wheat", "white", "whitesmoke", "yellow",
        "yellowgreen",
    };

    // CSS Color 4 system colors (used as <color> keywords, e.g. Canvas, ButtonText).
    private static readonly HashSet<string> SystemColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "canvas", "canvastext", "linktext", "visitedtext", "activetext",
        "buttonface", "buttontext", "buttonborder", "field", "fieldtext",
        "highlight", "highlighttext", "selecteditem", "selecteditemtext",
        "mark", "marktext", "graytext", "accentcolor", "accentcolortext",
        // Deprecated but still recognised system-color keywords.
        "activeborder", "activecaption", "appworkspace", "background",
        "buttonhighlight", "buttonshadow", "captiontext", "inactiveborder",
        "inactivecaption", "inactivecaptiontext", "infobackground", "infotext",
        "menu", "menutext", "scrollbar", "threeddarkshadow", "threedface",
        "threedhighlight", "threedlightshadow", "threedshadow", "window",
        "windowframe", "windowtext",
    };

    // A recognised-CSS-property allow-list for @supports. It is deliberately broad —
    // it models the properties a reference browser recognises (so real feature
    // queries stay true), and exists only to reject genuinely-unknown property names
    // such as "unknown". Custom ("--*") and vendor-prefixed properties are accepted
    // by IsKnownCssProperty without appearing here.
    private static readonly HashSet<string> KnownProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        // Boxes / positioning
        "display", "position", "top", "right", "bottom", "left", "inset",
        "inset-block", "inset-inline", "inset-block-start", "inset-block-end",
        "inset-inline-start", "inset-inline-end", "float", "clear", "z-index",
        "visibility", "box-sizing", "aspect-ratio", "contain", "contain-intrinsic-size",
        "contain-intrinsic-width", "contain-intrinsic-height",
        "contain-intrinsic-block-size", "contain-intrinsic-inline-size",
        "content-visibility", "container", "container-type", "container-name",
        "isolation", "overflow", "overflow-x", "overflow-y", "overflow-block",
        "overflow-inline", "overflow-clip-margin", "overflow-anchor", "overscroll-behavior",
        "overscroll-behavior-x", "overscroll-behavior-y", "overscroll-behavior-block",
        "overscroll-behavior-inline", "resize",
        // Sizing
        "width", "height", "min-width", "min-height", "max-width", "max-height",
        "block-size", "inline-size", "min-block-size", "min-inline-size",
        "max-block-size", "max-inline-size",
        // Margins / padding / borders
        "margin", "margin-top", "margin-right", "margin-bottom", "margin-left",
        "margin-block", "margin-inline", "margin-block-start", "margin-block-end",
        "margin-inline-start", "margin-inline-end", "margin-trim",
        "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
        "padding-block", "padding-inline", "padding-block-start", "padding-block-end",
        "padding-inline-start", "padding-inline-end",
        "border", "border-width", "border-style", "border-color",
        "border-top", "border-right", "border-bottom", "border-left",
        "border-top-width", "border-right-width", "border-bottom-width", "border-left-width",
        "border-top-style", "border-right-style", "border-bottom-style", "border-left-style",
        "border-top-color", "border-right-color", "border-bottom-color", "border-left-color",
        "border-block", "border-inline", "border-block-width", "border-inline-width",
        "border-block-style", "border-inline-style", "border-block-color", "border-inline-color",
        "border-block-start", "border-block-end", "border-inline-start", "border-inline-end",
        "border-block-start-width", "border-block-end-width",
        "border-inline-start-width", "border-inline-end-width",
        "border-block-start-style", "border-block-end-style",
        "border-inline-start-style", "border-inline-end-style",
        "border-block-start-color", "border-block-end-color",
        "border-inline-start-color", "border-inline-end-color",
        "border-radius", "border-top-left-radius", "border-top-right-radius",
        "border-bottom-left-radius", "border-bottom-right-radius",
        "border-start-start-radius", "border-start-end-radius",
        "border-end-start-radius", "border-end-end-radius",
        "border-image", "border-image-source", "border-image-slice",
        "border-image-width", "border-image-outset", "border-image-repeat",
        "border-collapse", "border-spacing",
        "outline", "outline-width", "outline-style", "outline-color", "outline-offset",
        "box-shadow", "box-decoration-break",
        // Backgrounds
        "background", "background-color", "background-image", "background-repeat",
        "background-position", "background-position-x", "background-position-y",
        "background-size", "background-origin", "background-clip", "background-attachment",
        "background-blend-mode",
        // Color / visual
        "color", "color-scheme", "opacity", "mix-blend-mode", "accent-color",
        "caret-color", "print-color-adjust", "forced-color-adjust",
        // Fonts / text
        "font", "font-family", "font-size", "font-style", "font-weight",
        "font-variant", "font-variant-caps", "font-variant-numeric",
        "font-variant-ligatures", "font-variant-east-asian", "font-variant-alternates",
        "font-variant-position", "font-variant-emoji", "font-feature-settings",
        "font-variation-settings", "font-kerning", "font-stretch", "font-size-adjust",
        "font-synthesis", "font-optical-sizing", "font-language-override",
        "line-height", "letter-spacing", "word-spacing", "text-align", "text-align-last",
        "text-indent", "text-transform", "text-decoration", "text-decoration-line",
        "text-decoration-style", "text-decoration-color", "text-decoration-thickness",
        "text-decoration-skip-ink", "text-underline-offset", "text-underline-position",
        "text-emphasis", "text-emphasis-style", "text-emphasis-color", "text-emphasis-position",
        "text-shadow", "text-overflow", "text-justify", "text-orientation",
        "text-combine-upright", "text-rendering", "text-wrap", "text-wrap-mode",
        "text-wrap-style", "text-spacing-trim", "text-autospace",
        "white-space", "white-space-collapse", "word-break", "word-wrap",
        "overflow-wrap", "line-break", "hyphens", "hyphenate-character",
        "hyphenate-limit-chars", "tab-size", "direction", "unicode-bidi",
        "writing-mode", "vertical-align", "quotes", "hanging-punctuation",
        "ruby-align", "ruby-position", "ruby-merge", "initial-letter",
        // Lists / tables / content
        "list-style", "list-style-type", "list-style-position", "list-style-image",
        "counter-reset", "counter-increment", "counter-set", "content",
        "table-layout", "empty-cells", "caption-side",
        // Flexbox / grid / alignment
        "flex", "flex-grow", "flex-shrink", "flex-basis", "flex-direction",
        "flex-flow", "flex-wrap", "order",
        "justify-content", "justify-items", "justify-self",
        "align-content", "align-items", "align-self", "place-content",
        "place-items", "place-self", "gap", "row-gap", "column-gap",
        "grid", "grid-template", "grid-template-rows", "grid-template-columns",
        "grid-template-areas", "grid-auto-rows", "grid-auto-columns", "grid-auto-flow",
        "grid-row", "grid-column", "grid-area", "grid-row-start", "grid-row-end",
        "grid-column-start", "grid-column-end", "grid-gap", "grid-row-gap", "grid-column-gap",
        // Multicol
        "columns", "column-count", "column-width", "column-rule", "column-rule-width",
        "column-rule-style", "column-rule-color", "column-span", "column-fill",
        // Transforms / transitions / animations
        "transform", "transform-origin", "transform-box", "transform-style",
        "translate", "rotate", "scale", "perspective", "perspective-origin",
        "backface-visibility",
        "transition", "transition-property", "transition-duration",
        "transition-timing-function", "transition-delay", "transition-behavior",
        "animation", "animation-name", "animation-duration", "animation-timing-function",
        "animation-delay", "animation-iteration-count", "animation-direction",
        "animation-fill-mode", "animation-play-state", "animation-composition",
        "animation-timeline", "animation-range", "animation-range-start", "animation-range-end",
        "will-change", "offset", "offset-path", "offset-distance", "offset-rotate",
        "offset-anchor", "offset-position",
        // Scrolling
        "scroll-behavior", "scroll-margin", "scroll-margin-top", "scroll-margin-right",
        "scroll-margin-bottom", "scroll-margin-left", "scroll-margin-block",
        "scroll-margin-inline", "scroll-padding", "scroll-padding-top",
        "scroll-padding-right", "scroll-padding-bottom", "scroll-padding-left",
        "scroll-padding-block", "scroll-padding-inline", "scroll-snap-type",
        "scroll-snap-align", "scroll-snap-stop", "scroll-timeline",
        "scroll-timeline-name", "scroll-timeline-axis", "view-timeline",
        "view-timeline-name", "view-timeline-axis", "view-timeline-inset",
        // Effects / masking / clipping
        "clip", "clip-path", "clip-rule", "mask", "mask-image", "mask-mode",
        "mask-repeat", "mask-position", "mask-clip", "mask-origin", "mask-size",
        "mask-composite", "mask-type", "mask-border", "filter", "backdrop-filter",
        "shape-outside", "shape-margin", "shape-image-threshold", "shape-rendering",
        "mix-blend-mode",
        // Interaction / UI
        "cursor", "pointer-events", "touch-action", "user-select", "user-modify",
        "appearance", "caret", "field-sizing", "zoom",
        // Anchor positioning
        "anchor-name", "anchor-scope", "position-anchor", "position-area",
        "position-try", "position-try-fallbacks", "position-try-order",
        "position-visibility", "inset-area",
        // Page / break
        "page", "page-break-before", "page-break-after", "page-break-inside",
        "break-before", "break-after", "break-inside", "orphans", "widows",
        "box-orient",
        // SVG presentation
        "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width",
        "stroke-opacity", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit",
        "stroke-dasharray", "stroke-dashoffset", "stop-color", "stop-opacity",
        "flood-color", "flood-opacity", "lighting-color", "color-interpolation",
        "color-interpolation-filters", "dominant-baseline", "alignment-baseline",
        "baseline-shift", "marker", "marker-start", "marker-mid", "marker-end",
        "paint-order", "vector-effect", "d", "cx", "cy", "r", "rx", "ry", "x", "y",
        "image-rendering", "object-fit", "object-position",
    };
}
