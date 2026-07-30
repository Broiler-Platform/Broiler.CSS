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

        // A size container needs container-type: size/inline-size; a STYLE container does not —
        // css-contain-3 makes every element a style query container. So the two are resolved
        // separately and a style-only query still evaluates when no size container exists.
        var sizeContainer = FindQueryContainer(element, pseudoElement, name);
        var styleContainer = FindStyleQueryContainer(element, pseudoElement, name);

        if (sizeContainer is null && styleContainer is null)
            return false;

        return EvaluateContainerCondition(
            condition, sizeContainer?.InlineSize, sizeContainer?.BlockSize, styleContainer);
    }

    /// <summary>
    /// Finds the style query container for (<paramref name="element"/>,
    /// <paramref name="pseudoElement"/>): the nearest ancestor whose <c>container-name</c> includes
    /// <paramref name="name"/>, or simply the nearest ancestor when the query is unnamed. Unlike a
    /// size container this needs no <c>container-type</c> — css-contain-3 §"Style Container
    /// Features" makes every element a style container.
    /// </summary>
    private DomElement? FindStyleQueryContainer(DomElement element, string? pseudoElement, string? name)
    {
        var start = pseudoElement is not null ? element : ParentElement(element);
        if (name is null)
            return start;

        for (var ancestor = start; ancestor is not null; ancestor = ParentElement(ancestor))
        {
            var declared = GetCascadedDeclarationMap(ancestor, null, includeInlineStyle: true);
            if (ContainerNameMatches(declared.GetValueOrDefault("container-name"), name))
                return ancestor;
        }

        return null;
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

        // An identifier immediately followed by '(' is a FUNCTION, not a container name —
        // `@container style(--x: y)` queries an unnamed container, it does not name one "style".
        // Reading it as a name sent the lookup hunting for container-name: style, found nothing,
        // and made every style query false.
        if (i < text.Length && text[i] == '(')
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

    /// <summary>
    /// Bounds <c>@container</c> condition nesting. Every recursion step consumes at least one
    /// character of the prelude, so no real query comes close to this; the cap exists because a
    /// .NET stack overflow cannot be caught and kills the process outright, so a future grammar gap
    /// must degrade to "query false" (the rule is dropped) rather than take the host down.
    /// </summary>
    private const int MaxContainerConditionDepth = 32;

    private bool EvaluateContainerCondition(
        string condition, double? inlineSize, double? blockSize, DomElement? styleContainer,
        int depth = 0)
    {
        if (depth > MaxContainerConditionDepth)
            return false;

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

            var value = EvaluateContainerGroup(token, inlineSize, blockSize, styleContainer, depth);
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

                // A function token — style(...) — keeps its argument list: splitting `style` from
                // `(...)` would leave the argument looking like a nested condition and the name
                // looking like a bare size feature, so both halves would evaluate false.
                if (i < s.Length && s[i] == '(')
                {
                    var depth = 0;
                    for (; i < s.Length; i++)
                    {
                        if (s[i] == '(')
                        {
                            depth++;
                        }
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
                }

                tokens.Add(s[start..i]);
            }
        }

        return tokens;
    }

    private bool EvaluateContainerGroup(
        string group, double? inlineSize, double? blockSize, DomElement? styleContainer, int depth)
    {
        var inner = group.Trim();

        // style(...) is a feature, not a nesting group — check before unwrapping parentheses, or
        // its argument would be mistaken for a nested condition.
        if (TryReadStyleFeature(inner, out var styleFeature))
            return EvaluateStyleFeature(styleFeature, styleContainer);

        // Any other lone query function — anchored(), scroll-state(), a future one — is
        // <general-enclosed> in css-conditional-5 and evaluates false, exactly like an unsupported
        // feature. It has to be answered here rather than treated as nesting: its argument list is
        // parenthesised but is not a container condition, so recursing re-tokenized the identical
        // single token forever.
        if (IsFunctionToken(inner))
            return false;

        if (TryUnwrapGroup(inner, out var unwrapped))
            inner = unwrapped;

        if (TryReadStyleFeature(inner, out styleFeature))
            return EvaluateStyleFeature(styleFeature, styleContainer);

        if (IsFunctionToken(inner))
            return false;

        // A nested query (a parenthesised group, or an and/or/not combination) recurses; otherwise
        // this is a single size feature.
        if (IsNestedCondition(inner))
            return EvaluateContainerCondition(inner, inlineSize, blockSize, styleContainer, depth + 1);

        return EvaluateSizeFeature(inner, inlineSize, blockSize);
    }

    /// <summary>
    /// Distinguishes a nested <c>&lt;container-condition&gt;</c> from a single
    /// <c>&lt;size-feature&gt;</c>, given a <c>&lt;query-in-parens&gt;</c> whose wrapping
    /// parentheses have been removed. A condition either opens another group or joins groups with a
    /// top-level <c>and</c>/<c>or</c>/<c>not</c>; a size feature does neither. Testing for a bare
    /// <c>(</c> instead misread the value function in <c>width = calc(100px + 10rem)</c> as
    /// nesting, and re-tokenizing that produced the same token at every level — an unbounded
    /// recursion that took the process down with it.
    /// </summary>
    private static bool IsNestedCondition(string inner)
    {
        // A leading '(' that never closes is malformed, not nesting. Recursing on it would hand the
        // same unbalanced text back to the tokenizer unchanged; treating it as a size feature makes
        // the query false, which is how every other unparsable prelude already behaves.
        if (inner.StartsWith('(') && MatchParen(inner, 0) > 0)
            return true;

        foreach (var token in TokenizeContainerCondition(inner))
        {
            if (IsCombinator(token))
                return true;
        }

        return false;
    }

    private static bool IsCombinator(string token) =>
        token.Equals("and", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("or", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("not", StringComparison.OrdinalIgnoreCase);

    /// <summary>Strips the parentheses of a <c>( … )</c> group, or reports false when
    /// <paramref name="text"/> is not one — including when a leading <c>(</c> is closed before the
    /// end, as in <c>(width) and (height)</c>, where stripping both ends would splice two groups
    /// into one malformed condition.</summary>
    private static bool TryUnwrapGroup(string text, out string inner)
    {
        inner = string.Empty;
        if (text.Length == 0 || text[0] != '(' || MatchParen(text, 0) != text.Length)
            return false;

        inner = text[1..^1].Trim();
        return true;
    }

    /// <summary>True when <paramref name="text"/> is exactly one function token — an identifier
    /// followed by a parenthesised argument list that closes at the end of the string.</summary>
    private static bool IsFunctionToken(string text) => TryReadFunction(text, out _, out _);

    /// <summary>Reads a whole-string <c>name(args)</c> function token.</summary>
    private static bool TryReadFunction(string text, out string name, out string arguments)
    {
        name = string.Empty;
        arguments = string.Empty;

        var i = 0;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '-' or '_'))
            i++;
        if (i == 0)
            return false;

        // `not (width > 0px)` is a negated group, not a call to a function named `not` — the
        // combinators are keywords and can never open an argument list.
        var end = i;
        if (IsCombinator(text[..end]))
            return false;

        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
        if (i >= text.Length || text[i] != '(' || MatchParen(text, i) != text.Length)
            return false;

        name = text[..end];
        arguments = text[(i + 1)..^1].Trim();
        return true;
    }

    /// <summary>Returns the index just past the <c>)</c> closing the <c>(</c> at
    /// <paramref name="open"/>, or <c>-1</c> when the parentheses never balance.</summary>
    private static int MatchParen(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')' && --depth == 0)
            {
                return i + 1;
            }
        }

        return -1;
    }

    /// <summary>Reads the body of a <c>style(...)</c> query, or reports false when
    /// <paramref name="group"/> is not one.</summary>
    private static bool TryReadStyleFeature(string group, out string feature)
    {
        feature = string.Empty;
        if (!TryReadFunction(group, out var name, out var arguments) ||
            !name.Equals("style", StringComparison.OrdinalIgnoreCase))
            return false;

        feature = arguments;
        return feature.Length > 0;
    }

    /// <summary>
    /// Evaluates a <c>style(--name: value)</c> query against <paramref name="container"/>.
    /// <para>
    /// Only the custom-property form is supported. A registered (<c>@property</c>) or plain custom
    /// property is looked up on the container, its <c>contrast-color()</c> resolved, and compared
    /// to the queried value after whitespace normalisation — css-contain-3 compares computed
    /// values, and for a custom property that is its token stream. A query naming a non-custom
    /// property returns false, matching the pre-support behaviour of dropping the rule rather than
    /// applying a declaration on a guess.
    /// </para>
    /// </summary>
    private bool EvaluateStyleFeature(string feature, DomElement? container)
    {
        if (container is null)
            return false;

        var colon = feature.IndexOf(':');
        if (colon < 0)
            return false;

        var name = feature[..colon].Trim();
        var expected = feature[(colon + 1)..].Trim();
        if (!name.StartsWith("--", StringComparison.Ordinal) || expected.Length == 0)
            return false;

        var actual = ResolveStyleQueryProperty(container, name);
        if (actual is null)
            return false;

        // A registered <color> property computes to an absolute colour, so `white` and
        // `rgb(255, 255, 255)` are the same computed value and must compare equal. Colour
        // comparison first, then the token-stream comparison an unregistered property gets.
        if (CssValueParser.TryParseColor(actual, out var actualColor) &&
            CssValueParser.TryParseColor(expected, out var expectedColor))
        {
            return actualColor == expectedColor;
        }

        return NormalizeStyleQueryValue(actual) == NormalizeStyleQueryValue(expected);
    }

    /// <summary>
    /// Resolves a custom property for a style query by walking the container's ancestor chain for
    /// the nearest cascaded declaration, then falling back to the <c>@property</c>
    /// <c>initial-value</c>.
    /// <para>
    /// The cascaded map is used rather than <c>GetComputedStyle</c> deliberately: this runs
    /// <em>during</em> style computation, and re-entering the full computation for an ancestor
    /// would recurse. Walking for the nearest declaration models the default inheritance of custom
    /// properties; a registration with <c>inherits: false</c> is not honoured here, which is a
    /// known gap rather than an oversight.
    /// </para>
    /// </summary>
    private string? ResolveStyleQueryProperty(DomElement container, string name)
    {
        for (var ancestor = container; ancestor is not null; ancestor = ParentElement(ancestor))
        {
            var declared = GetCascadedDeclarationMap(ancestor, null, includeInlineStyle: true);
            if (declared.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return CssContrastColor.ResolveFunctions(value.Trim());
        }

        var registrations = CollectCustomPropertyRegistrations();
        if (registrations.TryGetValue(name, out var registration) && registration.InitialValue is { } initial)
            return CssContrastColor.ResolveFunctions(initial.Trim());

        return null;
    }

    /// <summary>Collapses internal whitespace and lower-cases, so <c>rgb(255, 255, 255)</c> and
    /// <c>rgb(255 255 255)</c>-style spacing differences do not defeat the comparison.</summary>
    private static string NormalizeStyleQueryValue(string value)
    {
        var sb = new StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                lastWasSpace = true;
                continue;
            }

            if (lastWasSpace && sb.Length > 0 && ch != ',' && ch != ')')
                sb.Append(' ');
            lastWasSpace = false;
            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
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
