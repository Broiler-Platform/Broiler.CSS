using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.CSS;

public sealed class CssParser
{
    private readonly List<CssDiagnostic> _diagnostics = [];

    public CssStyleSheet ParseStyleSheet(string? source)
    {
        _diagnostics.Clear();
        var text = source ?? string.Empty;
        var rules = ParseRules(text, 0);
        return new CssStyleSheet(rules, _diagnostics);
    }

    public CssDeclarationBlock ParseDeclarations(string? source)
    {
        _diagnostics.Clear();
        return ParseDeclarationBlock(source ?? string.Empty, 0);
    }

    // <paramref name="parentSelector"/> is set when these rules are the body of an
    // enclosing style rule (CSS Nesting Level 1) — either a directly nested rule or one
    // inside a nested conditional group (@media/@supports/...). Each style rule's selector
    // is then desugared against the parent (§"nest-desugaring"); at the top level it is
    // <see langword="null"/> and selectors are absolute.
    private List<CssRule> ParseRules(string text, int sourceOffset, string? parentSelector = null)
    {
        var rules = new List<CssRule>();
        var position = 0;
        while (position < text.Length)
        {
            SkipTrivia(text, ref position);
            if (position >= text.Length)
                break;

            var ruleStart = position;
            if (text[position] == '@')
            {
                rules.Add(ParseAtRule(text, ref position, sourceOffset, parentSelector));
                continue;
            }

            var (Index, Character) = FindTopLevelDelimiter(text, position, '{', ';');
            if (Index < 0 || Character != '{')
            {
                AddDiagnostic(
                    "CSS1001",
                    "Expected a rule block.",
                    CssDiagnosticSeverity.Error,
                    sourceOffset + ruleStart,
                    text.Length - ruleStart);
                break;
            }

            var selectorText = CssSyntax.RemoveComments(text[position..Index]).Trim();
            // CSS Nesting §: a rule nested in a parent style rule (or in a conditional group
            // inside one) has its selector desugared relative to the parent. Top-level rules
            // (parentSelector == null) keep their absolute selector.
            var effectiveSelector = parentSelector is null
                ? selectorText
                : DesugarNestedSelector(selectorText, parentSelector);

            var close = FindClosingBrace(text, Index);
            if (close < 0)
            {
                AddDiagnostic(
                    "CSS1002",
                    "Unterminated style rule.",
                    CssDiagnosticSeverity.Error,
                    sourceOffset + ruleStart,
                    text.Length - ruleStart);
                close = text.Length - 1;
            }

            var blockStart = Index + 1;
            var blockLength = Math.Max(0, close - blockStart);
            var (declarations, nestedRules) = ParseStyleRuleBlock(
                text.Substring(blockStart, blockLength), sourceOffset + blockStart, effectiveSelector);
            rules.Add(new CssStyleRule(
                CssSelectorParser.Parse(effectiveSelector),
                declarations,
                new CssSourceRange(sourceOffset + ruleStart, close - ruleStart + 1)));
            // CSS Nesting §: nested rules desugar to independent flattened rules placed
            // immediately after their parent, so the cascade's document-order tie-break keeps
            // a nested declaration winning over an equally-specific one in the parent block.
            rules.AddRange(nestedRules);
            position = close + 1;
        }
        return rules;
    }

