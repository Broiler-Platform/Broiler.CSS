using System;
using System.Collections.Generic;
using System.Globalization;

namespace Broiler.CSS;

/// <summary>
/// Shared static CSS length and number resolution used by the layout engine
/// (<c>ParseLength</c>, <c>ParseNumber</c>, <c>IsValidLength</c>, and
/// <c>GetActualBorderWidth</c>).
/// Viewport-relative units depend on <see cref="SetViewportSize(float, float, string)"/> being called
/// per layout pass, mirroring the renderer.
/// </summary>
public static class CssLengthParser
{
    private readonly record struct LengthEvaluation(double Pixels, bool IsUnitless);

    /// <summary>Pre-computed factor for 1vh (viewport height / 100).</summary>
    [ThreadStatic]
    private static double _vhFactor;

    /// <summary>Pre-computed factor for 1vw (viewport width / 100).</summary>
    [ThreadStatic]
    private static double _vwFactor;

    /// <summary>Pre-computed factor for 1vmin (min dimension / 100).</summary>
    [ThreadStatic]
    private static double _vminFactor;

    /// <summary>Pre-computed factor for 1vmax (max dimension / 100).</summary>
    [ThreadStatic]
    private static double _vmaxFactor;

    /// <summary>
    /// Pre-computed factor for 1vi — 1% of the initial containing block along the
    /// root element's <em>inline</em> axis. Set by <see cref="SetViewportSize(float, float, string)"/>;
    /// equal to <see cref="_vwFactor"/> in a horizontal writing mode.
    /// </summary>
    [ThreadStatic]
    private static double _viFactor;

    /// <summary>
    /// Pre-computed factor for 1vb — 1% of the initial containing block along the
    /// root element's <em>block</em> axis. Set by <see cref="SetViewportSize(float, float, string)"/>;
    /// equal to <see cref="_vhFactor"/> in a horizontal writing mode.
    /// </summary>
    [ThreadStatic]
    private static double _vbFactor;

    /// <summary>
    /// Element <c>zoom</c> factor applied to <em>absolute</em> used lengths
    /// (<c>px</c>/<c>mm</c>/<c>cm</c>/<c>in</c>/<c>pt</c>/<c>pc</c>/<c>q</c>/<c>rem</c>/<c>rlh</c>)
    /// during evaluation — including the sub-terms of a <c>calc()</c>/<c>min()</c>/<c>max()</c>
    /// expression, which is why the scaling lives here rather than as a post-multiply at the call
    /// site. Font-relative units (<c>em</c>/<c>ex</c>/<c>ch</c>/<c>ic</c>/<c>lh</c>) are deliberately
    /// excluded: they already ride the caller's zoomed <c>emFactor</c>/<c>lineHeightFactor</c>.
    /// Viewport units are excluded too (element zoom does not scale the viewport). <c>0</c> (the
    /// thread-static default) is treated as the neutral <c>1.0</c>, so a caller that never opts in
    /// is byte-identical to the pre-zoom parser.
    /// </summary>
    [ThreadStatic]
    private static double _absoluteZoom;

    /// <summary>
    /// Element <c>zoom</c> factor applied to <em>percentage</em> terms. Percentages resolve against
    /// their (already resolved) <c>hundredPercent</c> basis; when that basis is the ancestor-zoomed
    /// containing block the caller passes its own zoom here to reach the effective factor, and when
    /// the basis is the box's own already-scaled size it passes <c>1.0</c>. <c>0</c> is treated as
    /// the neutral <c>1.0</c>.
    /// </summary>
    [ThreadStatic]
    private static double _percentZoom;

    private static double AbsoluteZoom => _absoluteZoom > 0 ? _absoluteZoom : 1.0;
    private static double PercentZoom => _percentZoom > 0 ? _percentZoom : 1.0;

    /// <summary>
    /// Sets the viewport dimensions used by <see cref="ParseLength"/> to
    /// resolve CSS viewport-relative units, assuming a horizontal writing mode
    /// for the logical units (<c>vi</c>/<c>vb</c>).
    /// </summary>
    public static void SetViewportSize(float width, float height) =>
        SetViewportSize(width, height, rootWritingMode: null);

