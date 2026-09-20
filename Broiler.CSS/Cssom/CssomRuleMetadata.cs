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

/// <summary>The layer an <c>@import</c> puts its sheet in (CSS Cascade 5 §2).</summary>
public enum CssImportLayer
{
    /// <summary>No <c>layer</c> part: the sheet's rules are unlayered.</summary>
    None = 0,

    /// <summary>The bare <c>layer</c> keyword: an anonymous layer.</summary>
    Anonymous = 1,

    /// <summary><c>layer(&lt;layer-name&gt;)</c>, with the name in <see cref="CssImportMetadata.LayerName"/>.</summary>
    Named = 2,
}

/// <summary>
/// Decomposed <c>@import</c> prelude according to CSS Cascade 5 §2:
/// <c>@import [ &lt;url&gt; | &lt;string&gt; ] [ layer | layer(&lt;layer-name&gt;) ]? [ supports(...) ]? &lt;media-query-list&gt;?</c>.
/// </summary>
public readonly record struct CssImportMetadata(
    string Href,
    CssImportLayer Layer,
    string? LayerName,
    string? Supports,
    string Media)
{
    public CssImportMetadata(string href, string media)
        : this(href, CssImportLayer.None, null, null, media)
    {
    }

    public void Deconstruct(out string href, out string media)
    {
        href = Href;
        media = Media;
    }
}

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

    /// <summary>Decomposes an <c>@import</c> prelude into its Cascade 5 parts.</summary>
    public static CssImportMetadata GetImport(CssAtRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return ParseImportPrelude(rule.Prelude);
    }

    /// <summary>
    /// Parses an <c>@import</c> prelude according to CSS Cascade 5 §2:
    /// URL (<c>url(...)</c> or string), optional layer (<c>layer</c> or <c>layer(&lt;name&gt;)</c>),
    /// optional supports condition (<c>supports(...)</c>), and media query list.
    /// </summary>
    public static CssImportMetadata ParseImportPrelude(string prelude)
    {
        if (string.IsNullOrWhiteSpace(prelude))
            return new CssImportMetadata(string.Empty, CssImportLayer.None, null, null, string.Empty);

        var text = prelude.Trim().TrimEnd(';').Trim();
        var i = SkipWhitespaceAndComments(text, 0);

        if (!TryReadImportUrl(text, ref i, out var href))
            return new CssImportMetadata(string.Empty, CssImportLayer.None, null, null, string.Empty);

        i = SkipWhitespaceAndComments(text, i);
        var layer = CssImportLayer.None;
        string? layerName = null;

        if (StartsWithIdent(text, i, "layer"))
        {
            var afterKeyword = i + "layer".Length;
            if (afterKeyword < text.Length && text[afterKeyword] == '(')
            {
                var close = FindMatchingParenthesis(text, afterKeyword);
                if (close >= 0)
                {
                    var inner = text[(afterKeyword + 1)..close];
                    var cleanedName = CssSyntax.RemoveComments(inner).Trim();
                    if (CssLayerNameMetadata.IsValidLayerName(cleanedName))
                    {
                        layer = CssImportLayer.Named;
                        layerName = cleanedName;
                        i = SkipWhitespaceAndComments(text, close + 1);
                    }
                }
            }
            else
            {
                layer = CssImportLayer.Anonymous;
                i = SkipWhitespaceAndComments(text, afterKeyword);
            }
        }

        string? supports = null;
        if (StartsWithIdent(text, i, "supports"))
        {
            var afterSupports = i + "supports".Length;
            if (afterSupports < text.Length && text[afterSupports] == '(')
            {
                var close = FindMatchingParenthesis(text, afterSupports);
                if (close >= 0)
                {
                    supports = text[(afterSupports + 1)..close].Trim();
                    i = SkipWhitespaceAndComments(text, close + 1);
                }
            }
        }

        var media = text[i..].Trim();
        return new CssImportMetadata(href, layer, layerName, supports, media);
    }

    private static bool TryReadImportUrl(string text, ref int i, out string href)
    {
        href = string.Empty;
        if (i < text.Length && (text[i] == '"' || text[i] == '\''))
            return TryReadCssString(text, ref i, out href);

        if (string.Compare(text, i, "url(", 0, 4, StringComparison.OrdinalIgnoreCase) != 0)
            return false;

        var j = i + 4;
        while (j < text.Length && IsCssWhitespace(text[j]))
            j++;

        if (j < text.Length && (text[j] == '"' || text[j] == '\''))
        {
            if (!TryReadCssString(text, ref j, out var quoted))
                return false;
            while (j < text.Length && IsCssWhitespace(text[j]))
                j++;
            if (j >= text.Length || text[j] != ')')
                return false;

            (href, i) = (quoted, j + 1);
            return true;
        }

        var unquoted = new System.Text.StringBuilder();
        while (j < text.Length && text[j] != ')')
        {
            var c = text[j];
            if (IsCssWhitespace(c))
            {
                while (j < text.Length && IsCssWhitespace(text[j]))
                    j++;
                if (j < text.Length && text[j] != ')')
                    return false;
            }
            else if (c is '"' or '\'' or '(' ||
                     c is (>= '\0' and <= '\b') or '\v' or (>= '\u000E' and <= '\u001F') or '\u007F')
            {
                return false;
            }
            else if (c == '\\')
            {
                if (!CssSyntax.IsValidEscape(text, j))
                    return false;
                AppendCssEscape(text, ref j, unquoted);
            }
            else
            {
                unquoted.Append(text[j++]);
            }
        }

        if (j >= text.Length)
            return false;

        (href, i) = (unquoted.ToString(), j + 1);
        return true;
    }

    private static bool TryReadCssString(string text, ref int i, out string value)
    {
        var quote = text[i];
        var decoded = new System.Text.StringBuilder();
        var j = i + 1;
        while (j < text.Length && text[j] is not ('\n' or '\r' or '\f'))
        {
            var c = text[j];
            if (c == quote)
            {
                (value, i) = (decoded.ToString(), j + 1);
                return true;
            }

            if (c == '\\')
                AppendCssEscape(text, ref j, decoded);
            else
                decoded.Append(text[j++]);
        }

        value = string.Empty;
        return false;
    }

    private static void AppendCssEscape(string text, ref int i, System.Text.StringBuilder into)
    {
        i++;
        if (i >= text.Length)
            return;

        if (char.IsAsciiHexDigit(text[i]))
        {
            var codePoint = 0;
            var digitsEnd = Math.Min(text.Length, i + 6);
            while (i < digitsEnd && char.IsAsciiHexDigit(text[i]))
            {
                var digit = text[i++];
                codePoint = (codePoint * 16) + (digit <= '9' ? digit - '0' : (digit | 0x20) - 'a' + 10);
            }

            if (i < text.Length && IsCssWhitespace(text[i]))
                i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;

            into.Append(codePoint == 0 || codePoint > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF
                ? "\uFFFD"
                : char.ConvertFromUtf32(codePoint));
            return;
        }

        if (text[i] is '\n' or '\r' or '\f')
        {
            i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            return;
        }

        into.Append(text[i]);
        i++;
    }

    private static bool StartsWithIdent(string text, int i, string word)
    {
        if (i + word.Length > text.Length ||
            string.Compare(text, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;

        var next = i + word.Length;
        return next == text.Length || !(IsNameChar(text[next]) || text[next] == '\\');
    }

    private static bool IsNameChar(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '-' || c >= 0x80;

    private static bool IsCssWhitespace(char c) =>
        c is ' ' or '\t' or '\r' or '\n' or '\f';

    private static int SkipWhitespaceAndComments(string text, int i)
    {
        while (i < text.Length)
        {
            if (IsCssWhitespace(text[i]))
            {
                i++;
                continue;
            }

            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
            {
                var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? text.Length : close + 2;
                continue;
            }

            break;
        }
        return i;
    }

    private static int FindMatchingParenthesis(string text, int openParen)
    {
        var depth = 0;
        char quote = '\0';
        for (var i = openParen; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == '\\')
                    i++;
                else if (c == quote)
                    quote = '\0';
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }

            if (CssSyntax.IsValidEscape(text, i))
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var commentEnd = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (commentEnd < 0)
                    return -1;
                i = commentEnd + 1;
                continue;
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }
        return -1;
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
