using System;

namespace Broiler.CSS.Dom;

/// <summary>
/// Parses and <em>evaluates</em> the prelude of an <c>@supports</c> rule against
/// the CSS <c>&lt;supports-condition&gt;</c> grammar
/// (<see href="https://drafts.csswg.org/css-conditional-3/#at-supports"/>).
///
/// <para>Two questions are answered from the same parse:
/// <list type="bullet">
///   <item><see cref="IsValidCondition"/> — is the prelude a grammatically valid
///   <c>&lt;supports-condition&gt;</c>? A condition that does not parse has a false
///   result and its rules must not apply (the WPT "invalid syntax" tests: a
///   declaration missing its parentheses, <c>and</c>/<c>or</c> mixed at one level
///   without grouping, <c>not not (…)</c>, unbalanced brackets, …).</item>
///   <item><see cref="EvaluatesTrue"/> — does a <em>valid</em> condition evaluate
///   to <c>true</c>, so its rules apply? Feature queries (<c>( &lt;declaration&gt; )</c>)
///   are resolved through a caller-supplied support oracle; a
///   <c>&lt;general-enclosed&gt;</c> (any other balanced group, e.g. <c>(unknown)</c>,
///   <c>(@page)</c>, <c>()</c>, or an unrecognised <c>function(…)</c>) evaluates to
///   <c>false</c> per the grammar — which, importantly, makes <c>not (@page)</c>
///   evaluate to <c>true</c>.</item>
/// </list></para>
///
/// <para>The grammar being parsed:
/// <code>
/// &lt;supports-condition&gt;  = not &lt;supports-in-parens&gt;
///                       | &lt;supports-in-parens&gt; [ and &lt;supports-in-parens&gt; ]*
///                       | &lt;supports-in-parens&gt; [ or  &lt;supports-in-parens&gt; ]*
/// &lt;supports-in-parens&gt; = ( &lt;supports-condition&gt; )
///                       | ( &lt;declaration&gt; )                 -- a feature query
///                       | &lt;general-enclosed&gt;               -- ( any-value? ) | ident( any-value? )
/// </code></para>
/// </summary>
internal static class SupportsConditionSyntax
{
    /// <summary>Tri-state result of parsing a <c>&lt;supports-condition&gt;</c>:
    /// <see cref="Invalid"/> means it does not match the grammar; otherwise it is a
    /// valid condition whose boolean result is <see cref="False"/> or
    /// <see cref="True"/>.</summary>
    private enum Result
    {
        Invalid,
        False,
        True,
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="condition"/> is a grammatically
    /// valid <c>&lt;supports-condition&gt;</c>, independent of whether it evaluates
    /// to true or false. An empty or null prelude, trailing junk, mismatched
    /// brackets, or an illegal combinator sequence all yield <c>false</c>.
    /// </summary>
    public static bool IsValidCondition(string? condition) =>
        Parse(condition, static (_, _) => true) != Result.Invalid;

    /// <summary>
    /// Returns <c>true</c> only when <paramref name="condition"/> is a valid
    /// <c>&lt;supports-condition&gt;</c> that evaluates to <c>true</c> (so the
    /// <c>@supports</c> rule's contents apply). Each feature query
    /// <c>( property: value )</c> is resolved through <paramref name="featureSupported"/>;
    /// a grammatically-invalid condition or one that evaluates false/unknown returns
    /// <c>false</c>.
    /// </summary>
    public static bool EvaluatesTrue(string? condition, Func<string, string, bool> featureSupported) =>
        Parse(condition, featureSupported) == Result.True;

    private static Result Parse(string? condition, Func<string, string, bool> featureSupported)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return Result.Invalid;

        var parser = new Parser(condition, featureSupported);
        var result = parser.ParseCondition();
        if (result == Result.Invalid)
            return Result.Invalid;

        parser.SkipWhitespace();
        // No trailing content is allowed after a complete condition (e.g.
        // "not (x) or (y)" leaves "or (y)" dangling — the `not` form does not
        // continue with a combinator).
        return parser.AtEnd ? result : Result.Invalid;
    }

    private ref struct Parser
    {
        private readonly ReadOnlySpan<char> _s;
        private readonly Func<string, string, bool> _featureSupported;
        private int _i;

        public Parser(ReadOnlySpan<char> s, Func<string, string, bool> featureSupported)
        {
            _s = s;
            _featureSupported = featureSupported;
            _i = 0;
        }

        public readonly bool AtEnd => _i >= _s.Length;

        public void SkipWhitespace()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
                _i++;
        }

        // <supports-condition>
        public Result ParseCondition()
        {
            SkipWhitespace();

            // not <supports-in-parens>
            if (TryMatchKeyword("not"))
            {
                SkipWhitespace();
                return Negate(ParseInParens());
            }

            // <supports-in-parens> [ (and|or) <supports-in-parens> ]*
            var acc = ParseInParens();
            if (acc == Result.Invalid)
                return Result.Invalid;

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
                    return Result.Invalid;

                SkipWhitespace();
                var next = ParseInParens();
                if (next == Result.Invalid)
                    return Result.Invalid;

                acc = combinator == "and" ? And(acc, next) : Or(acc, next);
            }

