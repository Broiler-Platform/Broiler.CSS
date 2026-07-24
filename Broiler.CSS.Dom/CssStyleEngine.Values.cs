using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Broiler.CSS.Dom;

// Pure, environment-light value transforms used by the cascade/computed-style
// engine: shorthand expansion, var()/custom-property resolution, relative
// font-weight resolution, media-query evaluation, and length parsing. These
// operate only on string dictionaries and the supplied environment, never on
// the DOM, so they are deterministic and unit-testable in isolation.
public sealed partial class CssStyleEngine
{
    private const int MaxCustomPropertyResolutionPasses = 4;

    // CSS Custom Properties: a var() reference is substituted with the
    // referenced property's value, which may itself contain further var()s.
    // Non-cyclic chains where each property references a lower one twice
    // (--p2: var(--p1) var(--p1); --p3: var(--p2) var(--p2); …) expand
    // exponentially — the "billion laughs" pattern — and would exhaust memory
    // (WPT css-variables/variable-exponential-blowup). Browsers cap the
    // substituted length; once a property's resolved value exceeds this bound
    // it computes to the guaranteed-invalid value instead.
    private const int MaxResolvedCustomPropertyValueLength = 100_000;

    // Sentinel for the CSS "guaranteed-invalid value" produced when var()
    // substitution overflows the length bound. Distinct from a legitimately
    // empty custom property (`--x: ;`), which keeps its empty value: a
    // referencing var() with a fallback uses the fallback when the referenced
    // property is guaranteed-invalid, but uses the empty value when it is
    // merely empty. The marker propagates up unchanged (it contains no
    // `var(`) and is scrubbed from any non-custom property before output.
    // Uses U+FFFF/U+FFFE noncharacters so it can never collide with real CSS.
    private const string CustomPropertyInvalidMarker = "￿￾css-guaranteed-invalid￾￿";

    // ---- CSS-wide keywords -------------------------------------------------

    private static void ResolveCssWideKeywordProperties(
        Dictionary<string, string> computed,
        IReadOnlyDictionary<string, string>? parentProps)
    {
        foreach (var key in computed.Keys.ToList())
        {
            if (key.StartsWith("--", StringComparison.Ordinal) ||
                !computed.TryGetValue(key, out var value) ||
                string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var lower = value.Trim().ToLowerInvariant();
            if (lower is not ("initial" or "inherit" or "unset" or "revert"))
                continue;

            // Preserve "inherit" verbatim so the computed snapshot mirrors the
            // bridge's getComputedStyle() behaviour rather than eagerly folding
            // it into the parent's value during keyword normalization.
            if (lower == "inherit")
                continue;

            string? replacement = lower switch
            {
                "unset" or "revert" => IsInheritedCssProperty(key)
                    ? parentProps != null && parentProps.TryGetValue(key, out var inherited)
                        ? inherited
                        : CssInitialValues.GetValueOrDefault(key)
                    : CssInitialValues.GetValueOrDefault(key),
                _ => CssInitialValues.GetValueOrDefault(key),
            };

            if (string.IsNullOrWhiteSpace(replacement))
                computed.Remove(key);
            else
                computed[key] = replacement;
        }
    }

    private static bool IsInheritedCssProperty(string property) =>
        CssInheritedProperties.Contains(property);

    // ---- var() resolution --------------------------------------------------

    private static void ResolveKnownCustomProperties(Dictionary<string, string> computed)
    {
        foreach (var key in computed.Keys.ToList())
        {
            if (key.StartsWith("--", StringComparison.Ordinal))
                continue;

            if (!computed.TryGetValue(key, out var value)
                || string.IsNullOrEmpty(value)
                || !ContainsSubstitutionFunction(value))
            {
                continue;
            }

            var resolved = ResolveKnownCustomProperties(value, computed);
            // A non-custom property whose value overflowed substitution is
            // guaranteed-invalid; drop it (it never reaches the renderer) so the
            // marker can't leak into output.
            computed[key] = resolved == CustomPropertyInvalidMarker ? string.Empty : resolved;
        }
    }

    private static string ResolveKnownCustomProperties(
        string value,
        Dictionary<string, string> computed,
        int depth = 0,
        HashSet<string>? visiting = null)
    {
        if (string.IsNullOrEmpty(value)
            || depth >= 8
            || !ContainsSubstitutionFunction(value))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        bool changed = false;
        int position = 0;

        while (position < value.Length)
        {
            int fnIndex = FindNextSubstitutionFunction(value, position, out bool isEnv);
            if (fnIndex < 0)
            {
                sb.Append(value, position, value.Length - position);
                break;
            }

            sb.Append(value, position, fnIndex - position);

            // Both "var(" and "env(" carry a 3-character name before the '('.
            int openParenIndex = fnIndex + 3;
            int closeParenIndex = FindMatchingClosingParen(value, openParenIndex);
            if (closeParenIndex < 0)
            {
                string inner = value[(openParenIndex + 1)..];
                string recovered = isEnv
                    ? ResolveEnvFunction(inner, computed, depth + 1, visiting)
                    : ResolveVarFunction(inner, computed, depth + 1, visiting);
                if (recovered == $"{(isEnv ? "env" : "var")}({inner})")
                {
                    sb.Append(value, fnIndex, value.Length - fnIndex);
                }
                else if (recovered == CustomPropertyInvalidMarker)
                {
                    return CustomPropertyInvalidMarker;
                }
                else
                {
                    sb.Append(recovered);
                    changed = true;
                }
                break;
            }

            string function = value.Substring(fnIndex, closeParenIndex - fnIndex + 1);
            string functionInner =
                value.Substring(openParenIndex + 1, closeParenIndex - openParenIndex - 1);
            string replacement = isEnv
                ? ResolveEnvFunction(functionInner, computed, depth + 1, visiting)
                : ResolveVarFunction(functionInner, computed, depth + 1, visiting);

            if (replacement == function)
            {
                sb.Append(function);
            }
            else if (replacement == CustomPropertyInvalidMarker)
            {
                // A nested substitution was guaranteed-invalid; the whole value
                // is too. Propagate the marker rather than embedding it.
                return CustomPropertyInvalidMarker;
            }
            else
            {
                sb.Append(replacement);
                changed = true;
            }

            // Guard against exponential blowup of non-cyclic var() chains: once
            // the accumulated substitution exceeds the bound the value is
            // guaranteed-invalid, which the referencing var() fallbacks and the
            // non-custom-property scrub then treat as such.
            if (sb.Length > MaxResolvedCustomPropertyValueLength)
                return CustomPropertyInvalidMarker;

            position = closeParenIndex + 1;
        }

        return changed ? sb.ToString() : value;
    }

    private static string ResolveVarFunction(
        string inner,
        Dictionary<string, string> computed,
        int depth,
        HashSet<string>? visiting = null)
    {
        string propertyName = inner.Trim();
        string fallback = string.Empty;
        bool hasFallback = false;

        int commaIndex = FindTopLevelChar(inner, ',');
        if (commaIndex >= 0)
        {
            propertyName = inner[..commaIndex].Trim();
            fallback = inner[(commaIndex + 1)..].Trim();
            hasFallback = true;
        }

        if (!propertyName.StartsWith("--", StringComparison.Ordinal))
            return $"var({inner})";

        if (computed.TryGetValue(propertyName, out var propertyValue))
        {
            // Cycle detection (CSS Custom Properties §3): if this custom property is
            // already being resolved further up the chain, the reference forms a
            // dependency cycle. Every property in the cycle is invalid at
            // computed-value time, so substitute the guaranteed-invalid value
            // (empty) instead of recursing — without this the cyclic value is
            // re-expanded each pass and grows exponentially until the process runs
            // out of memory (WPT css-variables/css-properties-values-api cycles).
            visiting ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!visiting.Add(propertyName))
                return string.Empty;

            var resolved = ResolveKnownCustomProperties(propertyValue, computed, depth, visiting);
            visiting.Remove(propertyName);

            // A custom property that resolved to the guaranteed-invalid value
            // (empty because it is part of a cycle, or the overflow marker
            // because its substitution blew up) makes the referencing var() fall
            // back to its provided default when one exists (CSS Custom Properties §3).
            bool invalid = string.IsNullOrEmpty(resolved) || resolved == CustomPropertyInvalidMarker;
            if (invalid && hasFallback)
                return ResolveKnownCustomProperties(fallback, computed, depth, visiting);

            return resolved;
        }

        if (hasFallback)
            return ResolveKnownCustomProperties(fallback, computed, depth, visiting);

        return $"var({inner})";
    }