    /// <summary>
    /// Sets the viewport dimensions used by <see cref="ParseLength"/> to resolve
    /// CSS viewport-relative units, mapping the logical units against
    /// <paramref name="rootWritingMode"/>.
    /// <para>
    /// CSS Values 4 §6.1.4 defines <c>vi</c>/<c>vb</c> against the <em>root
    /// element's</em> inline/block axis — not the axis of the element the unit
    /// appears on — so a per-pass factor is the right granularity. Under a
    /// vertical writing mode the axes swap: the inline axis runs down the page
    /// (so 1vi is 1% of the viewport height) and the block axis runs across it.
    /// </para>
    /// <para>
    /// DIAGNOSTIC NOTE (WPT issue #1491, problem 30): before <c>vb</c> resolved,
    /// <c>page-box-008-print.html</c>'s <c>block-size: 100vb</c> box got no size
    /// at all, so the test rendered 99% hotpink body background where Chromium
    /// renders 99% yellow.
    /// </para>
    /// </summary>
    public static void SetViewportSize(float width, float height, string? rootWritingMode)
    {
        _vwFactor = width * 0.01;
        _vhFactor = height * 0.01;
        _vminFactor = Math.Min(width, height) * 0.01;
        _vmaxFactor = Math.Max(width, height) * 0.01;

        if (CssWritingMode.IsVertical(rootWritingMode))
        {
            _viFactor = _vhFactor;
            _vbFactor = _vwFactor;
        }
        else
        {
            _viFactor = _vwFactor;
            _vbFactor = _vhFactor;
        }
    }

    /// <summary>
    /// Sets the element <c>zoom</c> factors used while resolving lengths, so a <c>calc()</c> whose
    /// sub-terms mix absolute, percentage and font-/viewport-relative units scales each term
    /// correctly (the hardcoded absolute unit→pixel factors are the only lever that cannot be reached
    /// from outside the parser). Callers set the factors around a parse and reset them to
    /// <c>1.0, 1.0</c> afterwards. Both default to the neutral <c>1.0</c>, so the parser is unchanged
    /// unless a caller opts in. See <see cref="_absoluteZoom"/>/<see cref="_percentZoom"/> for the
    /// unit split.
    /// </summary>
    public static void SetElementZoom(double absoluteZoom, double percentZoom)
    {
        _absoluteZoom = absoluteZoom;
        _percentZoom = percentZoom;
    }

    /// <summary>
    /// Whether <paramref name="unit"/> is an absolute used-length unit that element <c>zoom</c>
    /// scales. Mirrors the engine's non-<c>calc()</c> zoom classification: absolute physical units
    /// plus root-relative <c>rem</c>/<c>rlh</c>; excludes <c>em</c>/<c>ex</c>/<c>ch</c>/<c>ic</c>/<c>lh</c>
    /// (ride the zoomed font metrics) and the viewport units (unaffected by element zoom).
    /// </summary>
    private static bool IsElementZoomAbsoluteUnit(string unit) => unit switch
    {
        CssConstants.Px or CssConstants.Mm or CssConstants.Cm or CssConstants.In or
        CssConstants.Pt or CssConstants.Pc or CssConstants.Q or
        CssConstants.Rem or CssConstants.Rlh => true,
        _ => false,
    };

    public static bool IsValidLength(string value)
    {
        var defaultRootLineHeight = CssMetrics.DefaultFontSizePx * CssMetrics.NormalLineHeightFactor;
        if (TryEvaluateLengthExpression(value, 100f, CssMetrics.DefaultFontSizePx, null, fontAdjust: false, returnPoints: false,
            lineHeightFactor: CssMetrics.DefaultFontSizePx * CssMetrics.NormalLineHeightFactor,
            rootLineHeightFactor: defaultRootLineHeight, out _))
        {
            return true;
        }

        value = NormalizeSingleValueLengthFunction(value);

        // CSS2.1 §4.3.2: "0" is a valid length (unit identifier optional after zero).
        if (value == "0")
            return true;

        if (value.Length <= 1)
            return false;

        string number = string.Empty;

        if (value.EndsWith('%'))
        {
            number = value[..^1];
        }
        else if (value.EndsWith(CssConstants.Rem, StringComparison.Ordinal) && value.Length > 3)
        {
            number = value[..^3];
        }
        else if (value.EndsWith(CssConstants.Rlh, StringComparison.OrdinalIgnoreCase) && value.Length > 3)
        {
            number = value[..^3];
        }
        // CSS Values 3 §5.1.2 / Values 4 §6.1: the viewport units.
        // TryScanTrailingViewportUnit is the single scanner for the whole family
        // (physical, logical, and the small/large/dynamic variants), so validity
        // keys off it rather than re-listing the spellings here.
        else if (TryScanTrailingViewportUnit(value, out _, out var viewportUnitLength))
        {
            number = value[..^viewportUnitLength];
        }
        else if (value.Length > 2)
        {
            // CSS2.1 §4.3.2: Non-zero lengths require a valid unit identifier.
            var unit = value.Substring(value.Length - 2, 2);
            switch (unit)
            {
                case CssConstants.Em:
                case CssConstants.Ex:
                case CssConstants.Ch:
                case CssConstants.Ic:
                case CssConstants.Lh:
                case CssConstants.Px:
                case CssConstants.Mm:
                case CssConstants.Cm:
                case CssConstants.In:
                case CssConstants.Pt:
                case CssConstants.Pc:
                    number = value[..^2];
                    break;
                default:
                    // CSS Values 3 §5.1.2: 2-character viewport units (vh, vw)
                    if (unit.Equals(CssConstants.Vh, StringComparison.OrdinalIgnoreCase) ||
                        unit.Equals(CssConstants.Vw, StringComparison.OrdinalIgnoreCase))
                    {
                        number = value[..^2];
                        break;
                    }
                    return false; // unrecognized unit
            }
        }

        return double.TryParse(number, out _);
    }

