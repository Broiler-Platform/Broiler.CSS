using System;

namespace Broiler.CSS.Dom;

/// <summary>
/// Validates the prelude of an <c>@supports</c> rule against the CSS
/// <c>&lt;supports-condition&gt;</c> grammar
/// (<see href="https://drafts.csswg.org/css-conditional-3/#at-supports"/>).
///
/// <para>Broiler evaluates the <em>truth</em> of a feature query optimistically
/// (any well-formed feature is assumed supported), so this type only decides
/// grammatical <em>validity</em>: a condition that does not parse has a false
/// result and its rules must not apply. That distinction is exactly what the WPT
/// <c>css-conditional</c> "invalid" tests exercise — e.g. a declaration missing
/// its parentheses (<c>@supports color: green</c>), <c>and</c>/<c>or</c> mixed at
/// one level without grouping, or <c>not not (…)</c>.</para>
///
/// <para>The grammar being checked:
/// <code>
/// &lt;supports-condition&gt;  = not &lt;supports-in-parens&gt;
///                       | &lt;supports-in-parens&gt; [ and &lt;supports-in-parens&gt; ]*
///                       | &lt;supports-in-parens&gt; [ or  &lt;supports-in-parens&gt; ]*
/// &lt;supports-in-parens&gt; = ( &lt;supports-condition&gt; )
///                       | ( &lt;declaration&gt; )                 -- a feature query
///                       | &lt;general-enclosed&gt;               -- ( any-value? ) | ident( any-value? )
/// </code>
/// Feature queries and general-enclosed blocks are both accepted as valid
/// <c>&lt;supports-in-parens&gt;</c>, so any balanced parenthesised group counts as
/// valid unless it contains unmatched brackets. Only structurally malformed
/// conditions are rejected.</para>
/// </summary>
internal static class SupportsConditionSyntax
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="condition"/> is a grammatically
    /// valid <c>&lt;supports-condition&gt;</c>. An empty or null prelude, trailing
    /// junk, mismatched brackets, or an illegal combinator sequence all yield
    /// <c>false</c>.
    /// </summary>
    public static bool IsValidCondition(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return false;

        var parser = new Parser(condition);
        if (!parser.ParseCondition())
            return false;

        parser.SkipWhitespace();
        return parser.AtEnd; // no trailing content is allowed after a complete condition
    }

    private ref struct Parser
    {
        private readonly ReadOnlySpan<char> _s;
        private int _i;

        public Parser(ReadOnlySpan<char> s)
        {
            _s = s;
            _i = 0;
        }

        public readonly bool AtEnd => _i >= _s.Length;

        public void SkipWhitespace()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
                _i++;
        }

        // <supports-condition>
        public bool ParseCondition()
        {
            SkipWhitespace();

            // not <supports-in-parens>
            if (TryMatchKeyword("not"))
            {
                SkipWhitespace();
                return ParseInParens();
            }

            // <supports-in-parens> [ (and|or) <supports-in-parens> ]*
            if (!ParseInParens())
                return false;

            string? combinator = null;
            while (true)
            {
                SkipWhitespace();
                if (AtEnd)
                    break;

                string? keyword =
                    TryMatchKeyword("and") ? "and"
                    : TryMatchKeyword("or") ? "or"
                    : null;

                if (keyword is null)
                    break; // leftover content; caller enforces AtEnd and rejects it

                // "and" and "or" cannot be mixed at the same level without parentheses.
                if (combinator is null)
                    combinator = keyword;
                else if (combinator != keyword)
                    return false;

                SkipWhitespace();
                if (!ParseInParens())
                    return false;
            }

            return true;
        }

        // <supports-in-parens>
        private bool ParseInParens()
        {
            SkipWhitespace();
            if (AtEnd)
                return false;

            char c = _s[_i];

            // A bare identifier is only valid here as the function token of a
            // general-enclosed block, i.e. ident( … ). Notably this is what makes
            // "not(x)"/"or(x)" a function rather than a keyword, and rejects a
            // parenthesis-less declaration such as "color: green".
            if (IsIdentStart(c))
            {
                int identStart = _i;
                while (_i < _s.Length && IsIdentChar(_s[_i]))
                    _i++;

                if (_i < _s.Length && _s[_i] == '(')
                    return ConsumeBalancedGroup();

                _i = identStart;
                return false;
            }

            // ( <supports-condition> ) | ( <declaration> ) | ( any-value? )
            // All three collapse to "a balanced parenthesised group" for validity:
            // a malformed inner condition still parses as general-enclosed, unless
            // it contains unmatched brackets (rejected by ConsumeBalancedGroup).
            if (c == '(')
                return ConsumeBalancedGroup();

            return false;
        }

        // Consumes a bracket-balanced group beginning at the current '(' (or the
        // '(' of a function token). Returns false on any unmatched )/]/} or if the
        // input ends before the opening '(' is closed. String literals are skipped
        // so brackets inside them do not affect nesting.
        private bool ConsumeBalancedGroup()
        {
            // Stack of expected closers, most-recent on top. Small and short-lived.
            Span<char> stack = stackalloc char[64];
            int depth = 0;

            while (_i < _s.Length)
            {
                char c = _s[_i];

                if (c is '"' or '\'')
                {
                    if (!SkipString(c))
                        return false;
                    continue;
                }

                if (c is '(' or '[' or '{')
                {
                    if (depth >= stack.Length)
                        return false; // pathologically deep nesting; treat as invalid
                    stack[depth++] = c switch { '(' => ')', '[' => ']', _ => '}' };
                    _i++;
                    continue;
                }

                if (c is ')' or ']' or '}')
                {
                    if (depth == 0 || stack[depth - 1] != c)
                        return false; // unmatched or mismatched closer
                    depth--;
                    _i++;
                    if (depth == 0)
                        return true; // the opening '(' has been closed
                    continue;
                }

                _i++;
            }

            return false; // reached end of input with an unclosed group
        }

        private bool SkipString(char quote)
        {
            _i++; // opening quote
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (c == '\\')
                {
                    _i += 2; // escape sequence
                    continue;
                }
                if (c == quote)
                {
                    _i++;
                    return true;
                }
                if (c is '\n' or '\r')
                    return false; // unterminated string (bad-string)
                _i++;
            }
            return false; // unterminated string
        }

        // Matches a bare "and"/"or"/"not" keyword: the exact letters, case-insensitive,
        // that are NOT immediately followed by '(' (which would make them a function
        // token) and end at a word boundary.
        private bool TryMatchKeyword(string keyword)
        {
            if (_i + keyword.Length > _s.Length)
                return false;

            for (int k = 0; k < keyword.Length; k++)
            {
                if (char.ToLowerInvariant(_s[_i + k]) != keyword[k])
                    return false;
            }

            int after = _i + keyword.Length;
            // Must be followed by whitespace or end-of-input. A following '(' means a
            // function token (e.g. "not(") and any other ident char means a different
            // identifier — neither is the combinator/negation keyword.
            if (after < _s.Length && !char.IsWhiteSpace(_s[after]))
                return false;

            _i = after;
            return true;
        }

        private static bool IsIdentStart(char c) =>
            char.IsLetter(c) || c == '_' || c == '-' || c >= 0x80;

        private static bool IsIdentChar(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '-' || c >= 0x80;
    }
}
