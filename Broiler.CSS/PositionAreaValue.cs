using System;

namespace Broiler.CSS;

/// <summary>
/// The selection within one axis (block or inline) of the CSS
/// <c>position-area</c> 3×3 grid. Named to match the region an axis keyword
/// selects relative to the anchor: <see cref="Start"/>/<see cref="End"/> pick the
/// cell before/after the anchor, <see cref="Center"/> the anchor's own span, and
/// the <c>Span*</c> members the two- and three-cell spans.
/// </summary>
public enum PositionAreaSpan
{
    /// <summary>The cell before the anchor on this axis (<c>top</c>/<c>left</c>/<c>start</c>).</summary>
    Start,
    /// <summary>The anchor's own span on this axis (<c>center</c>).</summary>
    Center,
    /// <summary>The cell after the anchor on this axis (<c>bottom</c>/<c>right</c>/<c>end</c>).</summary>
    End,
    /// <summary>Start cell through the anchor span (<c>span-top</c>/<c>span-left</c>/<c>span-start</c>).</summary>
    SpanStart,
    /// <summary>Anchor span through the end cell (<c>span-bottom</c>/<c>span-right</c>/<c>span-end</c>).</summary>
    SpanEnd,
    /// <summary>All three cells (<c>span-all</c>; also the default for an unrecognized keyword).</summary>
    SpanAll,
}

/// <summary>
/// Typed model of the CSS <c>position-area</c> value: the block-axis and
/// inline-axis grid selections that place an anchor-positioned element within
/// the 3×3 grid formed by the anchor and its containing block.
/// </summary>
/// <remarks>
/// Promoted out of the HtmlBridge anchor resolver as the first neutral
/// anchor-positioning syntax model owned by <c>Broiler.CSS</c> (HtmlBridge
/// complexity-reduction roadmap, Phase 5 work item 4 — "move neutral
/// anchor/keyframe/timing syntax models to Broiler.CSS first; Layout consumes
/// those models and applies them to boxes"). This type is pure syntax: it
/// classifies the keyword grammar into two axis selections and carries no
/// geometry, DOM, or containing-block knowledge — the used-value computation
/// (grid rectangle, insets, alignment) stays with its consumer for now.
/// <see cref="Parse"/> is total: any input (including empty, <c>none</c>, or an
/// unrecognized keyword) yields a value, defaulting each axis to
/// <see cref="PositionAreaSpan.SpanAll"/>.
/// </remarks>
public readonly record struct PositionAreaValue(PositionAreaSpan Block, PositionAreaSpan Inline)
{
    /// <summary>
    /// Parses a <c>position-area</c> value into its block- and inline-axis
    /// selections. Follows CSS Anchor Positioning axis disambiguation: an
    /// explicitly block/inline keyword is assigned to its axis; a single
    /// ambiguous keyword (<c>center</c>/<c>span-all</c>) applies to both axes;
    /// two keywords are ordered block-then-inline unless their explicit axes say
    /// otherwise. Unrecognized keywords fall back to
    /// <see cref="PositionAreaSpan.SpanAll"/>.
    /// </summary>
    public static PositionAreaValue Parse(string value)
    {
        var block = PositionAreaSpan.SpanAll;
        var inline = PositionAreaSpan.SpanAll;

        if (value is null)
            return new PositionAreaValue(block, inline);

        var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return new PositionAreaValue(block, inline);

        if (parts.Length == 1)
        {
            var sel = MapKeyword(parts[0]);
            var axis = ClassifyKeyword(parts[0]);
            if (axis == KeywordAxis.Block)
                block = sel;
            else if (axis == KeywordAxis.Inline)
                inline = sel;
            else // ambiguous single keyword
            {
                block = sel;
                inline = sel;
            }
            return new PositionAreaValue(block, inline);
        }

        // Two keywords: disambiguate axes.
        var sel1 = MapKeyword(parts[0]);
        var sel2 = MapKeyword(parts[1]);
        var axis1 = ClassifyKeyword(parts[0]);
        var axis2 = ClassifyKeyword(parts[1]);

        if (axis1 == KeywordAxis.Block && axis2 == KeywordAxis.Inline)
        { block = sel1; inline = sel2; }
        else if (axis1 == KeywordAxis.Inline && axis2 == KeywordAxis.Block)
        { inline = sel1; block = sel2; }
        else if (axis1 == KeywordAxis.Block && axis2 != KeywordAxis.Block)
        { block = sel1; inline = sel2; }
        else if (axis1 == KeywordAxis.Inline && axis2 != KeywordAxis.Inline)
        { inline = sel1; block = sel2; }
        else if (axis2 == KeywordAxis.Block)
        { inline = sel1; block = sel2; }
        else if (axis2 == KeywordAxis.Inline)
        { block = sel1; inline = sel2; }
        else
        { block = sel1; inline = sel2; } // both ambiguous → first=block, second=inline

        return new PositionAreaValue(block, inline);
    }

    private enum KeywordAxis { Block, Inline, Ambiguous }

    private static KeywordAxis ClassifyKeyword(string kw) => kw.Trim().ToLowerInvariant() switch
    {
        "top" or "bottom" or "span-top" or "span-bottom" or "block-start" or "block-end" => KeywordAxis.Block,
        "left" or "right" or "span-left" or "span-right" or "inline-start" or "inline-end" => KeywordAxis.Inline,
        _ => KeywordAxis.Ambiguous,
    };

    private static PositionAreaSpan MapKeyword(string kw) => kw.Trim().ToLowerInvariant() switch
    {
        "top" or "left" or "start" or "block-start" or "inline-start" => PositionAreaSpan.Start,
        "center" => PositionAreaSpan.Center,
        "bottom" or "right" or "end" or "block-end" or "inline-end" => PositionAreaSpan.End,
        "span-top" or "span-left" or "span-start" => PositionAreaSpan.SpanStart,
        "span-bottom" or "span-right" or "span-end" => PositionAreaSpan.SpanEnd,
        "span-all" or "all" => PositionAreaSpan.SpanAll,
        _ => PositionAreaSpan.SpanAll,
    };
}