    public static double ParseNumber(string number, double hundredPercent)
    {
        if (string.IsNullOrEmpty(number))
            return 0f;

        string toParse = number;
        bool isPercent = number.EndsWith('%');

        if (isPercent)
            toParse = number[..^1];

        if (!double.TryParse(toParse, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out double result))
            return 0f;

        if (isPercent)
            result = result / 100f * hundredPercent;

        return result;
    }

    public static double ParseLength(string length, double hundredPercent, double emFactor, bool fontAdjust = false) =>
        ParseLength(length, hundredPercent, emFactor, null, fontAdjust, false);

    public static double ParseLength(string length, double hundredPercent, double emFactor, string defaultUnit) =>
        ParseLength(length, hundredPercent, emFactor, defaultUnit, false, false);

    public static double ParseLength(string length, double hundredPercent, double emFactor, string defaultUnit,
        bool fontAdjust, bool returnPoints, double? lineHeightFactor = null, double? rootLineHeightFactor = null)
    {
        //Return zero if no length specified, zero specified
        if (string.IsNullOrEmpty(length) || length == "0")
            return 0f;

        var computedLineHeightFactor = lineHeightFactor ?? (emFactor * CssMetrics.NormalLineHeightFactor);
        var computedRootLineHeightFactor = rootLineHeightFactor
            ?? (CssMetrics.DefaultFontSizePx * CssMetrics.NormalLineHeightFactor);

        if (TryEvaluateLengthExpression(length, hundredPercent, emFactor, defaultUnit, fontAdjust, returnPoints,
            computedLineHeightFactor, computedRootLineHeightFactor, out var evaluated))
        {
            return evaluated;
        }

        length = NormalizeSingleValueLengthFunction(length);

        //If percentage, use ParseNumber
        if (length.EndsWith('%'))
            return ParseNumber(length, hundredPercent);

        //Get units of the length
        string unit = GetUnit(length, defaultUnit, out bool hasUnit, out int unitLen);

        // Number of the length. Trim by the unit AS WRITTEN: the small/large/
        // dynamic viewport variants canonicalise to a shorter spelling
        // (svmin → vmin), so unit.Length would leave the prefix on the number.
        string number = hasUnit
            ? length[..^unitLen]
            : length;

        // pt with returnPoints yields the raw point count (no px conversion).
        if (unit == CssConstants.Pt && returnPoints)
            return ParseNumber(number, hundredPercent);

        double factor = UnitToPixelFactor(unit, emFactor, fontAdjust,
            computedLineHeightFactor, computedRootLineHeightFactor);

        // An unrecognized unit resolves to 0px (legacy ParseLength default).
        if (double.IsNaN(factor))
            factor = 0f;

        return factor * ParseNumber(number, hundredPercent);
    }

