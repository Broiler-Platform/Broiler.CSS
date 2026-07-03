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

    private List<CssRule> ParseRules(string text, int sourceOffset)
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
                rules.Add(ParseAtRule(text, ref position, sourceOffset));
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
            var declarations = ParseDeclarationBlock(text.Substring(blockStart, blockLength), sourceOffset + blockStart);
            rules.Add(new CssStyleRule(
                CssSelectorParser.Parse(selectorText),
                declarations,
                new CssSourceRange(sourceOffset + ruleStart, close - ruleStart + 1)));
            position = close + 1;
        }
        return rules;
    }

    private CssAtRule ParseAtRule(string text, ref int position, int sourceOffset)
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
            nestedRules = ParseRules(blockText, sourceOffset + blockStart);

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
            var raw = part;
            var leading = raw.Length - raw.TrimStart().Length;
            var trimmed = CssSyntax.RemoveComments(raw).Trim();
            if (trimmed.Length == 0)
            {
                declarationStart += raw.Length + 1;
                continue;
            }

            var colon = FindTopLevelColon(trimmed);
            if (colon <= 0)
            {
                AddDiagnostic(
                    "CSS2001",
                    "Malformed declaration was ignored.",
                    CssDiagnosticSeverity.Warning,
                    sourceOffset + declarationStart + leading,
                    trimmed.Length);
                declarationStart += raw.Length + 1;
                continue;
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
                    sourceOffset + declarationStart + leading,
                    trimmed.Length);
                declarationStart += raw.Length + 1;
                continue;
            }

            declarations.Add(new CssDeclaration(
                name.StartsWith("--", StringComparison.Ordinal) ? name : name.ToLowerInvariant(),
                CssValueParser.Parse(valueText),
                important
                ));
            declarationStart += raw.Length + 1;
        }
        return new CssDeclarationBlock(declarations);
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
