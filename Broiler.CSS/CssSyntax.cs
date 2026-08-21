using System;
using System.Collections.Generic;
using System.Text;

namespace Broiler.CSS;

public static class CssSyntax
{
    /// <summary>
    /// CSS Syntax §4.3.8 "check if two code points are a valid escape": a <c>\</c> that is
    /// not followed by a newline starts an escape, and the code point after it is part of
    /// the surrounding token rather than a delimiter of its own.
    /// </summary>
    /// <remarks>
    /// Callers scanning raw CSS text for structural characters must consult this before
    /// acting on <c>{</c>, <c>}</c>, <c>;</c>, <c>:</c> and friends. Acid2's parser section
    /// contains <c>error: \};</c>, whose escaped brace closed the rule early and desynced
    /// every rule after it — the whole tail of the stylesheet was silently dropped.
    /// Escapes inside a string are handled by the callers' own quote state.
    /// </remarks>
    public static bool IsValidEscape(string text, int index)
    {
        if (index < 0 || index >= text.Length || text[index] != '\\')
            return false;
        if (index + 1 >= text.Length)
            return false;
        return text[index + 1] is not ('\n' or '\r' or '\f');
    }

    public static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var start = 0;
        var braces = 0;
        var brackets = 0;
        var parentheses = 0;
        char quote = '\0';

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quote != '\0')
            {
                if (character == '\\')
                    index++;
                else if (character == quote)
                    quote = '\0';
                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }
            if (IsValidEscape(text, index))
            {
                index++;
                continue;
            }
            if (character == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                var close = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = close < 0 ? text.Length : close + 1;
                continue;
            }

            switch (character)
            {
                case '{': braces++; break;
                case '}': braces--; break;
                case '[': brackets++; break;
                case ']': brackets--; break;
                case '(': parentheses++; break;
                case ')': parentheses--; break;
            }

            if (character == separator && braces == 0 && brackets == 0 && parentheses == 0)
            {
                yield return text[start..index];
                start = index + 1;
            }
        }

        yield return text[start..];
    }

    public static int FindMatching(string text, int openingIndex, char open, char close)
    {
        var depth = 0;
        char quote = '\0';
        for (var index = openingIndex; index < text.Length; index++)
        {
            var character = text[index];
            if (quote != '\0')
            {
                if (character == '\\')
                    index++;
                else if (character == quote)
                    quote = '\0';
                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }
            if (IsValidEscape(text, index))
            {
                index++;
                continue;
            }
            if (character == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                var commentEnd = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (commentEnd < 0)
                    return text.Length - 1;
                index = commentEnd + 1;
                continue;
            }

            if (character == open)
                depth++;
            else if (character == close && --depth == 0)
                return index;
        }
        return text.Length - 1;
    }

    public static string RemoveComments(string text)
    {
        if (!text.Contains("/*", StringComparison.Ordinal))
            return text;

        var builder = new StringBuilder(text.Length);
        var position = 0;
        while (position < text.Length)
        {
            var start = text.IndexOf("/*", position, StringComparison.Ordinal);
            if (start < 0)
            {
                builder.Append(text, position, text.Length - position);
                break;
            }
            builder.Append(text, position, start - position);
            var end = text.IndexOf("*/", start + 2, StringComparison.Ordinal);
            if (end < 0)
                break;
            position = end + 2;
        }
        return builder.ToString();
    }
}