    /// <summary>
    /// Resolves a CSS unit token to its multiplicative CSS-pixel factor. Single
    /// source of the unit → pixel table, shared by <see cref="ParseLength"/> and
    /// <see cref="TryParseSimpleLength"/>. Returns <see cref="double.NaN"/> for an
    /// unrecognized unit; callers map that to their own behaviour (ParseLength →
    /// factor 0; the math-expression evaluator → parse failure). The <c>pt</c>
    /// factor is always the px-per-point ratio — the <c>returnPoints</c> special
    /// case (yield the raw point count) is handled at each call site.
    /// </summary>
    private static double UnitToPixelFactor(string unit, double emFactor, bool fontAdjust,
        double lineHeightFactor, double rootLineHeightFactor) => unit switch
    {
        CssConstants.Em => emFactor,
        // rem is relative to the root element font size (default medium).
        CssConstants.Rem => CssMetrics.DefaultFontSizePx,
        CssConstants.Ex => emFactor / 2,
        // Approximate 1ch as half an em (8px advance for 16px monospace text).
        CssConstants.Ch => emFactor / 2,
        // Approximate 1ic as 1em.
        CssConstants.Ic => emFactor,
        CssConstants.Lh => lineHeightFactor,
        CssConstants.Rlh => rootLineHeightFactor,
        CssConstants.Px => fontAdjust ? CssMetrics.PxToPt : 1.0, //TODO: hi-dpi support
        CssConstants.Mm => CssMetrics.PxPerMm,
        CssConstants.Cm => CssMetrics.PxPerCm,
        CssConstants.In => CssMetrics.PxPerInch,
        CssConstants.Pt => CssMetrics.PtToPx,
        CssConstants.Pc => CssMetrics.PxPerPica,
        CssConstants.Q => CssMetrics.PxPerQ,
        // CSS Values 3 §5.1.2: viewport-relative units (1% of the axis).
        CssConstants.Vh => _vhFactor,
        CssConstants.Vw => _vwFactor,
        CssConstants.Vmin => _vminFactor,
        CssConstants.Vmax => _vmaxFactor,
        // CSS Values 4 §6.1.4: the logical viewport units. GetUnit canonicalises
        // the small/large/dynamic variants (svb, lvb, dvb, …) onto these, so the
        // table stays six entries wide.
        CssConstants.Vi => _viFactor,
        CssConstants.Vb => _vbFactor,
        _ => double.NaN,
    };

    /// <summary>
    /// Maps a viewport-unit token onto its canonical default-viewport spelling,
    /// or returns <see langword="null"/> when the token is not a viewport unit.
    /// <para>
    /// CSS Values 4 §6.1.2–6.1.4 defines four viewport sizes — default, small
    /// (<c>sv*</c>), large (<c>lv*</c>) and dynamic (<c>dv*</c>) — which differ
    /// only when retractable UA chrome overlaps the viewport. Broiler renders
    /// headless into a fixed surface with no such chrome, so all four coincide
    /// and the variants collapse onto the default spelling. That keeps one
    /// factor per axis rather than four identical copies; if a UA with dynamic
    /// chrome ever appears, this is the single place that has to split.
    /// </para>
    /// </summary>
    /// <summary>
    /// Viewport-unit spellings by character count, longest first — the scan order
    /// <see cref="TryScanTrailingViewportUnit"/> walks. Static so the hot length
    /// path does not allocate per call.
    /// </summary>
    private static readonly int[] ViewportUnitLengths = [5, 4, 3, 2];

    private static string? CanonicalViewportUnit(string unit)
    {
        var lower = unit.ToLowerInvariant();

        // Strip the small/large/dynamic viewport prefix, if any.
        if (lower.Length >= 3 && (lower[0] is 's' or 'l' or 'd') && lower[1] == 'v')
            lower = lower[1..];

        return lower switch
        {
            CssConstants.Vw or CssConstants.Vh or CssConstants.Vmin or
            CssConstants.Vmax or CssConstants.Vi or CssConstants.Vb => lower,
            _ => null,
        };
    }

    /// <summary>
    /// Scans <paramref name="length"/> for a trailing viewport unit, longest
    /// spelling first so <c>svmin</c> is not read as a stray <c>s</c> followed by
    /// <c>vmin</c>. Reports the canonical default-viewport spelling
    /// (<paramref name="canonical"/>) and how many characters the written unit
    /// occupied (<paramref name="tokenLength"/>) — the two differ for the
    /// small/large/dynamic variants. Every candidate leaves at least one
    /// character of number ahead of the unit, so a bare unit is not a length.
    /// </summary>
    private static bool TryScanTrailingViewportUnit(string length, out string canonical, out int tokenLength)
    {
        // 5: svmin/svmax/lvmin/lvmax/dvmin/dvmax · 4: vmin/vmax
        // 3: svw/svh/svi/svb and lv*/dv* peers   · 2: vw/vh/vi/vb

        foreach (var candidate in ViewportUnitLengths)
        {
            if (length.Length <= candidate)
                continue;

            if (CanonicalViewportUnit(length[^candidate..]) is { } match)
            {
                canonical = match;
                tokenLength = candidate;
                return true;
            }
        }

        canonical = string.Empty;
        tokenLength = 0;
        return false;
    }

