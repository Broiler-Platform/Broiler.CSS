using System;
using System.Collections.Generic;
using System.Text;

namespace Broiler.CSS.Cssom;

/// <summary>
/// Helpers for validating, parsing, and normalizing CSS Cascade 5 &lt;layer-name&gt; syntax.
/// </summary>
public static class CssLayerNameMetadata
{
    private static readonly HashSet<string> ReservedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "initial",
        "inherit",
        "unset",
        "revert",
        "revert-layer",
        "default",
    };

    /// <summary>
    /// Checks whether <paramref name="name"/> represents an anonymous layer (null, empty, or whitespace-only).
    /// </summary>
    public static bool IsAnonymous(string? name) => string.IsNullOrWhiteSpace(name);

    /// <summary>
    /// Checks whether <paramref name="name"/> is a valid CSS Cascade 5 &lt;layer-name&gt;.
    /// A layer name consists of one or more CSS identifiers separated by dots ('.'),
    /// with no whitespace around the dots. None of the identifier segments may be a
    /// CSS-wide keyword or 'default'.
    /// </summary>
    public static bool IsValidLayerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var segments = GetSegments(name);
        return segments.Length > 0;
    }

    /// <summary>
    /// Checks whether <paramref name="ident"/> is a valid single layer-name identifier segment.
    /// It must be a valid CSS identifier and not a CSS-wide keyword or 'default'.
    /// </summary>
    public static bool IsValidSegment(string? ident)
    {
        if (string.IsNullOrEmpty(ident))
            return false;

        var i = 0;
        if (!TryConsumeIdent(ident, ref i))
            return false;

        if (i != ident.Length)
            return false;

        var unescaped = RendererStyleQueries.UnescapeIdentifier(ident);
        return !ReservedKeywords.Contains(unescaped);
    }

    /// <summary>
    /// Splits a dotted layer name (e.g. "base.reset") into its individual identifier segments.
    /// Returns an empty array if <paramref name="name"/> is not a valid layer name.
    /// </summary>
    public static string[] GetSegments(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return [];

        var text = name.Trim();
        var segments = new List<string>();
        var i = 0;
        var n = text.Length;

        while (i < n)
        {
            var start = i;
            if (!TryConsumeIdent(text, ref i))
                return [];

            var segment = text[start..i];
            var unescaped = RendererStyleQueries.UnescapeIdentifier(segment);
            if (ReservedKeywords.Contains(unescaped))
                return [];

            segments.Add(segment);

            if (i >= n)
                break;

            if (text[i] != '.')
                return [];

            i++; // consume '.'
            if (i >= n)
                return []; // trailing '.'
        }

        return segments.Count > 0 ? segments.ToArray() : [];
    }

    /// <summary>
    /// Normalizes a layer name by unescaping each identifier segment and joining them with dots.
    /// Returns an empty string if the name is anonymous or invalid.
    /// </summary>
    public static string NormalizeLayerName(string? name)
    {
        var segments = GetSegments(name);
        if (segments.Length == 0)
            return string.Empty;

        var builder = new StringBuilder();
        for (var i = 0; i < segments.Length; i++)
        {
            if (i > 0)
                builder.Append('.');
            builder.Append(RendererStyleQueries.UnescapeIdentifier(segments[i]));
        }
        return builder.ToString();
    }

    /// <summary>
    /// Parses an @layer statement prelude (e.g. "reset, base, framework.grid;") into individual layer names.
    /// Returns the array of valid layer names, in declaration order.
    /// </summary>
    public static string[] ParseStatementLayerNames(string? prelude)
    {
        if (string.IsNullOrWhiteSpace(prelude))
            return [];

        var cleaned = CssSyntax.RemoveComments(prelude.Trim().TrimEnd(';'));
        var result = new List<string>();

        foreach (var part in CssSyntax.SplitTopLevel(cleaned, ','))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0)
                continue;

            if (IsValidLayerName(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result.ToArray();
    }

    private static bool TryConsumeIdent(string text, ref int i)
    {
        if (i >= text.Length)
            return false;

        var j = i;
        if (text[j] == '-')
        {
            j++;
            if (j < text.Length && text[j] == '-')
                j++;
            else if (!StartsName(text, j))
                return false;
        }
        else if (!StartsName(text, j))
        {
            return false;
        }

        while (j < text.Length)
        {
            if (CssSyntax.IsValidEscape(text, j))
            {
                ConsumeEscape(text, ref j);
            }
            else if (IsNameChar(text[j]))
            {
                j++;
            }
            else
            {
                break;
            }
        }

        if (j == i)
            return false;

        i = j;
        return true;
    }

    private static void ConsumeEscape(string text, ref int i)
    {
        i++; // skip '\'
        if (i >= text.Length)
            return;

        if (char.IsAsciiHexDigit(text[i]))
        {
            var end = Math.Min(text.Length, i + 6);
            while (i < end && char.IsAsciiHexDigit(text[i]))
                i++;

            if (i < text.Length && IsCssWhitespace(text[i]))
                i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
        }
        else if (text[i] is '\r' or '\n' or '\f')
        {
            i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
        }
        else
        {
            i++;
        }
    }

    private static bool StartsName(string text, int i) =>
        i < text.Length && (IsNameStartChar(text[i]) || CssSyntax.IsValidEscape(text, i));

    private static bool IsNameStartChar(char c) =>
        char.IsLetter(c) || c == '_' || c >= 0x80;

    private static bool IsNameChar(char c) =>
        IsNameStartChar(c) || char.IsDigit(c) || c == '-';

    private static bool IsCssWhitespace(char c) =>
        c is ' ' or '\t' or '\r' or '\n' or '\f';
}
