using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Dom;

namespace Broiler.CSS.Dom;

/// <summary>
/// <c>@container</c> size-query evaluation (css-conditional-5 / css-contain-3). A conditional group
/// rule applies its contents only when the element's query container satisfies the size condition.
/// </summary>
/// <remarks>
/// The engine resolves a query container's size from the container's own declared
/// <c>width</c>/<c>height</c> (explicit lengths and viewport units) rather than from layout, because
/// the cascade runs before layout. A container whose size cannot be resolved without layout
/// (<c>auto</c>/percentage) — or a container that cannot be found, or a feature that is not a
/// supported size feature (including <c>style()</c> queries) — makes the query <em>false</em>, which
/// is exactly the pre-support behaviour of dropping the rule, so an unresolved query never applies a
/// declaration that would not have applied before. Explicitly-sized containers (the common modal /
/// dialog / fixed-width cases) evaluate correctly.
/// </remarks>
public sealed partial class CssStyleEngine
{
    private enum ContainerFeatureRange { Exact, Min, Max }

    /// <summary>
    /// Evaluates an <c>@container</c> prelude for (<paramref name="element"/>,
    /// <paramref name="pseudoElement"/>): resolves the query container and tests the size condition
    /// against it. Returns <c>false</c> (rule dropped) when no container matches or the size is
    /// unknown.
    /// </summary>
    private bool EvaluateContainerQuery(string prelude, DomElement element, string? pseudoElement)
    {
        var text = prelude.Trim();
        if (text.Length == 0)
            return false;

        var (name, condition) = SplitContainerName(text);
        if (condition.Length == 0)
            return false;

        var container = FindQueryContainer(element, pseudoElement, name);
        if (container is null)
            return false;

        return EvaluateContainerCondition(condition, container.Value.InlineSize, container.Value.BlockSize);
    }

    /// <summary>Splits an optional leading <c>&lt;container-name&gt;</c> off a container condition. A
    /// leading identifier that is not <c>not</c>/<c>and</c>/<c>or</c> and not the start of a
    /// parenthesised query is the name; the remainder is the condition.</summary>
    private static (string? Name, string Condition) SplitContainerName(string text)
    {
        if (text[0] == '(')
            return (null, text);

        var i = 0;
        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '(')
            i++;
        var first = text[..i];
        if (first.Equals("not", StringComparison.OrdinalIgnoreCase) ||
            first.Equals("and", StringComparison.OrdinalIgnoreCase) ||
            first.Equals("or", StringComparison.OrdinalIgnoreCase))
            return (null, text);