            return acc;
        }

        // <supports-in-parens>
        private Result ParseInParens()
        {
            SkipWhitespace();
            if (AtEnd)
                return Result.Invalid;

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
                {
                    var name = _s[identStart.._i];
                    if (!ConsumeBalancedGroup(out _, out _))
                        return Result.Invalid;
                    // A recognised boolean function (selector()/font-tech()/
                    // font-format()) is treated as supported; every other
                    // function is <general-enclosed> and evaluates to false.
                    return IsSupportedFunction(name) ? Result.True : Result.False;
                }

                _i = identStart;
                return Result.Invalid;
            }

            // ( <supports-condition> ) | ( <declaration> ) | ( any-value? )
            if (c == '(')
            {
                if (!ConsumeBalancedGroup(out int innerStart, out int innerEnd))
                    return Result.Invalid;
                return EvaluateGroupContent(_s[innerStart..innerEnd]);
            }

            return Result.Invalid;
        }

        // Decides what a balanced parenthesised group's inner content is:
        //   1. a nested <supports-condition> (recursed and required to consume all),
        //   2. a feature query <declaration> (resolved via the support oracle), or
        //   3. otherwise <general-enclosed>, which evaluates to false.
        private readonly Result EvaluateGroupContent(ReadOnlySpan<char> inner)
        {
            // 1. Nested condition, e.g. "((color: green))" or "(not (color: green))".
            //    An inner sequence that parses as a condition but is not fully
            //    consumed (e.g. "not (a) or (b)") is not a valid nested condition —
            //    it falls through to <general-enclosed> below.
            var sub = new Parser(inner, _featureSupported);
            var nested = sub.ParseCondition();
            if (nested != Result.Invalid)
            {
                sub.SkipWhitespace();
                if (sub.AtEnd)
                    return nested;
            }

            // 2. Feature query: ( <property> : <value> ).
            if (TryParseDeclaration(inner, out var property, out var value))
                return _featureSupported(property, value) ? Result.True : Result.False;

            // 3. <general-enclosed> — a balanced group we do not otherwise understand.
            return Result.False;
        }

        // A feature query's inner text is a declaration: <ident> : <non-empty value>.
        // The property name must be a bare identifier (letters/digits/-/_, or a
        // custom-property "--" prefix); anything else (e.g. "@page", "a b") is not a
        // declaration and is left for <general-enclosed> handling.
        private static bool TryParseDeclaration(ReadOnlySpan<char> inner, out string property, out string value)
        {
            property = string.Empty;
            value = string.Empty;

            int colon = inner.IndexOf(':');
            if (colon <= 0)
                return false;

            var name = inner[..colon].Trim();
            var val = inner[(colon + 1)..].Trim();
            if (name.IsEmpty || val.IsEmpty || !IsPropertyName(name))
                return false;

            property = name.ToString();
            value = val.ToString();
            return true;
        }

        private static bool IsPropertyName(ReadOnlySpan<char> name)
        {
            if (!IsIdentStart(name[0]))
                return false;
            foreach (var ch in name)
            {
                if (!IsIdentChar(ch))
                    return false;
            }
            return true;
        }

        private static Result Negate(Result r) => r switch
        {
            Result.True => Result.False,
            Result.False => Result.True,
            _ => Result.Invalid,
        };

        // Both operands are already known non-Invalid when these are called.
        private static Result And(Result a, Result b) =>
            a == Result.False || b == Result.False ? Result.False : Result.True;

        private static Result Or(Result a, Result b) =>
            a == Result.True || b == Result.True ? Result.True : Result.False;

        // Consumes a bracket-balanced group beginning at the current '(' (or the
        // '(' of a function token). On success, sets innerStart/innerEnd to the
        // span between the outermost '(' and its matching ')', advances past the
        // ')', and returns true. Returns false on any unmatched )/]/} or if the
        // input ends before the opening '(' is closed. String literals are skipped
        // so brackets inside them do not affect nesting.
        private bool ConsumeBalancedGroup(out int innerStart, out int innerEnd)
        {
            innerStart = _i + 1; // just past the opening '('
            innerEnd = _i;

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
                    if (depth == 0)
                    {
                        innerEnd = _i; // the matching close of the outermost '('
                        _i++;
                        return true;
                    }
                    _i++;
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

        // CSS Conditional 4 recognises a handful of boolean functions besides the
        // ( <declaration> ) feature query. selector()/font-tech()/font-format() are
        // treated as supported (their argument is well-formed); every other function
        // token is an unknown <general-enclosed> and evaluates to false.
        private static bool IsSupportedFunction(ReadOnlySpan<char> name) =>
            name.Equals("selector", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("font-tech", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("font-format", StringComparison.OrdinalIgnoreCase);

        private static bool IsIdentStart(char c) =>
            char.IsLetter(c) || c == '_' || c == '-' || c >= 0x80;

        private static bool IsIdentChar(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '-' || c >= 0x80;
    }
}
