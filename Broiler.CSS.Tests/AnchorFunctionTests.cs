using Broiler.CSS;

namespace Broiler.CSS.Tests;

/// <summary>
/// Behaviour-parity guard for <see cref="AnchorFunction"/>, the second neutral
/// anchor-positioning syntax model promoted out of the HtmlBridge anchor resolver
/// (Phase 5 item 4). Pins the exact <c>anchor()</c> / <c>anchor-size()</c> token
/// grammar and typed extraction ported from the bridge's former
/// <c>AnchorFunctionPattern</c> / <c>AnchorSizeFunctionPattern</c> regexes: name
/// optionality, side/dimension mapping, comma fallback (trimmed), embedded
/// rewriting inside larger values, and the anchor()/anchor-size() disjointness.
/// </summary>
public sealed class AnchorFunctionTests
{
    private static string Describe(AnchorFunctionRef r) =>
        $"[{r.Name ?? "<null>"}|{r.Side}|{r.Fallback ?? "<null>"}]";

    private static string DescribeSize(AnchorSizeFunctionRef r) =>
        $"[{r.Name ?? "<null>"}|{r.Dimension}]";

    [Theory]
    [InlineData("anchor(--a top)", "[--a|Top|<null>]")]
    [InlineData("anchor(top)", "[<null>|Top|<null>]")]
    [InlineData("anchor(--foo right)", "[--foo|Right|<null>]")]
    [InlineData("anchor(bottom)", "[<null>|Bottom|<null>]")]
    [InlineData("anchor(left)", "[<null>|Left|<null>]")]
    [InlineData("anchor(center)", "[<null>|Center|<null>]")]
    [InlineData("anchor(start)", "[<null>|Start|<null>]")]
    [InlineData("anchor(end)", "[<null>|End|<null>]")]
    // comma fallback, trimmed.
    [InlineData("anchor(--a top, 10px)", "[--a|Top|10px]")]
    [InlineData("anchor(--a top ,   20px )", "[--a|Top|20px]")]
    [InlineData("anchor(bottom, 5%)", "[<null>|Bottom|5%]")]
    // case-insensitive keyword, name value preserved verbatim.
    [InlineData("anchor(--A TOP)", "[--A|Top|<null>]")]
    public void Rewrite_ParsesSingleReference(string value, string expected)
    {
        string? seen = null;
        var result = AnchorFunction.Rewrite(value, r => { seen = Describe(r); return "X"; });
        Assert.Equal(expected, seen);
        Assert.Equal("X", result);
    }

    [Fact(Timeout = 600000)]
    public void Rewrite_ReplacesEmbeddedFunctionOnly()
    {
        var result = AnchorFunction.Rewrite("calc(anchor(--a bottom) + 5px)", _ => "100px");
        Assert.Equal("calc(100px + 5px)", result);
    }

    [Fact(Timeout = 600000)]
    public void Rewrite_ReplacesEveryReference()
    {
        int n = 0;
        var result = AnchorFunction.Rewrite("anchor(--a left) anchor(--a right)", _ => $"v{++n}");
        Assert.Equal("v1 v2", result);
        Assert.Equal(2, n);
    }

    [Theory]
    [InlineData("")]
    [InlineData("10px")]
    [InlineData("anchor-size(--a width)")] // anchor() pattern must NOT match anchor-size()
    public void Rewrite_LeavesNonMatchingValueUnchanged(string value)
    {
        var result = AnchorFunction.Rewrite(value, _ => "SHOULD_NOT_APPEAR");
        Assert.Equal(value, result);
    }

    [Theory]
    [InlineData("anchor-size(--a width)", "[--a|Width]")]
    [InlineData("anchor-size(width)", "[<null>|Width]")]
    [InlineData("anchor-size(--a height)", "[--a|Height]")]
    [InlineData("anchor-size(block)", "[<null>|Block]")]
    [InlineData("anchor-size(inline)", "[<null>|Inline]")]
    [InlineData("anchor-size(self-block)", "[<null>|SelfBlock]")]
    [InlineData("anchor-size(self-inline)", "[<null>|SelfInline]")]
    [InlineData("anchor-size(--B HEIGHT)", "[--B|Height]")]
    public void RewriteSize_ParsesReference(string value, string expected)
    {
        string? seen = null;
        var result = AnchorFunction.RewriteSize(value, r => { seen = DescribeSize(r); return "Y"; });
        Assert.Equal(expected, seen);
        Assert.Equal("Y", result);
    }

    [Fact(Timeout = 600000)]
    public void RewriteSize_DoesNotMatchPlainAnchor()
    {
        var result = AnchorFunction.RewriteSize("anchor(--a top)", _ => "SHOULD_NOT_APPEAR");
        Assert.Equal("anchor(--a top)", result);
    }

    [Fact(Timeout = 600000)]
    public void TryGetFirst_ReturnsFirstReference()
    {
        Assert.True(AnchorFunction.TryGetFirst("width: anchor(--a right) 0", out var r));
        Assert.Equal("--a", r.Name);
        Assert.Equal(AnchorSide.Right, r.Side);
    }

    [Theory]
    [InlineData("")]
    [InlineData("10px")]
    [InlineData("anchor-size(--a width)")]
    public void TryGetFirst_FalseWhenNoAnchorFunction(string value)
    {
        Assert.False(AnchorFunction.TryGetFirst(value, out _));
    }
}