    private CssAtRule ParseAtRule(string text, ref int position, int sourceOffset, string? parentSelector = null)
    {
        var start = position++;
        var nameStart = position;
        while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] is '-' or '_'))
        {
            position++;
        }
        var name = text[nameStart..position].ToLowerInvariant();
        var delimiter = FindTopLevelDelimiter(text, position, '{', ';');
        if (delimiter.Index < 0)
        {
            var prelude = CssSyntax.RemoveComments(text[position..]).Trim();
            AddDiagnostic(
                "CSS1003",
                $"Unterminated @{name} rule.",
                CssDiagnosticSeverity.Error,
                sourceOffset + start,
                text.Length - start);
            position = text.Length;
            return new CssAtRule(
                name,
                prelude,
                null,
                null,
                null,
                new CssSourceRange(sourceOffset + start, text.Length - start));
        }

        var preludeText = CssSyntax.RemoveComments(text[position..delimiter.Index]).Trim();
        if (delimiter.Character == ';')
        {
            position = delimiter.Index + 1;
            return new CssAtRule(
                name,
                preludeText,
                null,
                null,
                null,
                new CssSourceRange(sourceOffset + start, position - start));
        }

        var close = FindClosingBrace(text, delimiter.Index);
        if (close < 0)
        {
            AddDiagnostic(
                "CSS1004",
                $"Unterminated @{name} block.",
                CssDiagnosticSeverity.Error,
                sourceOffset + start,
                text.Length - start);
            close = text.Length - 1;
        }

        var blockStart = delimiter.Index + 1;
        var blockLength = Math.Max(0, close - blockStart);
        var blockText = text.Substring(blockStart, blockLength);
        CssDeclarationBlock? declarations = null;
        List<CssRule>? nestedRules = null;

        if (name is "font-face" or "page" or "property" or "counter-style" or "font-palette-values")
            declarations = ParseDeclarationBlock(blockText, sourceOffset + blockStart);
        else if (name is "media" or "supports" or "layer" or "container" or "scope" or "starting-style" or "keyframes" or "-webkit-keyframes")
            // CSS Nesting §: a conditional group nested in a style rule carries the parent
            // selector down so its inner rules desugar against it (keyframes' percentage
            // selectors have no parent, so parentSelector stays inert there).
            nestedRules = ParseRules(blockText, sourceOffset + blockStart, parentSelector);

        position = close + 1;
        return new CssAtRule(
            name,
            preludeText,
            blockText,
            declarations,
            nestedRules,
            new CssSourceRange(sourceOffset + start, position - start));
    }

    private CssDeclarationBlock ParseDeclarationBlock(string text, int sourceOffset)
    {
        var declarations = new List<CssDeclaration>();
        var declarationStart = 0;
        foreach (var part in CssSyntax.SplitTopLevel(text, ';'))
        {
            AddDeclarationFromSegment(part, sourceOffset + declarationStart, declarations);
            declarationStart += part.Length + 1;
        }
        return new CssDeclarationBlock(declarations);
    }

    // Parses one `name: value` segment (as produced by splitting a declaration block on
    // top-level `;`), appending a valid declaration or recording a diagnostic. Shared by the
    // flat declaration-block path and the nesting-aware style-rule-block path below.
    private void AddDeclarationFromSegment(string raw, int segmentOffset, List<CssDeclaration> declarations)
    {
        var leading = raw.Length - raw.TrimStart().Length;
        var trimmed = CssSyntax.RemoveComments(raw).Trim();
        if (trimmed.Length == 0)
            return;

        var colon = FindTopLevelColon(trimmed);
        if (colon <= 0)
        {
            AddDiagnostic(
                "CSS2001",
                "Malformed declaration was ignored.",
                CssDiagnosticSeverity.Warning,
                segmentOffset + leading,
                trimmed.Length);
            return;
        }

        var name = trimmed[..colon].Trim();
        var valueText = trimmed[(colon + 1)..].Trim();
        var important = TryRemoveImportant(ref valueText);
        if (!IsValidPropertyName(name) || valueText.Length == 0)
        {
            AddDiagnostic(
                "CSS2002",
                "Declaration has an invalid property name or empty value.",
                CssDiagnosticSeverity.Warning,
                segmentOffset + leading,
                trimmed.Length);
            return;
        }

        declarations.Add(new CssDeclaration(
            name.StartsWith("--", StringComparison.Ordinal) ? name : name.ToLowerInvariant(),
            CssValueParser.Parse(valueText),
            important
            ));
    }

    // CSS Nesting Level 1: the body of a style rule may interleave declarations with nested
    // style rules and nested conditional groups (@media/@supports/...). Walks the block at top
    // level, returning the parent's own declarations plus the flattened, selector-desugared
    // nested rules (in source order). <paramref name="parentSelector"/> is the enclosing rule's
    // already-absolute selector, used to desugar each nested selector.
    private (CssDeclarationBlock Declarations, List<CssRule> NestedRules) ParseStyleRuleBlock(
        string text, int sourceOffset, string parentSelector)
    {
        var declarations = new List<CssDeclaration>();
        var nestedRules = new List<CssRule>();
        var position = 0;

        while (position < text.Length)
        {
            SkipTrivia(text, ref position);
            if (position >= text.Length)
                break;

            // Nested conditional group / at-rule (e.g. `@media (...) { ... }`) — parse it with
            // the parent selector so its inner rules desugar, and flatten it out as a sibling.
            if (text[position] == '@')
            {
                nestedRules.Add(ParseAtRule(text, ref position, sourceOffset, parentSelector));
                continue;
            }

            var (delimiterIndex, delimiter) = FindTopLevelDelimiter(text, position, '{', ';');

            // No further `{` or `;`: the remainder is a trailing declaration with no semicolon.
            if (delimiterIndex < 0)
            {
                AddDeclarationFromSegment(text[position..], sourceOffset + position, declarations);
                break;
            }

            // A `;` before any `{` closes a declaration belonging to the parent rule.
            if (delimiter == ';')
            {
                AddDeclarationFromSegment(text[position..delimiterIndex], sourceOffset + position, declarations);
                position = delimiterIndex + 1;
                continue;
            }

            // A `{` first: this segment is a nested style rule. Its prelude is the nested
            // selector list; desugar it against the parent and recurse into its block.
            var prelude = CssSyntax.RemoveComments(text[position..delimiterIndex]).Trim();
            var ruleStart = position;
            var close = FindClosingBrace(text, delimiterIndex);
            if (close < 0)
            {
                AddDiagnostic(
                    "CSS1002",
                    "Unterminated nested style rule.",
                    CssDiagnosticSeverity.Error,
                    sourceOffset + ruleStart,
                    text.Length - ruleStart);
                close = text.Length - 1;
            }

            if (prelude.Length == 0)
            {
                // Empty prelude (e.g. a stray `{ ... }`): skip the block rather than emit an
                // all-matching rule.
                position = close + 1;
                continue;
            }

            var nestedSelector = DesugarNestedSelector(prelude, parentSelector);
            var blockStart = delimiterIndex + 1;
            var blockLength = Math.Max(0, close - blockStart);
            var (childDeclarations, childNested) = ParseStyleRuleBlock(
                text.Substring(blockStart, blockLength), sourceOffset + blockStart, nestedSelector);

            nestedRules.Add(new CssStyleRule(
                CssSelectorParser.Parse(nestedSelector),
                childDeclarations,
                new CssSourceRange(sourceOffset + ruleStart, close - ruleStart + 1)));
            nestedRules.AddRange(childNested);
            position = close + 1;
        }

        return (new CssDeclarationBlock(declarations), nestedRules);
    }

    // CSS Nesting Level 1 §"nest-desugaring": rewrites a nested selector list so it is absolute
    // with respect to <paramref name="parentSelector"/>. Each nested complex selector that
    // contains the nesting selector `&` has it replaced by the parent; one that does not is
    // treated as relative and gets the parent prepended with a descendant combinator (so
    // `.child` under `.parent` becomes `.parent .child`, and `> .child` becomes
    // `.parent > .child`). A multi-selector parent is wrapped in `:is(...)` so it acts as one
    // unit and keeps its combined specificity.
    private static string DesugarNestedSelector(string nestedSelectorList, string parentSelector)
    {
        var parent = parentSelector.Trim();
        var parentIsList = CountTopLevel(parent, ',') > 0;
        var parentRef = parentIsList ? $":is({parent})" : parent;

        var builder = new System.Text.StringBuilder();
        var first = true;
        foreach (var candidate in CssSyntax.SplitTopLevel(nestedSelectorList, ','))
        {
            var nested = candidate.Trim();
            if (nested.Length == 0)
                continue;
            if (!first)
                builder.Append(", ");
            first = false;
            builder.Append(ContainsNestingSelector(nested)
                ? SubstituteNestingSelector(nested, parentRef)
                : $"{parentRef} {nested}");
        }

        return builder.ToString();
    }

    // Whether the selector contains a top-level `&` nesting selector (ignoring any that appear
    // inside a quoted string, e.g. an attribute value).
    private static bool ContainsNestingSelector(string selector)
    {
        char quote = '\0';
        for (var index = 0; index < selector.Length; index++)
        {
            var character = selector[index];
            if (quote != '\0')
            {
                if (character == '\\')
                    index++;
                else if (character == quote)
                    quote = '\0';
                continue;
            }
            if (character is '"' or '\'')
                quote = character;
            else if (character == '&')
                return true;
        }
        return false;
    }

    // Replaces every `&` nesting selector (outside quoted strings) with the parent reference.
    private static string SubstituteNestingSelector(string selector, string parentRef)
    {
        var builder = new System.Text.StringBuilder(selector.Length + parentRef.Length);
        char quote = '\0';
        for (var index = 0; index < selector.Length; index++)
        {
            var character = selector[index];
            if (quote != '\0')
            {
                builder.Append(character);
                if (character == '\\' && index + 1 < selector.Length)
                    builder.Append(selector[++index]);
                else if (character == quote)
                    quote = '\0';
                continue;
            }
            if (character is '"' or '\'')
            {
                quote = character;
                builder.Append(character);
            }
            else if (character == '&')
                builder.Append(parentRef);
            else
                builder.Append(character);
        }
        return builder.ToString();
    }

    // Counts occurrences of a separator at the top level (outside parens/brackets/quotes),
    // used to detect a comma-separated parent selector list.
    private static int CountTopLevel(string text, char separator)
    {
        var count = 0;
        foreach (var _ in CssSyntax.SplitTopLevel(text, separator))
            count++;
        return count - 1;
    }

    private static (int Index, char Character) FindTopLevelDelimiter(string text, int start, params char[] delimiters)
    {
        var parentheses = 0;
        var brackets = 0;
        char quote = '\0';
        for (var index = start; index < text.Length; index++)
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
            if (character == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                var close = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (close < 0)
                    return (-1, '\0');
                index = close + 1;
                continue;
            }

            if (character == '(') parentheses++;
            else if (character == ')') parentheses--;
            else if (character == '[') brackets++;
            else if (character == ']') brackets--;
            else if (parentheses == 0 && brackets == 0 && delimiters.Contains(character))
                return (index, character);
        }
        return (-1, '\0');
    }

    // CSS Syntax §"consume a simple block": the end of the rule's { } block is
    // the matching }, but ( ) and [ ] blocks nested inside are themselves
    // consumed as balanced blocks — so a } (or ]) that is not the mirror of the
    // innermost still-open block is a stray token, not a block terminator.  A
    // brace-only depth count stops at the first } that actually sits inside an
    // unclosed ( … ) or [ … ], splitting one rule into two and letting a later
    // rule (e.g. `body { background: red }`) apply when it should have been
    // swallowed (WPT css/CSS2/fonts/font-family-invalid-characters-002/004).
    private static int FindClosingBrace(string text, int open)
    {
        var stack = new Stack<char>();
        char quote = '\0';
        for (var index = open; index < text.Length; index++)
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
            if (character == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                var commentEnd = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (commentEnd < 0)
                    return -1;
                index = commentEnd + 1;
                continue;
            }

            switch (character)
            {
                case '{':
                case '(':
                case '[':
                    stack.Push(character);
                    break;
                case '}':
                case ')':
                case ']':
                    // Only a closer that mirrors the innermost open block pops it;
                    // a mismatched closer is a stray token and is ignored.  The
                    // stack can only empty by matching the initial '{', so that
                    // position is the rule block's closing brace.
                    if (stack.Count > 0 && Mirror(stack.Peek()) == character)
                    {
                        stack.Pop();
                        if (stack.Count == 0)
                            return index;
                    }
                    break;
            }
        }
        return -1;

        static char Mirror(char opener) => opener switch
        {
            '{' => '}',
            '(' => ')',
            '[' => ']',
            _ => '\0',
        };
    }

    private static int FindTopLevelColon(string text)
    {
        var result = FindTopLevelDelimiter(text, 0, ':');
        return result.Index;
    }

    private static bool TryRemoveImportant(ref string value)
    {
        var bang = value.LastIndexOf('!');
        if (bang < 0)
            return false;
        if (!value[(bang + 1)..].Trim().Equals("important", StringComparison.OrdinalIgnoreCase))
            return false;
        value = value[..bang].TrimEnd();
        return true;
    }

    private static bool IsValidPropertyName(string name)
    {
        if (name.Length == 0)
            return false;
        if (name.StartsWith("--", StringComparison.Ordinal))
            return name.Length > 2;
        return name.All(character =>
            char.IsLetterOrDigit(character) ||
            character is '-' or '_' ||
            character >= 0x80);
    }

    private static void SkipTrivia(string text, ref int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
                continue;
            }
            if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '*')
            {
                var close = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
                position = close < 0 ? text.Length : close + 2;
                continue;
            }
            break;
        }
    }

    private void AddDiagnostic(string code, string message, CssDiagnosticSeverity severity, int start, int length) =>
        _diagnostics.Add(new CssDiagnostic(code, message, severity, new CssSourceRange(start, Math.Max(0, length))));
}