        return (first, text[i..].TrimStart());
    }

    private readonly record struct ContainerBox(double? InlineSize, double? BlockSize);

    /// <summary>
    /// Finds the query container for (<paramref name="element"/>, <paramref name="pseudoElement"/>):
    /// the nearest ancestor establishing a size container (<c>container-type: size</c> or
    /// <c>inline-size</c>) whose <c>container-name</c> includes <paramref name="name"/> when one is
    /// given. For a pseudo-element the search includes the originating element itself (the pseudo is
    /// its child); for a real element it starts at the parent. Returns the container's resolved
    /// content-box sizes, or <c>null</c> when none is found.
    /// </summary>
    private ContainerBox? FindQueryContainer(DomElement element, string? pseudoElement, string? name)
    {
        var start = pseudoElement is not null ? element : ParentElement(element);
        for (var ancestor = start; ancestor is not null; ancestor = ParentElement(ancestor))
        {
            var declared = GetCascadedDeclarationMap(ancestor, null, includeInlineStyle: true);
            var type = (declared.GetValueOrDefault("container-type") ?? "normal").Trim().ToLowerInvariant();
            if (type is not ("size" or "inline-size"))
                continue;

            if (name is not null && !ContainerNameMatches(declared.GetValueOrDefault("container-name"), name))
                continue;

            var inlineSize = ResolveContainerLength(declared.GetValueOrDefault("width"));
            var blockSize = type == "size" ? ResolveContainerLength(declared.GetValueOrDefault("height")) : null;
            return new ContainerBox(inlineSize, blockSize);
        }

        return null;
    }

    private static bool ContainerNameMatches(string? declaredNames, string queryName)
    {
        if (string.IsNullOrWhiteSpace(declaredNames))
            return false;
        foreach (var token in declaredNames.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (token.Equals(queryName, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>Resolves a container's declared <c>width</c>/<c>height</c> to a content-box pixel
    /// length, or <c>null</c> when it needs layout (<c>auto</c>/percentage) or does not parse.</summary>
    private double? ResolveContainerLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var v = value.Trim();
        if (v.Equals("auto", StringComparison.OrdinalIgnoreCase) || v.Contains('%'))
            return null;
        var px = CssLengthParser.ParseToPixels(v, _environment.ViewportWidth, _environment.ViewportHeight);
        return double.IsNaN(px) ? null : Math.Max(0, px);
    }

    // ---- Condition evaluation ---------------------------------------------

    private static bool EvaluateContainerCondition(string condition, double? inlineSize, double? blockSize)
    {
        var tokens = TokenizeContainerCondition(condition);
        if (tokens.Count == 0)
            return false;

        bool? accumulated = null;
        string? op = null;
        var negateNext = false;

        foreach (var token in tokens)
        {
            if (token.Equals("and", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("or", StringComparison.OrdinalIgnoreCase))
            {
                op = token.ToLowerInvariant();
                continue;
            }
            if (token.Equals("not", StringComparison.OrdinalIgnoreCase))
            {
                negateNext = true;
                continue;
            }

            var value = EvaluateContainerGroup(token, inlineSize, blockSize);
            if (negateNext)
            {
                value = !value;
                negateNext = false;
            }

            accumulated = accumulated is null
                ? value
                : op == "or" ? accumulated.Value || value : accumulated.Value && value;
        }

        return accumulated ?? false;
    }

    /// <summary>Splits a condition into top-level tokens: parenthesised groups (nesting respected) and
    /// the <c>and</c>/<c>or</c>/<c>not</c> keywords between them.</summary>
    private static List<string> TokenizeContainerCondition(string s)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s[i]))
            {
                i++;
                continue;
            }

            if (s[i] == '(')
            {
                var depth = 0;
                var start = i;
                for (; i < s.Length; i++)
                {
                    if (s[i] == '(')
                        depth++;
                    else if (s[i] == ')')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            i++;
                            break;
                        }
                    }
                }
                tokens.Add(s[start..i]);
            }
            else
            {
                var start = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '(')
                    i++;
                tokens.Add(s[start..i]);
            }
        }

        return tokens;
    }

    private static bool EvaluateContainerGroup(string group, double? inlineSize, double? blockSize)
    {
        var inner = group.Trim();
        if (inner.StartsWith('(') && inner.EndsWith(')'))
            inner = inner[1..^1].Trim();

        // A nested query (further parentheses, or an and/or/not combination) recurses; otherwise this
        // is a single size feature.
        if (inner.Contains('('))
            return EvaluateContainerCondition(inner, inlineSize, blockSize);

        return EvaluateSizeFeature(inner, inlineSize, blockSize);
    }

    private static bool EvaluateSizeFeature(string feature, double? inlineSize, double? blockSize)
    {
        var f = feature.Trim();
        if (f.Length == 0)
            return false;

        // Plain / min- / max- colon form: `width: 200px`, `min-width: 200px`, `max-width: 200px`.
        var colon = f.IndexOf(':');
        if (colon >= 0)
        {
            var name = f[..colon].Trim().ToLowerInvariant();
            var range = ContainerFeatureRange.Exact;
            if (name.StartsWith("min-", StringComparison.Ordinal))
            {
                range = ContainerFeatureRange.Min;
                name = name[4..];
            }
            else if (name.StartsWith("max-", StringComparison.Ordinal))
            {
                range = ContainerFeatureRange.Max;
                name = name[4..];
            }

            var actual = AxisSize(name, inlineSize, blockSize);
            var bound = ParseFeatureLength(f[(colon + 1)..]);
            if (actual is null || bound is null)
                return false;

            return range switch
            {
                ContainerFeatureRange.Min => actual.Value >= bound.Value,
                ContainerFeatureRange.Max => actual.Value <= bound.Value,
                _ => Math.Abs(actual.Value - bound.Value) < 0.5,
            };
        }

        // Range form with comparison operators: `width > 1px`, `200px <= width`, `1px < width < 5px`.
        if (f.IndexOfAny(['<', '>', '=']) >= 0)
            return EvaluateRangeFeature(f, inlineSize, blockSize);

        // Boolean form: `(width)` is true when the size is known and non-zero.
        var boolActual = AxisSize(f.ToLowerInvariant(), inlineSize, blockSize);
        return boolActual is > 0;
    }

    private static bool EvaluateRangeFeature(string f, double? inlineSize, double? blockSize)
    {
        var (operands, ops) = SplitRange(f);

        if (ops.Count == 1)
        {
            var left = operands[0].Trim();
            var right = operands[1].Trim();
            var leftAxis = AxisSize(left.ToLowerInvariant(), inlineSize, blockSize);
            var rightAxis = AxisSize(right.ToLowerInvariant(), inlineSize, blockSize);

            if (leftAxis is not null && rightAxis is null)
            {
                var bound = ParseFeatureLength(right);
                return bound is not null && Compare(leftAxis.Value, ops[0], bound.Value);
            }
            if (rightAxis is not null && leftAxis is null)
            {
                var bound = ParseFeatureLength(left);
                return bound is not null && Compare(bound.Value, ops[0], rightAxis.Value);
            }
            return false;
        }

        if (ops.Count == 2)
        {
            // `<value> <op> <name> <op> <value>`
            var low = ParseFeatureLength(operands[0]);
            var actual = AxisSize(operands[1].Trim().ToLowerInvariant(), inlineSize, blockSize);
            var high = ParseFeatureLength(operands[2]);
            if (low is null || actual is null || high is null)
                return false;
            return Compare(low.Value, ops[0], actual.Value) && Compare(actual.Value, ops[1], high.Value);
        }

        return false;
    }

    private static (List<string> Operands, List<string> Ops) SplitRange(string f)
    {
        var operands = new List<string>();
        var ops = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < f.Length; i++)
        {
            var c = f[i];
            if (c is '<' or '>' or '=')
            {
                operands.Add(current.ToString());
                current.Clear();
                var op = c.ToString();
                if (c is '<' or '>' && i + 1 < f.Length && f[i + 1] == '=')
                {
                    op += "=";
                    i++;
                }
                ops.Add(op);
            }
            else
            {
                current.Append(c);
            }
        }
        operands.Add(current.ToString());
        return (operands, ops);
    }

    private static bool Compare(double a, string op, double b) => op switch
    {
        "<" => a < b,
        "<=" => a <= b,
        ">" => a > b,
        ">=" => a >= b,
        "=" => Math.Abs(a - b) < 0.5,
        _ => false,
    };

    private static double? AxisSize(string name, double? inlineSize, double? blockSize) => name switch
    {
        "width" or "inline-size" => inlineSize,
        "height" or "block-size" => blockSize,
        _ => null,
    };

    private static double? ParseFeatureLength(string value)
    {
        var v = value.Trim();
        if (v.Length == 0 || v.Contains('%'))
            return null;
        var px = CssLengthParser.ParseToPixels(v);
        return double.IsNaN(px) ? null : px;
    }
}