    // ---- env() resolution --------------------------------------------------

    // True when the value contains a var() or env() substitution function whose
    // resolution has been deferred to computed-value time.
    private static bool ContainsSubstitutionFunction(string value) =>
        value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0
        || value.IndexOf("env(", StringComparison.OrdinalIgnoreCase) >= 0;

    // Index of the earliest var()/env() function at or after <paramref name="start"/>,
    // or -1 if neither occurs. <paramref name="isEnv"/> reports which one won.
    private static int FindNextSubstitutionFunction(string value, int start, out bool isEnv)
    {
        int varIndex = value.IndexOf("var(", start, StringComparison.OrdinalIgnoreCase);
        int envIndex = value.IndexOf("env(", start, StringComparison.OrdinalIgnoreCase);

        if (envIndex >= 0 && (varIndex < 0 || envIndex < varIndex))
        {
            isEnv = true;
            return envIndex;
        }

        isEnv = false;
        return varIndex;
    }

    // Resolves an env() reference (CSS Environment Variables §env). Broiler models
    // the UA-defined variables that have a fixed value in a headless desktop
    // context (the safe-area insets, which are 0px with no notch); any other name
    // is unknown. An unknown env() substitutes its comma-separated fallback when
    // one is present (which may itself contain var()/env()), and is otherwise
    // invalid at computed-value time — the guaranteed-invalid value, which resets
    // the referencing property to its initial value rather than reviving an
    // earlier cascaded declaration.
    private static string ResolveEnvFunction(
        string inner,
        Dictionary<string, string> computed,
        int depth,
        HashSet<string>? visiting = null)
    {
        string nameSpec = inner.Trim();
        string fallback = string.Empty;
        bool hasFallback = false;

        int commaIndex = FindTopLevelChar(inner, ',');
        if (commaIndex >= 0)
        {
            nameSpec = inner[..commaIndex].Trim();
            fallback = inner[(commaIndex + 1)..].Trim();
            hasFallback = true;
        }

        // A dimensional env() name may carry integer indices
        // (e.g. env(viewport-segment-width 0 0)); the leading identifier names it.
        string name = nameSpec;
        int firstWhitespace = nameSpec.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '\f' });
        if (firstWhitespace >= 0)
            name = nameSpec[..firstWhitespace];

        if (TryGetUaEnvironmentValue(name, out var uaValue))
            return uaValue;

        if (hasFallback)
            return ResolveKnownCustomProperties(fallback, computed, depth, visiting);

        return CustomPropertyInvalidMarker;
    }

    private static bool TryGetUaEnvironmentValue(string name, out string value)
    {
        switch (name.ToLowerInvariant())
        {
            case "safe-area-inset-top":
            case "safe-area-inset-right":
            case "safe-area-inset-bottom":
            case "safe-area-inset-left":
            case "safe-area-max-inset-top":
            case "safe-area-max-inset-right":
            case "safe-area-max-inset-bottom":
            case "safe-area-max-inset-left":
                value = "0px";
                return true;
            default:
                value = string.Empty;
                return false;
        }
    }

    private static int FindMatchingClosingParen(string value, int openParenIndex)
    {
        int depth = 0;
        for (int i = openParenIndex; i < value.Length; i++)
        {
            if (value[i] == '(')
                depth++;
            else if (value[i] == ')')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static int FindTopLevelChar(string value, char target)
    {
        int depth = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '(')
                depth++;
            else if (value[i] == ')')
                depth--;
            else if (value[i] == target && depth == 0)
                return i;
        }

        return -1;
    }

    // ---- Declaration value validation / error recovery --------------------

    /// <summary>
    /// CSS error recovery: returns <c>false</c> for values that are clearly
    /// invalid for the given property, so an invalid declaration is dropped and a
    /// previously cascaded valid value wins (CSS Syntax §4 / CSS 2.1 §4.1.8). Only
    /// properties with a closed set of keyword values are validated; everything
    /// else accepts any non-empty value. The supplied value must already have its
    /// <c>!important</c> flag stripped (the engine tracks importance separately).
    /// </summary>
    /// <remarks>
    /// Exposed to consumers (e.g. the HtmlBridge inline-style ingestion path)
    /// through <see cref="CssDeclarationValidator"/> so they no longer maintain a
    /// parallel, narrower copy of this closed-keyword table.
    /// </remarks>
    internal static bool IsAcceptableDeclarationValue(string property, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var v = value.Trim().ToLowerInvariant();

        // CSS-wide keywords are always valid.
        if (v is "inherit" or "initial" or "unset" or "revert")
            return true;

        // Custom-property references and env() are validated after substitution,
        // not during raw cascade, so keep them for the later resolution step.
        if (ContainsSubstitutionFunction(v))
            return true;

        // CSS Values 4 §10 (calc-type-checking): inside a min()/max()/clamp() used
        // in a <length-percentage> context every argument must itself resolve to a
        // <length-percentage>. A bare <number> — including a unitless 0, which is
        // *not* the dimensionless 0 length that is allowed outside a math function —
        // is a type mismatch, so the whole function (and its declaration) is
        // invalid and must be dropped, letting a previously-cascaded valid value
        // win (WPT css-values/max-unitless-zero-invalid: `height: min(0, 100%)`
        // yields to the earlier `height: min(100%)`). calc() is intentionally not
        // checked: a <number> is a valid operand there (e.g. `calc(100% / 3)`).
        if (IsLengthPercentageProperty(property) && ComparisonMathFunctionHasBareNumberArgument(v))
            return false;

        switch (property.ToLowerInvariant())
        {
            case "white-space":
                return IsWhiteSpaceValue(v);

            case "display":
                if (v is "block" or "inline" or "inline-block" or "none"
                    or "flex" or "inline-flex" or "grid" or "inline-grid"
                    or "table" or "inline-table" or "table-row" or "table-cell"
                    or "table-column" or "table-row-group" or "table-header-group"
                    or "table-footer-group" or "table-column-group"
                    or "table-caption" or "list-item" or "contents"
                    or "run-in" or "flow" or "flow-root"
                    // Internal ruby display types (CSS Display 3). The layout engine
                    // does not model ruby specially yet, but these are valid <display>
                    // keywords — accepting them is strictly better than dropping a
                    // valid declaration and falling back to a stale cascade value.
                    or "ruby" or "ruby-base" or "ruby-text"
                    or "ruby-base-container" or "ruby-text-container"
                    or "math")
                    return true;
                // The experimental CSS Grid Level 3 <display-inside> keyword
                // grid-lanes is intentionally NOT accepted: no stable browser
                // ships it unflagged, so treating display:grid-lanes (or the
                // two-value inline grid-lanes) as invalid — dropping the
                // declaration so the element keeps its default display — matches
                // reference browsers. Accepting it and mapping it to a grid
                // formatting context instead diverged from every reference on the
                // css-grid/grid-lanes WPT suite.
                // CSS Display 3 two-value syntax: <display-outside> <display-inside>
                // (e.g. "inline grid", "block flow-root"). Accept a valid pair in
                // either order so the layout engine can normalize it to a legacy
                // single keyword.
                return IsTwoValueDisplay(v);

            case "position":
                return v is "static" or "relative" or "absolute" or "fixed" or "sticky";

            case "float":
            case "css-float":
                return v is "none" or "left" or "right" or "inline-start" or "inline-end";

            case "clear":
                return v is "none" or "left" or "right" or "both" or "inline-start" or "inline-end";

            case "visibility":
                return v is "visible" or "hidden" or "collapse";

            case "overflow":
            case "overflow-x":
            case "overflow-y":
                // CSS Overflow Level 3: one or two keywords.
                foreach (var part in v.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part is not ("visible" or "hidden" or "scroll" or "auto" or "clip"))
                        return false;
                }
                return true;

            case "text-align":
                // CSS Text 4 §text-align: start | end | left | right | center |
                // justify | match-parent | justify-all.  'justify-all' is the
                // shorthand value that also justifies the last line (text-align-all
                // + text-align-last both justify).  The -webkit-{left,right,center}
                // legacy keywords are non-standard but widely supported and exercised
                // by WPT (css-align): they both align inline content AND provide
                // block-level alignment for in-flow block children (see CssBox
                // justify-self resolution).
                return v is "left" or "right" or "center" or "justify"
                    or "start" or "end" or "match-parent" or "justify-all"
                    or "-webkit-left" or "-webkit-right" or "-webkit-center";

            case "text-align-last":
                // CSS Text 4 §text-align-last: auto | start | end | left | right |
                // center | justify | match-parent.
                return v is "auto" or "start" or "end" or "left" or "right"
                    or "center" or "justify" or "match-parent";

            case "text-decoration-style":
                return v is "solid" or "double" or "dotted" or "dashed" or "wavy";

            case "text-transform":
                return IsTextTransformValue(v);

            case "vertical-align":
                return v is "baseline" or "sub" or "super" or "text-top"
                    or "text-bottom" or "middle" or "top" or "bottom"
                    || IsLengthOrPercentage(v);

            case "box-sizing":
                return v is "content-box" or "border-box";

            case "cursor":
                return v is "auto" or "default" or "none" or "context-menu"
                    or "help" or "pointer" or "progress" or "wait"
                    or "cell" or "crosshair" or "text" or "vertical-text"
                    or "alias" or "copy" or "move" or "no-drop"
                    or "not-allowed" or "grab" or "grabbing"
                    or "e-resize" or "n-resize" or "ne-resize" or "nw-resize"
                    or "s-resize" or "se-resize" or "sw-resize" or "w-resize"
                    or "ew-resize" or "ns-resize" or "nesw-resize" or "nwse-resize"
                    or "col-resize" or "row-resize" or "all-scroll" or "zoom-in" or "zoom-out"
                    || v.StartsWith("url(", StringComparison.Ordinal);

            case "list-style-type":
                return v is "disc" or "circle" or "square" or "decimal"
                    or "decimal-leading-zero" or "lower-roman" or "upper-roman"
                    or "lower-greek" or "lower-latin" or "upper-latin"
                    or "armenian" or "georgian" or "lower-alpha" or "upper-alpha"
                    or "none";

            case "border-style":
                return IsBorderStyleList(v);

            case "border-top-style":
            case "border-right-style":
            case "border-bottom-style":
            case "border-left-style":
            case "outline-style":
                return IsBorderStyleKeyword(v);

            case "font-style":
                return v is "normal" or "italic" or "oblique";

            case "font-weight":
                return v is "normal" or "bold" or "bolder" or "lighter"
                    || (int.TryParse(v, out var w) && w is >= 1 and <= 1000);

            case "color":
            case "background-color":
            case "border-color":
            case "border-top-color":
            case "border-right-color":
            case "border-bottom-color":
            case "border-left-color":
            case "outline-color":
                // Reject unknown vendor-prefixed values (e.g. -acid3-bogus) while
                // accepting named colors, #hex, rgb()/hsl(), transparent, etc.
                return !v.StartsWith('-')
                    || v.StartsWith("-webkit-", StringComparison.Ordinal)
                    || v.StartsWith("-moz-", StringComparison.Ordinal)
                    || v.StartsWith("-ms-", StringComparison.Ordinal)
                    || v.StartsWith("-o-", StringComparison.Ordinal);

            default:
                return true;
        }
    }

    /// <summary>
    /// CSS Display 3 §2.1: validate the two-value <c>display</c> syntax
    /// (<c>&lt;display-outside&gt; &amp;&amp; &lt;display-inside&gt;</c>), e.g.
    /// <c>inline grid</c> or <c>block flow-root</c>. The two keywords may appear
    /// in either order. The experimental <c>grid-lanes</c> &lt;display-inside&gt;
    /// is deliberately excluded so <c>inline grid-lanes</c> is rejected as invalid
    /// (matching reference browsers, which do not ship grid-lanes unflagged).
    /// </summary>
    private static bool IsTwoValueDisplay(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        static bool IsOutside(string k) => k is "block" or "inline" or "run-in";
        static bool IsInside(string k) => k is "flow" or "flow-root" or "table"
            or "flex" or "grid" or "ruby";

        return (IsOutside(parts[0]) && IsInside(parts[1]))
            || (IsInside(parts[0]) && IsOutside(parts[1]));
    }

    // CSS Text 4 §3: white-space is a shorthand for white-space-collapse and
    // text-wrap-mode. Its value is either a legacy single keyword
    // (normal | pre | nowrap | pre-wrap | pre-line) or the two-longhand form
    // <'white-space-collapse'> || <'text-wrap-mode'> — at most one collapse
    // keyword combined with at most one wrap keyword, in either order. Accepting
    // the full grammar keeps values like "preserve-breaks" (== pre-line) and
    // "break-spaces nowrap" from being dropped as invalid.
    private static bool IsWhiteSpaceValue(string value)
    {
        if (value is "normal" or "nowrap" or "pre" or "pre-wrap" or "pre-line")
            return true;

        bool collapseSeen = false, wrapSeen = false;
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token)
            {
                case "collapse":
                case "preserve":
                case "preserve-breaks":
                case "preserve-spaces":
                case "break-spaces":
                    if (collapseSeen) return false;
                    collapseSeen = true;
                    break;
                case "wrap":
                case "nowrap":
                    if (wrapSeen) return false;
                    wrapSeen = true;
                    break;
                default:
                    return false;
            }
        }

        return collapseSeen || wrapSeen;
    }

    // CSS Text 3 §2.1: none | [ [capitalize | uppercase | lowercase] || full-width
    // || full-size-kana ] | math-auto. The case keywords are mutually exclusive; a
    // valid multi-token value combines at most one case keyword with full-width
    // and/or full-size-kana (in any order), each at most once. Accepting the full
    // grammar keeps combinations like "capitalize full-width" and the standalone
    // "full-size-kana"/"math-auto" from being dropped as invalid.
    private static bool IsTextTransformValue(string value)
    {
        if (value is "none" or "math-auto")
            return true;

        bool caseSeen = false, fullWidth = false, fullSizeKana = false;
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token)
            {
                case "capitalize":
                case "uppercase":
                case "lowercase":
                    if (caseSeen) return false;
                    caseSeen = true;
                    break;
                case "full-width":
                    if (fullWidth) return false;
                    fullWidth = true;
                    break;
                case "full-size-kana":
                    if (fullSizeKana) return false;
                    fullSizeKana = true;
                    break;
                default:
                    return false;
            }
        }

        return caseSeen || fullWidth || fullSizeKana;
    }

    private static bool IsBorderStyleList(string value)
    {
        var parts = SplitCssValues(value);
        return parts.Length is >= 1 and <= 4 && parts.All(IsBorderStyleKeyword);
    }

    private static bool IsBorderStyleKeyword(string value) =>
        value is "none" or "hidden" or "dotted" or "dashed"
            or "solid" or "double" or "groove" or "ridge"
            or "inset" or "outset";

    // ---- Shorthand expansion ----------------------------------------------

    /// <summary>
    /// Public entry point to the CSS shorthand-expansion pass. Given a property map, fills in
    /// longhand properties for any shorthands present (font, margin/padding/border families, the
    /// CSS-logical box shorthands, inset, background, and outline). Additive and idempotent: it
    /// never removes the shorthand key and never overwrites a longhand that is already present.
    /// This is the single canonical implementation — <c>Broiler.HtmlBridge</c> routes its
    /// computed/declared style maps through it instead of a bridge-private copy that could drift
    /// (HtmlBridge DOM/CSS promotion roadmap, Phase 2).
    /// </summary>
    public static void ExpandShorthands(Dictionary<string, string> declarations) =>
        ExpandCssShorthands(declarations);

    private static void ExpandCssShorthands(Dictionary<string, string> computed)
    {
        if (computed.TryGetValue("font", out var fontVal))
            ExpandFontShorthand(computed, fontVal);

        if (computed.TryGetValue("margin", out var marginVal))
            ExpandBoxShorthand(computed, marginVal, "margin-top", "margin-right", "margin-bottom", "margin-left");

        if (computed.TryGetValue("padding", out var paddingVal))
            ExpandBoxShorthand(computed, paddingVal, "padding-top", "padding-right", "padding-bottom", "padding-left");

        if (computed.TryGetValue("border-width", out var bwVal))
            ExpandBoxShorthand(computed, bwVal, "border-top-width", "border-right-width", "border-bottom-width", "border-left-width");

        if (computed.TryGetValue("border-style", out var bsVal))
            ExpandBoxShorthand(computed, bsVal, "border-top-style", "border-right-style", "border-bottom-style", "border-left-style");

        if (computed.TryGetValue("border-color", out var bcVal))
            ExpandBoxShorthand(computed, bcVal, "border-top-color", "border-right-color", "border-bottom-color", "border-left-color");

        if (computed.TryGetValue("border", out var borderVal))
            ExpandBorderShorthand(computed, borderVal);

        if (computed.TryGetValue("outline", out var outlineVal))
            ExpandOutlineShorthand(computed, outlineVal);

        if (computed.TryGetValue("border-left", out var borderLeftVal))
            ExpandBorderSideShorthand(computed, borderLeftVal, "left");
        if (computed.TryGetValue("border-top", out var borderTopVal))
            ExpandBorderSideShorthand(computed, borderTopVal, "top");
        if (computed.TryGetValue("border-right", out var borderRightVal))
            ExpandBorderSideShorthand(computed, borderRightVal, "right");
        if (computed.TryGetValue("border-bottom", out var borderBottomVal))
            ExpandBorderSideShorthand(computed, borderBottomVal, "bottom");

        if (computed.TryGetValue("border-inline", out var biVal))
        {
            if (!computed.ContainsKey("border-left")) computed["border-left"] = biVal;
            if (!computed.ContainsKey("border-right")) computed["border-right"] = biVal;
            ExpandBorderSideShorthand(computed, biVal, "left");
            ExpandBorderSideShorthand(computed, biVal, "right");
        }

        if (computed.TryGetValue("border-block", out var bbVal))
        {
            if (!computed.ContainsKey("border-top")) computed["border-top"] = bbVal;
            if (!computed.ContainsKey("border-bottom")) computed["border-bottom"] = bbVal;
            ExpandBorderSideShorthand(computed, bbVal, "top");
            ExpandBorderSideShorthand(computed, bbVal, "bottom");
        }

        if (computed.TryGetValue("margin-block", out var mbVal))
        {
            var parts = SplitCssValues(mbVal);
            if (parts.Length >= 1)
            {
                if (!computed.ContainsKey("margin-top")) computed["margin-top"] = parts[0];
                if (!computed.ContainsKey("margin-bottom")) computed["margin-bottom"] = parts.Length > 1 ? parts[1] : parts[0];
            }
        }

        if (computed.TryGetValue("margin-inline", out var miVal))
        {
            var parts = SplitCssValues(miVal);
            if (parts.Length >= 1)
            {
                if (!computed.ContainsKey("margin-left")) computed["margin-left"] = parts[0];
                if (!computed.ContainsKey("margin-right")) computed["margin-right"] = parts.Length > 1 ? parts[1] : parts[0];
            }
        }

        if (computed.TryGetValue("padding-block", out var pbVal))
        {
            var parts = SplitCssValues(pbVal);
            if (parts.Length >= 1)
            {
                if (!computed.ContainsKey("padding-top")) computed["padding-top"] = parts[0];
                if (!computed.ContainsKey("padding-bottom")) computed["padding-bottom"] = parts.Length > 1 ? parts[1] : parts[0];
            }
        }

        if (computed.TryGetValue("padding-inline", out var piVal))
        {
            var parts = SplitCssValues(piVal);
            if (parts.Length >= 1)
            {
                if (!computed.ContainsKey("padding-left")) computed["padding-left"] = parts[0];
                if (!computed.ContainsKey("padding-right")) computed["padding-right"] = parts.Length > 1 ? parts[1] : parts[0];
            }
        }

        if (computed.TryGetValue("inset", out var insetVal))
        {
            var insetParts = SplitCssValues(insetVal);
            if (insetParts.Length > 0)
            {
                string iTop = insetParts[0];
                string iRight = insetParts.Length > 1 ? insetParts[1] : iTop;
                string iBottom = insetParts.Length > 2 ? insetParts[2] : iTop;
                string iLeft = insetParts.Length > 3 ? insetParts[3] : iRight;

                if (!computed.ContainsKey("top")) computed["top"] = iTop;
                if (!computed.ContainsKey("right")) computed["right"] = iRight;
                if (!computed.ContainsKey("bottom")) computed["bottom"] = iBottom;
                if (!computed.ContainsKey("left")) computed["left"] = iLeft;
            }
        }

        if (computed.TryGetValue("background", out var bgVal))
            ExpandBackgroundShorthand(computed, bgVal);
    }

    private static void ExpandFontShorthand(Dictionary<string, string> computed, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (value.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase))
        {
            if (!computed.ContainsKey("font-style")) computed["font-style"] = "inherit";
            if (!computed.ContainsKey("font-variant")) computed["font-variant"] = "inherit";
            if (!computed.ContainsKey("font-weight")) computed["font-weight"] = "inherit";
            if (!computed.ContainsKey("font-size")) computed["font-size"] = "inherit";
            if (!computed.ContainsKey("line-height")) computed["line-height"] = "inherit";
            if (!computed.ContainsKey("font-family")) computed["font-family"] = "inherit";
            return;
        }

        var tokens = SplitCssValues(value);
        if (tokens.Length == 0)
            return;

        // The `<size> [ / <line-height> ]?` component may carry white space around
        // the slash (`50px / 1`, `50px /1`, `50px/ 1`). SplitCssValues then yields
        // the slash as its own token (or glued to only one side), which the size
        // classifier below cannot recognise — it would treat `50px` as a size with
        // no line-height and fold `/ 1 <family>` into the font-family, dropping the
        // real family. Glue the slash back onto the size token so the canonical
        // `size/line-height` form reaches the classifier regardless of spacing. A
        // bare `/` token is unambiguous here: an unquoted family cannot contain one,
        // and a quoted family is a single token from SplitCssValues.
        tokens = NormalizeFontSlashTokens(tokens);

        string fontStyle = "normal";
        string fontVariant = "normal";
        string fontWeight = "normal";
        string? fontSize = null;
        string? lineHeight = null;
        int fontSizeIndex = -1;

        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var lower = token.ToLowerInvariant();

            // CSS Fonts `font` shorthand grammar:
            //   [ <style> || <variant> || <weight> ]? <size> [ / <line-height> ]? <family>
            // Any style/variant/weight keywords always PRECEDE the font-size, so
            // classify them FIRST. Otherwise a bare numeric weight (100..900) is
            // mistaken for a unitless font-size length — the value that gates
            // WPT floats-143 (`font:900 2em/1`), background-root-006
            // (`font:900 1.75em`) and c527-font-10 (`font:italic small-caps 100
            // 150%/300%`): the weight was consumed as `font-size:900`/`100`,
            // producing gigantic text. A unitless number before the size can
            // only be a weight.
            if (lower is "normal" or "italic" or "oblique")
            {
                fontStyle = lower;
                continue;
            }
            if (lower == "small-caps")
            {
                fontVariant = lower;
                continue;
            }
            if (lower is "bold" or "bolder" or "lighter" or "100" or "200" or "300" or "400" or "500" or "600" or "700" or "800" or "900")
            {
                fontWeight = lower;
                continue;
            }

            if (TryParseFontSizeAndLineHeight(lower, token, out var parsedFontSize, out var parsedLineHeight))
            {
                fontSize = parsedFontSize;
                lineHeight = parsedLineHeight;
                fontSizeIndex = i;
                break;
            }
        }

        if (fontSizeIndex < 0 || fontSizeIndex >= tokens.Length - 1 || string.IsNullOrWhiteSpace(fontSize))
            return;

        var fontFamily = string.Join(" ", tokens[(fontSizeIndex + 1)..]).Trim();
        if (string.IsNullOrWhiteSpace(fontFamily))
            return;

        bool hasNonEmptyFamily = fontFamily
            .Split(',', StringSplitOptions.TrimEntries)
            .Any(part => !string.IsNullOrWhiteSpace(part.Trim('"', '\'', ' ')));
        if (!hasNonEmptyFamily)
            return;

        if (!computed.ContainsKey("font-style")) computed["font-style"] = fontStyle;
        if (!computed.ContainsKey("font-variant")) computed["font-variant"] = fontVariant;
        if (!computed.ContainsKey("font-weight")) computed["font-weight"] = fontWeight;
        if (!computed.ContainsKey("font-size")) computed["font-size"] = fontSize;
        var resolvedLineHeight = !string.IsNullOrWhiteSpace(lineHeight) ? lineHeight : "normal";
        if (!computed.ContainsKey("line-height")) computed["line-height"] = resolvedLineHeight;
        if (!computed.ContainsKey("font-family")) computed["font-family"] = fontFamily;
    }

    /// <summary>
    /// Collapses white space around the <c>size / line-height</c> slash in a
    /// tokenized <c>font</c> shorthand so the size/line-height pair is a single
    /// token. Handles the slash split off as its own token (<c>50px / 1</c>),
    /// glued to the size (<c>50px/ 1</c>), or glued to the line-height
    /// (<c>50px /1</c>). Tokens that already contain the slash (<c>50px/1</c>)
    /// pass through unchanged.
    /// </summary>
    private static string[] NormalizeFontSlashTokens(string[] tokens)
    {
        if (Array.TrueForAll(tokens, t => t.IndexOf('/', StringComparison.Ordinal) < 0))
            return tokens;

        var result = new List<string>(tokens.Length);
        for (int i = 0; i < tokens.Length; i++)
        {
            string t = tokens[i];

            if (t == "/")
            {
                // `size / line-height` — glue onto the preceding size token and
                // absorb the following line-height token.
                if (result.Count > 0)
                {
                    string next = i + 1 < tokens.Length ? tokens[i + 1] : string.Empty;
                    result[^1] = result[^1] + "/" + next;
                    if (i + 1 < tokens.Length) i++;
                    continue;
                }
            }
            else if (t.StartsWith('/') && result.Count > 0)
            {
                // `size /line-height` — the slash+line-height glued together.
                result[^1] = result[^1] + t;
                continue;
            }
            else if (t.EndsWith('/') && i + 1 < tokens.Length)
            {
                // `size/ line-height` — the size+slash glued together.
                result.Add(t + tokens[i + 1]);
                i++;
                continue;
            }

            result.Add(t);
        }

        return result.ToArray();
    }

    private static bool TryParseFontSizeAndLineHeight(string lowerToken, string originalToken, out string fontSize, out string lineHeight)
    {
        fontSize = string.Empty;
        lineHeight = string.Empty;

        string sizeToken = lowerToken;
        string? lineHeightToken = null;
        int slashIndex = lowerToken.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex >= 0)
        {
            sizeToken = lowerToken[..slashIndex];
            lineHeightToken = originalToken[(slashIndex + 1)..];
        }

        if (!IsFontSizeToken(sizeToken))
            return false;

        if (lineHeightToken != null)
        {
            var trimmedLineHeight = lineHeightToken.Trim();
            if (!IsFontLineHeightToken(trimmedLineHeight))
                return false;
            lineHeight = trimmedLineHeight;
        }

        fontSize = originalToken;
        if (slashIndex >= 0)
            fontSize = originalToken[..slashIndex];

        return true;
    }

    private static bool IsFontSizeToken(string token) =>
        token is "xx-small" or "x-small" or "small" or "medium" or "large" or "x-large" or "xx-large" or "larger" or "smaller"
        || IsLengthOrPercentage(token);

    private static bool IsFontLineHeightToken(string token) =>
        token.Equals("normal", StringComparison.OrdinalIgnoreCase)
        || IsLengthOrPercentage(token)
        || double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static void ExpandBoxShorthand(Dictionary<string, string> computed, string value,
        string topProp, string rightProp, string bottomProp, string leftProp)
    {
        var parts = SplitCssValues(value);
        if (parts.Length == 0) return;

        string top, right, bottom, left;
        switch (parts.Length)
        {
            case 1:
                top = right = bottom = left = parts[0];
                break;
            case 2:
                top = bottom = parts[0];
                right = left = parts[1];
                break;
            case 3:
                top = parts[0];
                right = left = parts[1];
                bottom = parts[2];
                break;
            default:
                top = parts[0];
                right = parts[1];
                bottom = parts[2];
                left = parts[3];
                break;
        }

        if (!computed.ContainsKey(topProp)) computed[topProp] = top;
        if (!computed.ContainsKey(rightProp)) computed[rightProp] = right;
        if (!computed.ContainsKey(bottomProp)) computed[bottomProp] = bottom;
        if (!computed.ContainsKey(leftProp)) computed[leftProp] = left;
    }

    private static void ExpandBorderShorthand(Dictionary<string, string> computed, string value)
    {
        var parts = SplitCssValues(value);

        string? width = null, style = null, color = null;

        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower is "none" or "hidden" or "dotted" or "dashed" or "solid"
                or "double" or "groove" or "ridge" or "inset" or "outset")
            {
                style ??= part;
            }
            else if (lower is "thin" or "medium" or "thick" || IsLengthOrPercentage(lower))
            {
                width ??= part;
            }
            else
            {
                color ??= part;
            }
        }

        if (width != null && !computed.ContainsKey("border-width")) computed["border-width"] = width;
        if (style != null && !computed.ContainsKey("border-style")) computed["border-style"] = style;
        if (color != null && !computed.ContainsKey("border-color")) computed["border-color"] = color;

        if (width != null)
            ExpandBoxShorthand(computed, width, "border-top-width", "border-right-width", "border-bottom-width", "border-left-width");
        if (style != null)
            ExpandBoxShorthand(computed, style, "border-top-style", "border-right-style", "border-bottom-style", "border-left-style");
        if (color != null)
            ExpandBoxShorthand(computed, color, "border-top-color", "border-right-color", "border-bottom-color", "border-left-color");
    }

    /// <summary>
    /// CSS UI §2: expands the <c>outline</c> shorthand
    /// (<c>&lt;outline-width&gt; || &lt;outline-style&gt; || &lt;outline-color&gt;</c>)
    /// into its longhands. The <c>auto</c> style keyword (focus-ring) is accepted.
    /// </summary>
    private static void ExpandOutlineShorthand(Dictionary<string, string> computed, string value)
    {
        var parts = SplitCssValues(value);
        string? width = null, style = null, color = null;

        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower is "none" or "hidden" or "dotted" or "dashed" or "solid"
                or "double" or "groove" or "ridge" or "inset" or "outset" or "auto")
                style ??= part;
            else if (lower is "thin" or "medium" or "thick" || IsLengthOrPercentage(lower))
                width ??= part;
            else
                color ??= part;
        }

        if (width != null && !computed.ContainsKey("outline-width")) computed["outline-width"] = width;
        if (style != null && !computed.ContainsKey("outline-style")) computed["outline-style"] = style;
        if (color != null && !computed.ContainsKey("outline-color")) computed["outline-color"] = color;
    }

    private static void ExpandBorderSideShorthand(Dictionary<string, string> computed, string value, string side)
    {
        var parts = SplitCssValues(value);
        string? width = null, style = null, color = null;
        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower is "none" or "hidden" or "dotted" or "dashed" or "solid"
                or "double" or "groove" or "ridge" or "inset" or "outset")
                style ??= part;
            else if (lower is "thin" or "medium" or "thick" || IsLengthOrPercentage(lower))
                width ??= part;
            else
                color ??= part;
        }

        if (width != null && !computed.ContainsKey($"border-{side}-width"))
            computed[$"border-{side}-width"] = width;
        if (style != null && !computed.ContainsKey($"border-{side}-style"))
            computed[$"border-{side}-style"] = style;
        if (color != null && !computed.ContainsKey($"border-{side}-color"))
            computed[$"border-{side}-color"] = color;
    }

    // CSS Backgrounds 3 §3.10: the `background` shorthand accepts multiple
    // comma-separated layers. Each layer carries its own image/position/size/
    // repeat/attachment/origin/clip; only the final layer may carry the
    // (single, non-layered) background-color. The renderer's paint walker reads
    // these longhands back as top-level comma-separated lists (one value per
    // layer), so the expansion MUST preserve every layer and emit clean
    // comma-joined values — dropping layers or leaving a trailing comma
    // corrupts background-image and silently discards layers downstream.
    private static void ExpandBackgroundShorthand(Dictionary<string, string> computed, string value)
    {
        var layers = SplitOnTopLevelCommas(value);
        if (layers.Count == 0)
            return;

        var images = new List<string>();
        var repeats = new List<string>();
        var attachments = new List<string>();
        var positions = new List<string>();
        var sizes = new List<string>();
        var origins = new List<string>();
        var clips = new List<string>();
        string? color = null;

        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            bool isFinalLayer = layerIndex == layers.Count - 1;

            string? image = null;
            string? repeat = null;
            string? attachment = null;
            string? origin = null;
            string? clip = null;
            var positionParts = new List<string>();
            var sizeParts = new List<string>();
            bool inSizeSection = false;

            foreach (var token in SplitCssValues(layers[layerIndex]))
            {
                var lower = token.ToLowerInvariant();

                // '/' switches the remainder of the layer to size values.
                if (lower == "/")
                {
                    inSizeSection = true;
                    continue;
                }

                if (inSizeSection)
                {
                    if (lower is "auto" or "cover" or "contain" || IsLengthOrPercentage(lower))
                    {
                        sizeParts.Add(lower);
                        continue;
                    }
                    inSizeSection = false;
                }

                if (lower.StartsWith("url(") || IsGradientFunction(lower))
                {
                    image ??= token;
                    continue;
                }

                if (lower == "none")
                {
                    image ??= "none";
                    continue;
                }

                if (lower is "scroll" or "fixed" or "local")
                {
                    attachment ??= lower;
                    continue;
                }

                // First <box> value is background-origin, second is background-clip.
                if (lower is "content-box" or "padding-box" or "border-box" or "border-area")
                {
                    if (origin == null)
                        origin = lower;
                    else
                        clip ??= lower;
                    continue;
                }

                if (lower is "repeat" or "repeat-x" or "repeat-y" or "no-repeat" or "space" or "round")
                {
                    repeat ??= lower;
                    continue;
                }

                if (lower is "left" or "right" or "top" or "bottom" or "center")
                {
                    positionParts.Add(lower);
                    continue;
                }

                if (IsLengthOrPercentage(lower))
                {
                    positionParts.Add(token);
                    continue;
                }

                if (lower is "inherit" or "auto")
                    continue;

                // background-color is single-valued and only legal in the final layer.
                if (isFinalLayer)
                    color ??= token;
            }

            images.Add(image ?? "none");
            repeats.Add(repeat ?? "repeat");
            attachments.Add(attachment ?? "scroll");
            positions.Add(positionParts.Count > 0 ? string.Join(" ", positionParts) : "0% 0%");
            sizes.Add(sizeParts.Count > 0 ? string.Join(" ", sizeParts) : "auto");
            origins.Add(origin ?? "padding-box");
            // CSS Backgrounds 3 §3.10: a single <box> value sets both origin and clip.
            clips.Add(clip ?? origin ?? "border-box");
        }

        if (!computed.ContainsKey("background-color"))
            computed["background-color"] = color ?? "transparent";
        if (!computed.ContainsKey("background-image"))
            computed["background-image"] = string.Join(", ", images);
        if (!computed.ContainsKey("background-repeat"))
            computed["background-repeat"] = string.Join(", ", repeats);
        if (!computed.ContainsKey("background-attachment"))
            computed["background-attachment"] = string.Join(", ", attachments);
        if (!computed.ContainsKey("background-position"))
            computed["background-position"] = string.Join(", ", positions);
        if (!computed.ContainsKey("background-size"))
            computed["background-size"] = string.Join(", ", sizes);
        if (!computed.ContainsKey("background-origin"))
            computed["background-origin"] = string.Join(", ", origins);
        if (!computed.ContainsKey("background-clip"))
            computed["background-clip"] = string.Join(", ", clips);
    }

    private static bool IsGradientFunction(string lowerToken) =>
        lowerToken.StartsWith("linear-gradient(") ||
        lowerToken.StartsWith("radial-gradient(") ||
        lowerToken.StartsWith("conic-gradient(") ||
        lowerToken.StartsWith("repeating-linear-gradient(") ||
        lowerToken.StartsWith("repeating-radial-gradient(") ||
        lowerToken.StartsWith("repeating-conic-gradient(");

    /// <summary>
    /// Splits a CSS value on top-level commas (those outside any parenthesised
    /// group), preserving commas nested inside functions such as
    /// <c>rgba(…)</c> or <c>linear-gradient(…)</c>. Empty segments are dropped so
    /// a stray trailing comma never yields a phantom layer.
    /// </summary>
    private static List<string> SplitOnTopLevelCommas(string value)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
            return parts;

        var sb = new StringBuilder(value.Length);
        int depth = 0;
        foreach (char c in value)
        {
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;

            if (c == ',' && depth == 0)
            {
                var segment = sb.ToString().Trim();
                if (segment.Length > 0)
                    parts.Add(segment);
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        var last = sb.ToString().Trim();
        if (last.Length > 0)
            parts.Add(last);

        return parts;
    }

    private static string[] SplitCssValues(string value)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        int depth = 0;
        foreach (char c in value)
        {
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;

            if (char.IsWhiteSpace(c) && depth == 0 && sb.Length > 0)
            {
                parts.Add(sb.ToString());
                sb.Clear();
            }
            else if (!char.IsWhiteSpace(c) || depth > 0)
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        return [.. parts];
    }

    // ---- Relative font-weight ---------------------------------------------

    private static void ResolveFontWeightKeywords(Dictionary<string, string> computed, int parentWeight)
    {
        if (!computed.TryGetValue("font-weight", out var fw) || string.IsNullOrEmpty(fw))
            return;

        if (int.TryParse(fw, out _))
            return;

        if (fw.Equals("normal", StringComparison.OrdinalIgnoreCase))
        {
            computed["font-weight"] = "400";
            return;
        }
        if (fw.Equals("bold", StringComparison.OrdinalIgnoreCase))
        {
            computed["font-weight"] = "700";
            return;
        }

        if (fw.Equals("bolder", StringComparison.OrdinalIgnoreCase))
            computed["font-weight"] = ResolveBolderWeight(parentWeight).ToString(CultureInfo.InvariantCulture);
        else if (fw.Equals("lighter", StringComparison.OrdinalIgnoreCase))
            computed["font-weight"] = ResolveLighterWeight(parentWeight).ToString(CultureInfo.InvariantCulture);
    }

    private static int ResolveBolderWeight(int parentWeight)
    {
        if (parentWeight < 400) return 400;
        if (parentWeight < 600) return 700;
        return 900;
    }

    private static int ResolveLighterWeight(int parentWeight)
    {
        if (parentWeight > 700) return 400;
        if (parentWeight > 500) return 400;
        return 100;
    }

    // ---- Media queries -----------------------------------------------------

    private static bool EvaluateMediaQuery(string query, int viewportWidth, int viewportHeight)
    {
        var queries = query.Split(',');
        foreach (var q in queries)
        {
            if (EvaluateSingleMediaQuery(q.Trim(), viewportWidth, viewportHeight))
                return true;
        }
        return false;
    }

    private static bool EvaluateSingleMediaQuery(string query, int viewportWidth, int viewportHeight)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;

        bool negate = false;
        var q = query.Trim();

        if (q.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
        {
            negate = true;
            q = q[4..].TrimStart();
        }
        else if (q.StartsWith("only ", StringComparison.OrdinalIgnoreCase))
        {
            q = q[5..].TrimStart();
        }

        var parts = SplitMediaQueryParts(q);
        bool result = true;

        foreach (var part in parts)
        {
            var p = part.Trim();
            if (string.IsNullOrEmpty(p)) continue;

            if (p.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("screen", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (p.StartsWith('(') && p.EndsWith(')'))
            {
                var condition = p[1..^1].Trim();
                if (!EvaluateMediaCondition(condition, viewportWidth, viewportHeight))
                {
                    result = false;
                    break;
                }
            }
            else
            {
                result = false;
                break;
            }
        }

        return negate ? !result : result;
    }

    private static List<string> SplitMediaQueryParts(string query)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < query.Length; i++)
        {
            if (query[i] == '(') depth++;
            else if (query[i] == ')') depth--;
            else if (depth == 0 && i + 5 <= query.Length)
            {
                var sub = query.Substring(i, Math.Min(5, query.Length - i));
                if (sub.Equals(" and ", StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(query[start..i]);
                    start = i + 5;
                    i += 4;
                }
            }
        }
        parts.Add(query[start..]);
        return parts;
    }

    private static bool EvaluateMediaCondition(string condition, int viewportWidth, int viewportHeight)
    {
        var colonIdx = condition.IndexOf(':');
        string feature;
        string? value = null;
        if (colonIdx >= 0)
        {
            feature = condition[..colonIdx].Trim().ToLowerInvariant();
            value = condition[(colonIdx + 1)..].Trim();
        }
        else
        {
            feature = condition.Trim().ToLowerInvariant();
        }

        const int ColorDepth = 8;
        const int MonochromeDepth = 0;

        switch (feature)
        {
            case "min-color":
                return value != null && int.TryParse(value, out var minColor) && minColor <= ColorDepth;
            case "max-color":
                return value != null && int.TryParse(value, out var maxColor) && maxColor >= ColorDepth;
            case "min-monochrome":
                return value != null && int.TryParse(value, out var minMono) && minMono <= MonochromeDepth;
            case "max-monochrome":
                return value != null && int.TryParse(value, out var maxMono) && maxMono >= MonochromeDepth;
            case "min-height":
                if (value != null)
                {
                    var px = CssLengthParser.ParseToPixels(value, viewportWidth, viewportHeight);
                    return !double.IsNaN(px) && viewportHeight >= Math.Max(0, px);
                }
                return false;
            case "max-height":
                if (value != null)
                {
                    var px = CssLengthParser.ParseToPixels(value, viewportWidth, viewportHeight);
                    return !double.IsNaN(px) && viewportHeight <= Math.Max(0, px);
                }
                return true;
            case "min-width":
                if (value != null)
                {
                    var px = CssLengthParser.ParseToPixels(value, viewportWidth, viewportHeight);
                    return !double.IsNaN(px) && viewportWidth >= Math.Max(0, px);
                }
                return false;
            case "max-width":
                if (value != null)
                {
                    var px = CssLengthParser.ParseToPixels(value, viewportWidth, viewportHeight);
                    return !double.IsNaN(px) && viewportWidth <= Math.Max(0, px);
                }
                return true;
            case "color":
                return true;
            case "-webkit-min-device-pixel-ratio":
            case "min-device-pixel-ratio":
                return value != null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var minDpr) && 1.0 >= minDpr;
            case "-webkit-max-device-pixel-ratio":
            case "max-device-pixel-ratio":
                return value != null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxDpr) && 1.0 <= maxDpr;
            case "min-resolution":
                return value != null && EvaluateResolutionCondition(value, isMin: true);
            case "max-resolution":
                return value != null && EvaluateResolutionCondition(value, isMin: false);
            case "pointer":
            case "any-pointer":
                return value != null && value.Trim().Equals("fine", StringComparison.OrdinalIgnoreCase);
            case "hover":
            case "any-hover":
                return value != null && value.Trim().Equals("hover", StringComparison.OrdinalIgnoreCase);
            case "prefers-color-scheme":
                return value != null && value.Trim().Equals("light", StringComparison.OrdinalIgnoreCase);
            case "prefers-reduced-motion":
                return value != null && value.Trim().Equals("no-preference", StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
    }

    private static bool EvaluateResolutionCondition(string value, bool isMin)
    {
        const double DeviceDpi = 96.0;
        const double DeviceDppx = 1.0;

        var v = value.Trim().ToLowerInvariant();
        double target;

        if (v.EndsWith("dppx"))
        {
            if (!double.TryParse(v[..^4], NumberStyles.Float, CultureInfo.InvariantCulture, out target))
                return false;
            return isMin ? DeviceDppx >= target : DeviceDppx <= target;
        }
        if (v.EndsWith("dpi"))
        {
            if (!double.TryParse(v[..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out target))
                return false;
            return isMin ? DeviceDpi >= target : DeviceDpi <= target;
        }
        if (v.EndsWith("dpcm"))
        {
            if (!double.TryParse(v[..^4], NumberStyles.Float, CultureInfo.InvariantCulture, out target))
                return false;
            double deviceDpcm = DeviceDpi / 2.54;
            return isMin ? deviceDpcm >= target : deviceDpcm <= target;
        }
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out target))
            return isMin ? DeviceDpi >= target : DeviceDpi <= target;
        return false;
    }

    // ---- Length parsing ----------------------------------------------------

    private static bool IsLengthOrPercentage(string v)
    {
        if (string.IsNullOrWhiteSpace(v))
            return false;

        v = v.Trim();
        if (v == "0")
            return true;
        if (v.EndsWith('%'))
            return double.TryParse(v[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        return !double.IsNaN(CssLengthParser.ParseToPixels(v));
    }

    // Properties whose grammar takes a <length-percentage> (optionally alongside
    // keywords such as auto/none), where a min()/max()/clamp() argument may never
    // be a bare <number>. Kept deliberately conservative — the core sizing, inset,
    // margin, padding, gap and text-indent/flex-basis families — so the
    // calc-type-checking rejection in IsAcceptableDeclarationValue can only drop a
    // value that is genuinely invalid, never one the renderer would have honoured.
    private static readonly HashSet<string> LengthPercentageProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "width", "height",
        "min-width", "min-height", "max-width", "max-height",
        "inline-size", "block-size",
        "min-inline-size", "min-block-size", "max-inline-size", "max-block-size",
        "top", "right", "bottom", "left",
        "inset", "inset-block", "inset-inline",
        "inset-block-start", "inset-block-end", "inset-inline-start", "inset-inline-end",
        "margin", "margin-top", "margin-right", "margin-bottom", "margin-left",
        "margin-block", "margin-inline",
        "margin-block-start", "margin-block-end", "margin-inline-start", "margin-inline-end",
        "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
        "padding-block", "padding-inline",
        "padding-block-start", "padding-block-end", "padding-inline-start", "padding-inline-end",
        "text-indent", "flex-basis",
        "gap", "row-gap", "column-gap",
        "grid-gap", "grid-row-gap", "grid-column-gap",
    };

    private static bool IsLengthPercentageProperty(string property) =>
        LengthPercentageProperties.Contains(property);

    private static readonly string[] ComparisonMathFunctionNames = { "min", "max", "clamp" };

    // True when the value contains a min()/max()/clamp() call that takes a bare
    // <number> (e.g. `min(0, 100%)`) as one of its top-level arguments. Every
    // occurrence is scanned, so a nested `min(0, …)` inside an outer function is
    // caught as well. calc() is not scanned — a <number> is a legal operand there.
    private static bool ComparisonMathFunctionHasBareNumberArgument(string value)
    {
        foreach (var name in ComparisonMathFunctionNames)
        {
            var searchFrom = 0;
            while (true)
            {
                var open = IndexOfFunctionCall(value, name, searchFrom);
                if (open < 0)
                    break;

                var openParen = open + name.Length;
                var closeParen = MatchingParenthesis(value, openParen);
                if (closeParen < 0)
                    break;

                var content = value.Substring(openParen + 1, closeParen - openParen - 1);
                foreach (var arg in SplitTopLevelCommaArguments(content))
                {
                    if (IsBareNumberTerm(arg))
                        return true;
                }

                searchFrom = openParen + 1;
            }
        }

        return false;
    }

    // Index of a `name(` function call in <paramref name="value"/> at or after
    // <paramref name="from"/>, requiring the character before the name not to be a
    // CSS identifier char so `max` is not matched inside `minmax(`.
    private static int IndexOfFunctionCall(string value, string name, int from)
    {
        var needle = name + "(";
        var i = from;
        while ((i = value.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            if (i == 0 || !IsCssIdentifierChar(value[i - 1]))
                return i;
            i += needle.Length;
        }

        return -1;
    }

    // Index of the ')' matching the '(' at <paramref name="openIndex"/>, or -1.
    private static int MatchingParenthesis(string value, int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < value.Length; i++)
        {
            if (value[i] == '(')
                depth++;
            else if (value[i] == ')' && --depth == 0)
                return i;
        }

        return -1;
    }

    private static List<string> SplitTopLevelCommaArguments(string content)
    {
        var args = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (c == '(')
                depth++;
            else if (c == ')')
                depth--;
            else if (c == ',' && depth == 0)
            {
                args.Add(content[start..i]);
                start = i + 1;
            }
        }

        args.Add(content[start..]);
        return args;
    }

    // A bare <number>: a finite numeric literal with no unit and no '%'. A parsed
    // special value (nan/infinity — valid calc() constants) is deliberately not
    // treated as a bare number.
    private static bool IsBareNumberTerm(string arg)
    {
        arg = arg.Trim();
        return arg.Length > 0
            && double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            && !double.IsNaN(n)
            && !double.IsInfinity(n);
    }

    private static bool IsCssIdentifierChar(char c) =>
        char.IsLetterOrDigit(c) || c == '-' || c == '_';

}