    private static bool TryEvaluateLengthExpression(string expression, double hundredPercent, double emFactor,
        string defaultUnit, bool fontAdjust, bool returnPoints, double lineHeightFactor,
        double rootLineHeightFactor, out double result)
    {
        if (TryEvaluateLengthExpressionCore(expression, hundredPercent, emFactor,
                defaultUnit, fontAdjust, returnPoints, lineHeightFactor,
                rootLineHeightFactor, insideMathFunction: false,
                out var evaluation))
        {
            result = evaluation.Pixels;
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryEvaluateLengthExpressionCore(string expression,
        double hundredPercent, double emFactor, string defaultUnit, bool fontAdjust,
        bool returnPoints, double lineHeightFactor, double rootLineHeightFactor, bool insideMathFunction,
        out LengthEvaluation evaluation)
    {
        evaluation = default;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var current = expression.Trim();
        while (current.Length >= 2 && current[0] == '(' && current[^1] == ')' && HasBalancedParens(current[1..^1]))
            current = current[1..^1].Trim();

        if (TryEvaluateMathFunction(current,
                hundredPercent, emFactor,
                defaultUnit, fontAdjust,
                returnPoints, lineHeightFactor,
                rootLineHeightFactor, out evaluation))
        {
            return true;
        }

        var additiveOperatorIndex = FindTopLevelAdditiveOperator(current);
        if (additiveOperatorIndex > 0)
        {
            if (!TryEvaluateLengthExpressionCore(current[..additiveOperatorIndex],
                    hundredPercent, emFactor,
                    defaultUnit, fontAdjust,
                    returnPoints, lineHeightFactor,
                    rootLineHeightFactor, insideMathFunction: true,
                    out var left))
            {
                return false;
            }

            if (!TryEvaluateLengthExpressionCore(
                    current[(additiveOperatorIndex + 1)..],
                    hundredPercent,
                    emFactor,
                    defaultUnit,
                    fontAdjust,
                    returnPoints,
                    lineHeightFactor,
                    rootLineHeightFactor,
                    insideMathFunction: true,
                    out var right))
            {
                return false;
            }

            evaluation = new LengthEvaluation(
                current[additiveOperatorIndex] == '+'
                    ? left.Pixels + right.Pixels
                    : left.Pixels - right.Pixels,
                IsUnitless: false);
            return true;
        }

        return TryParseSimpleLength(current,
            hundredPercent, emFactor,
            defaultUnit, fontAdjust,
            returnPoints, lineHeightFactor,
            rootLineHeightFactor, insideMathFunction,
            out evaluation);
    }

    private static bool TryEvaluateMathFunction(
        string expression,
        double hundredPercent,
        double emFactor,
        string defaultUnit,
        bool fontAdjust,
        bool returnPoints,
        double lineHeightFactor,
        double rootLineHeightFactor,
        out LengthEvaluation evaluation)
    {
        evaluation = default;
        if (string.IsNullOrWhiteSpace(expression) || expression[^1] != ')')
            return false;

        static bool StartsWithFunction(string value, string functionName)
            => value.StartsWith(functionName + "(", StringComparison.OrdinalIgnoreCase);

        if (StartsWithFunction(expression, "calc"))
        {
            var content = expression[5..^1];
            return HasBalancedParens(content) &&
                   TryEvaluateLengthExpressionCore(
                       content,
                       hundredPercent,
                       emFactor,
                       defaultUnit,
                       fontAdjust,
                       returnPoints,
                       lineHeightFactor,
                       rootLineHeightFactor,
                       insideMathFunction: true,
                       out evaluation);
        }

        if (!StartsWithFunction(expression, "min") && !StartsWithFunction(expression, "max"))
            return false;

        var isMax = StartsWithFunction(expression, "max");
        var argsContent = expression[4..^1];
        if (!HasBalancedParens(argsContent))
            return false;

        var parts = SplitTopLevelArguments(argsContent);
        if (parts.Count == 0)
            return false;

        double? candidate = null;
        foreach (var part in parts)
        {
            if (!TryEvaluateLengthExpressionCore(
                    part,
                    hundredPercent,
                    emFactor,
                    defaultUnit,
                    fontAdjust,
                    returnPoints,
                    lineHeightFactor,
                    rootLineHeightFactor,
                    insideMathFunction: true,
                    out var value) ||
                value.IsUnitless)
            {
                return false;
            }

            candidate = candidate.HasValue
                ? (isMax ? Math.Max(candidate.Value, value.Pixels) : Math.Min(candidate.Value, value.Pixels))
                : value.Pixels;
        }

        if (!candidate.HasValue)
            return false;

        evaluation = new LengthEvaluation(candidate.Value, IsUnitless: false);
        return true;
    }

    private static bool TryParseSimpleLength(
        string expression,
        double hundredPercent,
        double emFactor,
        string defaultUnit,
        bool fontAdjust,
        bool returnPoints,
        double lineHeightFactor,
        double rootLineHeightFactor,
        bool insideMathFunction,
        out LengthEvaluation evaluation)
    {
        evaluation = default;
        var value = expression.Trim();
        if (string.IsNullOrEmpty(value))
            return false;

        if (value.EndsWith('%'))
        {
            evaluation = new LengthEvaluation(ParseNumber(value, hundredPercent) * PercentZoom, IsUnitless: false);
            return true;
        }

        string unit = GetUnit(value, defaultUnit, out bool hasUnit, out int unitLen);
        if (!hasUnit)
        {
            if (insideMathFunction)
                return false;

            if (double.TryParse(value, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out double raw))
            {
                evaluation = new LengthEvaluation(raw, IsUnitless: true);
                return true;
            }

            return false;
        }

        // Trim by the unit AS WRITTEN, not by the canonical spelling: the
        // small/large/dynamic viewport variants canonicalise to something shorter
        // (svmin → vmin), so unit.Length would leave the prefix on the number.
        string number = value[..^unitLen];
        if (!double.TryParse(number, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out double parsedNumber))
            return false;

        double factor = UnitToPixelFactor(unit, emFactor, fontAdjust,
            lineHeightFactor, rootLineHeightFactor);

        if (double.IsNaN(factor))
            return false;

        double pixels = unit == CssConstants.Pt && returnPoints
            ? ParseNumber(number, hundredPercent)
            : factor * parsedNumber;
        if (IsElementZoomAbsoluteUnit(unit))
            pixels *= AbsoluteZoom;
        evaluation = new LengthEvaluation(pixels, IsUnitless: false);
        return true;
    }

    /// <summary>
    /// Index of the last top-level <c>+</c>/<c>-</c> operator in a <c>calc()</c>-style
    /// expression (scanning right-to-left, ignoring operators inside parentheses and
    /// those that are actually a sign on the following term), or <c>-1</c> if none.
    /// Canonical CSS-syntax utility shared with the HtmlBridge length parser.
    /// </summary>
    public static int FindTopLevelAdditiveOperator(string expression)
    {
        var depth = 0;
        for (int i = expression.Length - 1; i >= 1; i--)
        {
            switch (expression[i])
            {
                case ')':
                    depth++;
                    break;
                case '(':
                    depth--;
                    break;
                case '+':
                case '-':
                    if (depth != 0)
                        break;

                    var leftIndex = i - 1;
                    while (leftIndex >= 0 && char.IsWhiteSpace(expression[leftIndex]))
                        leftIndex--;

                    var rightIndex = i + 1;
                    while (rightIndex < expression.Length && char.IsWhiteSpace(expression[rightIndex]))
                        rightIndex++;

                    if (leftIndex >= 0 &&
                        rightIndex < expression.Length &&
                        expression[leftIndex] != '(' &&
                        expression[leftIndex] != ',' &&
                        expression[leftIndex] != '+' &&
                        expression[leftIndex] != '-')
                    {
                        return i;
                    }
                    break;
            }
        }

        return -1;
    }

    /// <summary>
    /// Splits a comma-separated argument list (e.g. a <c>min()</c>/<c>max()</c> body) on its
    /// top-level commas, keeping nested <c>fn(...)</c> groups intact and trimming each argument.
    /// Canonical CSS-syntax utility shared with the HtmlBridge length parser.
    /// </summary>
    public static List<string> SplitTopLevelArguments(string value)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (int i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(value[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        parts.Add(value[start..].Trim());
        return parts;
    }

    /// <summary>
    /// Font-free approximation of a single CSS length value to pixels, used where no live
    /// font/box metrics are available: <c>em</c>/<c>rem</c>/<c>ic</c> = 16px, <c>ex</c>/<c>ch</c> = 8px,
    /// <c>lh</c>/<c>rlh</c> = 19.2px, and the viewport units — <c>vw</c>/<c>vh</c>, the logical
    /// <c>vi</c>/<c>vb</c>, <c>vmin</c>/<c>vmax</c>, and their <c>sv*</c>/<c>lv*</c>/<c>dv*</c>
    /// variants — resolved against the supplied viewport (0 = unavailable → those units yield
    /// <see cref="double.NaN"/>). <paramref name="rootWritingMode"/> selects the axes for the
    /// logical units. A bare number
    /// is treated as pixels. Returns <see cref="double.NaN"/> when the value cannot be parsed. Distinct
    /// from <see cref="ParseLength(string, double, double, bool)"/>, which resolves against caller-supplied
    /// font/percentage bases.
    /// </summary>
    public static double ParseToPixels(string value, int viewportWidth = 0, int viewportHeight = 0,
        string? rootWritingMode = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return double.NaN;

        var v = NormalizeSingleValueLengthFunction(value).Trim().ToLowerInvariant();

        // One scan covers the whole viewport family; the canonical spelling then
        // picks the axis, and tokenLength trims the written unit (svmin → 5).
        if (TryScanTrailingViewportUnit(v, out var viewportUnit, out var viewportUnitLength))
        {
            var vertical = CssWritingMode.IsVertical(rootWritingMode);
            var axis = viewportUnit switch
            {
                CssConstants.Vw => viewportWidth,
                CssConstants.Vh => viewportHeight,
                CssConstants.Vi => vertical ? viewportHeight : viewportWidth,
                CssConstants.Vb => vertical ? viewportWidth : viewportHeight,
                CssConstants.Vmin => Math.Min(viewportWidth, viewportHeight),
                CssConstants.Vmax => Math.Max(viewportWidth, viewportHeight),
                _ => 0,
            };

            if (axis > 0 && TryParseLeadingNumber(v, viewportUnitLength, out var viewportNumber))
                return (viewportNumber / 100.0) * axis;

            return double.NaN;
        }

        if (v.EndsWith("px"))
            return TryParseLeadingNumber(v, 2, out var px) ? px : double.NaN;
        if (v.EndsWith("rem"))
            return TryParseLeadingNumber(v, 3, out var rem) ? rem * 16.0 : double.NaN;
        if (v.EndsWith("em"))
            return TryParseLeadingNumber(v, 2, out var em) ? em * 16.0 : double.NaN;
        if (v.EndsWith("ex"))
            return TryParseLeadingNumber(v, 2, out var ex) ? ex * 8.0 : double.NaN;
        if (v.EndsWith("ch"))
            return TryParseLeadingNumber(v, 2, out var ch) ? ch * 8.0 : double.NaN;
        if (v.EndsWith("ic"))
            return TryParseLeadingNumber(v, 2, out var ic) ? ic * 16.0 : double.NaN;
        if (v.EndsWith("rlh"))
            return TryParseLeadingNumber(v, 3, out var rlh) ? rlh * 19.2 : double.NaN;
        if (v.EndsWith("lh"))
            return TryParseLeadingNumber(v, 2, out var lh) ? lh * 19.2 : double.NaN;

        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
            return raw;
        return double.NaN;
    }

    private static bool TryParseLeadingNumber(string value, int suffixLength, out double result) =>
        double.TryParse(value[..^suffixLength], NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    /// <summary>
    /// Unwraps a length value that is a single-argument <c>calc()</c>/<c>min()</c>/<c>max()</c>
    /// function (or redundant parentheses) down to its inner length token — e.g.
    /// <c>calc(10px)</c> → <c>10px</c>, <c>((2em))</c> → <c>2em</c>. A function containing a
    /// top-level comma (a genuine multi-argument <c>min()</c>/<c>max()</c>) or an operator is
    /// left untouched. Canonical CSS-syntax utility shared with the HtmlBridge length parser.
    /// </summary>
    public static string NormalizeSingleValueLengthFunction(string value)
    {
        var current = value.Trim();
        while (TryUnwrapSingleValueFunction(current, "calc", out var inner) ||
               TryUnwrapSingleValueFunction(current, "max", out inner) ||
               TryUnwrapSingleValueFunction(current, "min", out inner))
        {
            current = inner.Trim();
        }

        while (current.Length >= 2 && current[0] == '(' && current[^1] == ')' && HasBalancedParens(current[1..^1]))
            current = current[1..^1].Trim();

        return current;
    }

    private static bool TryUnwrapSingleValueFunction(string value, string functionName, out string inner)
    {
        inner = string.Empty;
        if (!value.StartsWith(functionName + "(", StringComparison.OrdinalIgnoreCase) || value[^1] != ')')
            return false;

        var content = value[(functionName.Length + 1)..^1];
        if (!HasBalancedParens(content))
            return false;

        var depth = 0;
        foreach (var ch in content)
        {
            switch (ch)
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    return false;
            }
        }

        inner = content;
        return true;
    }

    /// <summary>
    /// Whether every <c>(</c> in <paramref name="value"/> has a matching <c>)</c> and no
    /// <c>)</c> appears before its opener — i.e. the parentheses nest correctly. Canonical
    /// CSS-syntax utility shared with the HtmlBridge length parser (used to validate the
    /// body of a <c>calc()</c>/<c>min()</c>/<c>max()</c> before evaluating it).
    /// </summary>
    public static bool HasBalancedParens(string value)
    {
        var depth = 0;
        foreach (var ch in value)
        {
            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                depth--;
                if (depth < 0)
                    return false;
            }
        }

        return depth == 0;
    }

    /// <summary>
    /// Scans a length token for its trailing CSS unit (the single source of the
    /// substring/unit-matching logic, also consumed by <see cref="CssLength"/>).
    /// Returns the canonical unit
    /// string and sets <paramref name="hasUnit"/>; falls back to
    /// <paramref name="defaultUnit"/> when no unit is present.
    /// </summary>
    internal static string GetUnit(string length, string defaultUnit, out bool hasUnit) =>
        GetUnit(length, defaultUnit, out hasUnit, out _);

    /// <summary>
    /// As <see cref="GetUnit(string, string, out bool)"/>, but also reports how
    /// many characters the unit occupied <em>as written</em>.
    /// <para>
    /// The two can differ: the small/large/dynamic viewport variants canonicalise
    /// onto a shorter spelling (<c>svmin</c> → <c>vmin</c>), so a caller that
    /// trims the number by the canonical unit's length would leave the variant
    /// prefix behind — <c>"100svmin"</c> would yield the number <c>"100s"</c> and
    /// fail to parse. Every site that splits number-from-unit must use
    /// <paramref name="unitLength"/>, not <c>unit.Length</c>.
    /// </para>
    /// </summary>
    internal static string GetUnit(string length, string defaultUnit, out bool hasUnit, out int unitLength)
    {
        // The viewport family (vw/vh/vi/vb, vmin/vmax, and their sv*/lv*/dv*
        // variants) is scanned longest-spelling-first in one place, so "svmin" is
        // not mistaken for "vmin" preceded by a stray 's'.
        if (TryScanTrailingViewportUnit(length, out var viewportUnit, out var viewportUnitLength))
        {
            hasUnit = true;
            unitLength = viewportUnitLength;
            return viewportUnit;
        }

        // Check for 3-character units first (e.g. "rem")
        if (length.Length >= 4)
        {
            if (length.EndsWith(CssConstants.Rem, StringComparison.Ordinal))
            {
                hasUnit = true;
                unitLength = 3;
                return CssConstants.Rem;
            }

            if (length.EndsWith(CssConstants.Rlh, StringComparison.OrdinalIgnoreCase))
            {
                hasUnit = true;
                unitLength = 3;
                return CssConstants.Rlh;
            }
        }

        unitLength = 2;
        var unit = length.Length >= 3 ? length.Substring(length.Length - 2, 2) : string.Empty;
        switch (unit)
        {
            case CssConstants.Em:
            case CssConstants.Ex:
            case CssConstants.Ch:
            case CssConstants.Ic:
            case CssConstants.Lh:
            case CssConstants.Px:
            case CssConstants.Mm:
            case CssConstants.Cm:
            case CssConstants.In:
            case CssConstants.Pt:
            case CssConstants.Pc:
                hasUnit = true;
                break;
            default:
                // The 2-character viewport units (vh/vw/vi/vb) were already taken
                // by TryScanTrailingViewportUnit above.
                // Check for single-character units (e.g. "Q" / "q")
                if (length.Length >= 2)
                {
                    char lastChar = char.ToLowerInvariant(length[^1]);
                    char prevChar = length[^2];
                    if (lastChar == 'q' && (char.IsDigit(prevChar) || prevChar == '.'))
                    {
                        hasUnit = true;
                        unitLength = 1;
                        return CssConstants.Q;
                    }
                }
                hasUnit = false;
                unitLength = 0;
                unit = defaultUnit ?? string.Empty;
                break;
        }
        return unit;
    }
    public static double GetActualBorderWidth(string borderValue, double emHeight)
    {
        if (string.IsNullOrEmpty(borderValue))
            return GetActualBorderWidth(CssConstants.Medium, emHeight);

        // CSS spec / browser used values for the border-width keywords: thin=1px,
        // medium=3px, thick=5px. This is the single source of truth for the keyword
        // widths across the layout engine, the HtmlBridge anchor resolver, and the
        // native anchor path (which previously carried their own drifted copies).
        return borderValue switch
        {
            CssConstants.Thin => (double)1f,
            CssConstants.Medium => (double)3f,
            CssConstants.Thick => (double)5f,
            _ => Math.Abs(ParseLength(borderValue, 1, emHeight)),
        };
    }
}
