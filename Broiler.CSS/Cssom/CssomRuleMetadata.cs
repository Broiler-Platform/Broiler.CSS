using System;
using System.Linq;

namespace Broiler.CSS.Cssom;

/// <summary>
/// The legacy CSSOM numeric <c>CSSRule.type</c> values. Modern rule interfaces
/// keep these constants for compatibility even where the spec has retired the
/// numeric type.
/// </summary>
public enum CssomRuleType
{
    Unknown = 0,
    Style = 1,
    Charset = 2,
    Import = 3,
    Media = 4,
    FontFace = 5,
    Page = 6,
    Keyframes = 7,
    Keyframe = 8,
    Namespace = 9,
    CounterStyle = 10,
    Supports = 11,
    Layer = 12,
    Property = 25,
}

/// <summary>Decomposed <c>@import</c> prelude.</summary>
public readonly record struct CssImportMetadata(string Href, string Media);

/// <summary>Decomposed <c>@namespace</c> prelude.</summary>
public readonly record struct CssNamespaceMetadata(string? Prefix, string Uri);

/// <summary>
/// Engine-neutral CSSOM metadata projected directly from the parsed
/// <see cref="CssRule"/> model. Consumers (notably the HtmlBridge CSSOM wrappers)
/// read structured rule metadata here instead of serializing a rule to text and
/// re-parsing it — <c>Broiler.CSS</c> already knows the rule kind, selectors,
/// prelude, and declarations, so this exposes them without a round-trip.
/// </summary>
/// <remarks>
/// Rule <em>kind</em> follows the parser's lowercased at-rule <see cref="CssAtRule.Name"/>.
/// At-rules whose name is not a recognized CSSOM rule (for example
/// <c>@container</c> or the vendor-prefixed <c>@-webkit-keyframes</c>) report
/// <see cref="CssomRuleType.Unknown"/>; callers decide how to surface those.
/// </remarks>
public static class CssomRuleMetadata
{
    /// <summary>The legacy CSSOM numeric type for <paramref name="rule"/>.</summary>
    public static CssomRuleType GetRuleType(CssRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule switch
        {
            CssStyleRule => CssomRuleType.Style,
            CssAtRule atRule => atRule.Name.ToLowerInvariant() switch
            {
                "charset" => CssomRuleType.Charset,
                "import" => CssomRuleType.Import,
                "media" => CssomRuleType.Media,
                "font-face" => CssomRuleType.FontFace,
                "page" => CssomRuleType.Page,
                "keyframes" => CssomRuleType.Keyframes,
                "namespace" => CssomRuleType.Namespace,
                "counter-style" => CssomRuleType.CounterStyle,
                "supports" => CssomRuleType.Supports,
                "layer" => CssomRuleType.Layer,
                "property" => CssomRuleType.Property,
                _ => CssomRuleType.Unknown,
            },
            _ => CssomRuleType.Unknown,
        };
    }

    /// <summary>The numeric <c>CSSRule.type</c> value for <paramref name="rule"/>.</summary>
    public static int GetCssomTypeNumber(CssRule rule) => (int)GetRuleType(rule);

    /// <summary>
    /// The comma-separated <c>selectorText</c> for a style rule, formatted exactly
    /// as the serializer emits it (used for both top-level style rules and
    /// <c>@keyframes</c> keyframe blocks, whose selector is the key text).
    /// </summary>
    public static string GetSelectorText(CssStyleRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return string.Join(", ", rule.Selectors.Selectors.Select(static selector => selector.Text));
    }

    /// <summary>The <c>@keyframes</c> name (its prelude, with surrounding quotes removed).</summary>
    public static string GetKeyframesName(CssAtRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.Prelude.Trim().Trim('"', '\'');
    }

    /// <summary>The <c>@charset</c> encoding (its prelude, with surrounding quotes removed).</summary>
    public static string GetCharsetEncoding(CssAtRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.Prelude.Trim().TrimEnd(';').Trim().Trim('"', '\'');
    }

    /// <summary>Decomposes an <c>@import</c> prelude into its href and media list.</summary>
    public static CssImportMetadata GetImport(CssAtRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var importBody = rule.Prelude.Trim().TrimEnd(';').Trim();
        var href = string.Empty;
        var mediaText = string.Empty;

        if (importBody.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var openParen = importBody.IndexOf('(');
            var closeParen = importBody.IndexOf(')', openParen + 1);
            if (openParen >= 0 && closeParen > openParen)
            {
                href = importBody.Substring(openParen + 1, closeParen - openParen - 1).Trim().Trim('"', '\'');
                mediaText = importBody[(closeParen + 1)..].Trim();
            }
        }
        else if (importBody.StartsWith("\"", StringComparison.Ordinal) || importBody.StartsWith('\''))
        {
            var quote = importBody[0];
            var closingQuote = importBody.IndexOf(quote, 1);
            if (closingQuote > 0)
            {
                href = importBody[1..closingQuote];
                mediaText = importBody[(closingQuote + 1)..].Trim();
            }
        }

        return new CssImportMetadata(href, mediaText);
    }

    /// <summary>Decomposes an <c>@namespace</c> prelude into its optional prefix and URI.</summary>
    public static CssNamespaceMetadata GetNamespace(CssAtRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var namespaceBody = rule.Prelude.Trim().TrimEnd(';').Trim();
        string? prefix = null;
        var namespaceUri = string.Empty;

        var parts = namespaceBody.Split([' ', '\t', '\r', '\n'], 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            prefix = parts[0];
            namespaceUri = ExtractNamespaceUri(parts[1]);
        }
        else if (parts.Length == 1)
        {
            namespaceUri = ExtractNamespaceUri(parts[0]);
        }

        return new CssNamespaceMetadata(prefix, namespaceUri);
    }

    /// <summary>
    /// Strips one matching layer of single or double quotes from an at-rule descriptor
    /// value (e.g. an <c>@property</c> <c>syntax</c> descriptor), leaving unquoted input
    /// unchanged.
    /// </summary>
    public static string UnquoteDescriptor(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            return value[1..^1];

        return value;
    }

    /// <summary>
    /// Escapes backslashes and double quotes so a descriptor value can be re-emitted
    /// inside a double-quoted CSS string (used when serializing <c>@property</c>
    /// <c>cssText</c>).
    /// </summary>
    public static string EscapeDescriptorString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>Extracts a namespace URI from a quoted string or <c>url(...)</c> token.</summary>
    public static string ExtractNamespaceUri(string uriPart)
    {
        uriPart = uriPart.Trim();

        if (uriPart.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var openParen = uriPart.IndexOf('(');
            var closeParen = uriPart.LastIndexOf(')');
            if (openParen >= 0 && closeParen > openParen)
            {
                return uriPart.Substring(openParen + 1, closeParen - openParen - 1)
                    .Trim()
                    .Trim('"', '\'');
            }
        }

        if (uriPart.Length > 1 && (uriPart[0] == '"' || uriPart[0] == '\''))
        {
            var quote = uriPart[0];
            var closingQuote = uriPart.LastIndexOf(quote);
            if (closingQuote > 0)
                return uriPart[1..closingQuote];
        }

        return uriPart;
    }
}
