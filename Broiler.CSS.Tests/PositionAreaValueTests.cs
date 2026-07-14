using Broiler.CSS;

namespace Broiler.CSS.Tests;

/// <summary>
/// Behaviour-parity guard for <see cref="PositionAreaValue"/>, the first neutral
/// anchor-positioning syntax model promoted out of the HtmlBridge anchor resolver
/// (Phase 5 item 4). Pins the exact axis-classification and two-keyword
/// disambiguation contract ported from the bridge's former
/// <c>DomBridge.ParsePositionArea</c>, including the total-parse fallback to
/// <see cref="PositionAreaSpan.SpanAll"/> for empty/unknown input.
/// </summary>
public sealed class PositionAreaValueTests
{
    [Theory]
    // Single explicit block keyword → block axis set, inline defaults to SpanAll.
    [InlineData("top", PositionAreaSpan.Start, PositionAreaSpan.SpanAll)]
    [InlineData("bottom", PositionAreaSpan.End, PositionAreaSpan.SpanAll)]
    [InlineData("span-top", PositionAreaSpan.SpanStart, PositionAreaSpan.SpanAll)]
    [InlineData("span-bottom", PositionAreaSpan.SpanEnd, PositionAreaSpan.SpanAll)]
    [InlineData("block-start", PositionAreaSpan.Start, PositionAreaSpan.SpanAll)]
    [InlineData("block-end", PositionAreaSpan.End, PositionAreaSpan.SpanAll)]
    // Single explicit inline keyword → inline axis set, block defaults to SpanAll.
    [InlineData("left", PositionAreaSpan.SpanAll, PositionAreaSpan.Start)]
    [InlineData("right", PositionAreaSpan.SpanAll, PositionAreaSpan.End)]
    [InlineData("span-left", PositionAreaSpan.SpanAll, PositionAreaSpan.SpanStart)]
    [InlineData("span-right", PositionAreaSpan.SpanAll, PositionAreaSpan.SpanEnd)]
    [InlineData("inline-start", PositionAreaSpan.SpanAll, PositionAreaSpan.Start)]
    [InlineData("inline-end", PositionAreaSpan.SpanAll, PositionAreaSpan.End)]
    // Ambiguous single keyword → applied to BOTH axes.
    [InlineData("center", PositionAreaSpan.Center, PositionAreaSpan.Center)]
    [InlineData("span-all", PositionAreaSpan.SpanAll, PositionAreaSpan.SpanAll)]
    [InlineData("start", PositionAreaSpan.Start, PositionAreaSpan.Start)]
    [InlineData("end", PositionAreaSpan.End, PositionAreaSpan.End)]
    // Empty / unrecognized → total parse, both axes SpanAll.
    [InlineData("", PositionAreaSpan.SpanAll, PositionAreaSpan.SpanAll)]
    [InlineData("   ", PositionAreaSpan.SpanAll, PositionAreaSpan.SpanAll)]
    [InlineData("none", PositionAreaSpan.SpanAll, PositionAreaSpan.SpanAll)]
    [InlineData("bogus", PositionAreaSpan.SpanAll, PositionAreaSpan.SpanAll)]
    public void Parse_SingleKeyword(string value, PositionAreaSpan block, PositionAreaSpan inline)
    {
        var v = PositionAreaValue.Parse(value);
        Assert.Equal(block, v.Block);
        Assert.Equal(inline, v.Inline);
    }

    [Theory]
    // block then inline (canonical order).
    [InlineData("top left", PositionAreaSpan.Start, PositionAreaSpan.Start)]
    [InlineData("bottom right", PositionAreaSpan.End, PositionAreaSpan.End)]
    [InlineData("top right", PositionAreaSpan.Start, PositionAreaSpan.End)]
    // inline then block → still assigned by explicit axis, not order.
    [InlineData("left top", PositionAreaSpan.Start, PositionAreaSpan.Start)]
    [InlineData("right bottom", PositionAreaSpan.End, PositionAreaSpan.End)]
    // one explicit + one ambiguous: explicit keeps its axis, ambiguous fills the other.
    [InlineData("top center", PositionAreaSpan.Start, PositionAreaSpan.Center)]
    [InlineData("center bottom", PositionAreaSpan.End, PositionAreaSpan.Center)]
    // Quirk preserved from the bridge original: with an ambiguous first token and
    // an explicit inline second token, the ambiguous value lands on the BLOCK axis
    // (the axis2==Inline branch), unlike "center bottom" (axis2==Block) below.
    [InlineData("center left", PositionAreaSpan.Center, PositionAreaSpan.Start)]
    [InlineData("span-left bottom", PositionAreaSpan.End, PositionAreaSpan.SpanStart)]
    // both ambiguous → first=block, second=inline.
    [InlineData("center center", PositionAreaSpan.Center, PositionAreaSpan.Center)]
    [InlineData("start end", PositionAreaSpan.Start, PositionAreaSpan.End)]
    // 3+ tokens: only the first two are consumed (parity with the bridge original).
    [InlineData("top left right", PositionAreaSpan.Start, PositionAreaSpan.Start)]
    public void Parse_TwoKeywords(string value, PositionAreaSpan block, PositionAreaSpan inline)
    {
        var v = PositionAreaValue.Parse(value);
        Assert.Equal(block, v.Block);
        Assert.Equal(inline, v.Inline);
    }

    [Theory]
    [InlineData("TOP", PositionAreaSpan.Start, PositionAreaSpan.SpanAll)]
    [InlineData("Top Left", PositionAreaSpan.Start, PositionAreaSpan.Start)]
    [InlineData("  bottom   right  ", PositionAreaSpan.End, PositionAreaSpan.End)]
    public void Parse_IsCaseAndWhitespaceInsensitive(string value, PositionAreaSpan block, PositionAreaSpan inline)
    {
        var v = PositionAreaValue.Parse(value);
        Assert.Equal(block, v.Block);
        Assert.Equal(inline, v.Inline);
    }
}
