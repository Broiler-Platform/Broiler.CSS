using System;
using System.Text.RegularExpressions;

namespace Broiler.CSS;

/// <summary>The edge (physical or logical) selected by a CSS <c>anchor()</c> function.</summary>
public enum AnchorSide
{
    Top,
    Right,
    Bottom,
    Left,
    Start,
    End,
    Center,
}

/// <summary>The dimension selected by a CSS <c>anchor-size()</c> function.</summary>
public enum AnchorSizeDimension
{
    Width,
    Height,
    Block,
    Inline,
    SelfBlock,
    SelfInline,
}

/// <summary>
/// A parsed CSS <c>anchor(&lt;name&gt;? &lt;side&gt; , &lt;fallback&gt;?)</c>
/// reference. <see cref="Name"/> is <c>null</c> when the function omits an
/// explicit anchor name (the implicit <c>position-anchor</c> applies);
/// <see cref="Fallback"/> is <c>null</c> when no comma fallback is present and is
/// otherwise trimmed.
/// </summary>
public readonly record struct AnchorFunctionRef(string? Name, AnchorSide Side, string? Fallback);

/// <summary>
/// A parsed CSS <c>anchor-size(&lt;name&gt;? &lt;dimension&gt;)</c> reference.
/// <see cref="Name"/> is <c>null</c> when the function omits an explicit anchor name.
/// </summary>
public readonly record struct AnchorSizeFunctionRef(string? Name, AnchorSizeDimension Dimension);

/// <summary>
/// Canonical grammar for the CSS anchor-positioning query functions
/// <c>anchor()</c> and <c>anchor-size()</c>. Owns the syntax (token matching and
/// typed extraction) so consumers keep only the used-value computation.
/// </summary>
/// <remarks>
/// Promoted out of the HtmlBridge anchor resolver as the second neutral
/// anchor-positioning syntax model owned by <c>Broiler.CSS</c> (HtmlBridge
/// complexity-reduction roadmap, Phase 5 work item 4 — see
/// <see cref="PositionAreaValue"/> for the first). These functions appear
/// embedded inside larger declaration values (e.g. <c>left: anchor(--a right)</c>),
/// so the grammar is exposed as <see cref="Rewrite"/>/<see cref="RewriteSize"/>
/// helpers that locate each reference and hand the parsed, typed
/// <see cref="AnchorFunctionRef"/>/<see cref="AnchorSizeFunctionRef"/> to a
/// caller-supplied resolver — the geometry (edge coordinates, containing-block
/// math, scroll adjustment) stays with the consumer. Pure syntax: no geometry,
/// DOM, or anchor-registry knowledge lives here.
/// </remarks>
public static partial class AnchorFunction
{
    private static readonly Regex FunctionPattern = AnchorFunctionRegex();
    private static readonly Regex SizeFunctionPattern = AnchorSizeFunctionRegex();

    /// <summary>
    /// Replaces every <c>anchor()</c> reference in <paramref name="value"/> with the
    /// string returned by <paramref name="resolve"/> for that parsed reference.
    /// Non-matching text is preserved verbatim.
    /// </summary>
    public static string Rewrite(string value, Func<AnchorFunctionRef, string> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        if (string.IsNullOrEmpty(value))
            return value;
        return FunctionPattern.Replace(value, m => resolve(ToRef(m)));
    }

    /// <summary>
    /// Replaces every <c>anchor-size()</c> reference in <paramref name="value"/> with
    /// the string returned by <paramref name="resolve"/> for that parsed reference.
    /// </summary>
    public static string RewriteSize(string value, Func<AnchorSizeFunctionRef, string> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        if (string.IsNullOrEmpty(value))
            return value;
        return SizeFunctionPattern.Replace(value, m => resolve(ToSizeRef(m)));
    }

    /// <summary>
    /// Extracts the first <c>anchor()</c> reference in <paramref name="value"/>, if any.
    /// </summary>
    public static bool TryGetFirst(string value, out AnchorFunctionRef reference)
    {
        if (!string.IsNullOrEmpty(value))
        {
            var m = FunctionPattern.Match(value);
            if (m.Success)
            {
                reference = ToRef(m);
                return true;
            }
        }
        reference = default;
        return false;
    }

    private static AnchorFunctionRef ToRef(Match m)
    {
        var nameGroup = m.Groups["name"];
        string? name = nameGroup.Success && nameGroup.Value.Length > 0 ? nameGroup.Value : null;
        var fallbackGroup = m.Groups["fallback"];
        string? fallback = fallbackGroup.Success ? fallbackGroup.Value.Trim() : null;
        return new AnchorFunctionRef(name, MapSide(m.Groups["edge"].Value), fallback);
    }

    private static AnchorSizeFunctionRef ToSizeRef(Match m)
    {
        var nameGroup = m.Groups["name"];
        string? name = nameGroup.Success && nameGroup.Value.Length > 0 ? nameGroup.Value : null;
        return new AnchorSizeFunctionRef(name, MapDimension(m.Groups["dim"].Value));
    }

    private static AnchorSide MapSide(string edge) => edge.ToLowerInvariant() switch
    {
        "top" => AnchorSide.Top,
        "right" => AnchorSide.Right,
        "bottom" => AnchorSide.Bottom,
        "left" => AnchorSide.Left,
        "start" => AnchorSide.Start,
        "end" => AnchorSide.End,
        "center" => AnchorSide.Center,
        _ => AnchorSide.Center,
    };

    private static AnchorSizeDimension MapDimension(string dim) => dim.ToLowerInvariant() switch
    {
        "width" => AnchorSizeDimension.Width,
        "height" => AnchorSizeDimension.Height,
        "block" => AnchorSizeDimension.Block,
        "inline" => AnchorSizeDimension.Inline,
        "self-block" => AnchorSizeDimension.SelfBlock,
        "self-inline" => AnchorSizeDimension.SelfInline,
        _ => AnchorSizeDimension.Width,
    };

    [GeneratedRegex(@"anchor\(\s*(?:(?<name>--[a-zA-Z0-9_-]+)\s+)?(?<edge>top|right|bottom|left|start|end|center)\s*(?:,\s*(?<fallback>[^)]+?))?\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AnchorFunctionRegex();

    [GeneratedRegex(@"anchor-size\(\s*(?:(?<name>--[a-zA-Z0-9_-]+)\s+)?(?<dim>width|height|block|inline|self-block|self-inline)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AnchorSizeFunctionRegex();
}
