using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Broiler.CSS;

/// <summary>
/// Canonical parsing for CSS <c>@position-try</c> at-rules and the
/// <c>position-try</c> / <c>position-try-fallbacks</c> fallback list.
/// </summary>
/// <remarks>
/// Promoted out of the HtmlBridge anchor resolver as the third neutral
/// anchor-positioning syntax model owned by <c>Broiler.CSS</c> (HtmlBridge
/// complexity-reduction roadmap, Phase 5 work item 4 — see
/// <see cref="PositionAreaValue"/> and <see cref="AnchorFunction"/>). Pure text
/// parsing: <see cref="Parse"/> turns one stylesheet's text into a
/// name → declarations map, and <see cref="ParseFallbackList"/> splits a
/// fallback reference list. The consumer keeps the entangled parts — discovering
/// <c>&lt;style&gt;</c> elements and reading their source, and applying the
/// resolved declarations to boxes.
///
/// The grammar is intentionally the resolver's original regex approach (the CSS
/// rule serializer does not round-trip the <c>@position-try</c> at-rule), ported
/// verbatim: CSS comments are stripped first (a comment inside a rule body may
/// contain <c>:</c>/<c>;</c> that would corrupt declaration parsing), duplicate
/// rule names and duplicate declarations are last-wins, and declaration names are
/// matched case-insensitively.
/// </remarks>
public static partial class PositionTryRule
{
    private static readonly Regex RulePattern = PositionTryRuleRegex();
    private static readonly Regex CommentPattern = CssCommentRegex();

    /// <summary>
    /// Parses every <c>@position-try --name { … }</c> at-rule in one stylesheet's
    /// text into a map from rule name to its declaration block. Rule names are
    /// case-sensitive (ordinal); declaration property names are case-insensitive.
    /// Later duplicates win.
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> Parse(string cssText)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(cssText))
            return result;

        // Strip CSS comments first: a comment inside a @position-try body
        // (common in WPT, e.g. "/* 2: position right */") contains ':' and ';'
        // that would otherwise corrupt declaration parsing.
        var styleText = CommentPattern.Replace(cssText, " ");
        foreach (Match m in RulePattern.Matches(styleText))
        {
            var name = m.Groups["name"].Value;
            var body = m.Groups["body"].Value;
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var decl in body.Split(';'))
            {
                var trimmed = decl.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx < 0) continue;
                var propName = trimmed[..colonIdx].Trim();
                var propValue = trimmed[(colonIdx + 1)..].Trim();
                props[propName] = propValue;
            }
            result[name] = props;
        }
        return result;
    }

    /// <summary>
    /// Splits a <c>position-try-fallbacks</c> / <c>position-try</c> value into its
    /// comma-separated fallback references, each trimmed. Empty entries are
    /// preserved (an empty name matches no <c>@position-try</c> rule).
    /// </summary>
    public static string[] ParseFallbackList(string value)
    {
        if (value is null)
            return [];
        return [.. value.Split(',').Select(n => n.Trim())];
    }

    [GeneratedRegex(@"@position-try\s+(?<name>--[a-zA-Z0-9_-]+)\s*\{(?<body>[^}]*)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PositionTryRuleRegex();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex CssCommentRegex();
}
