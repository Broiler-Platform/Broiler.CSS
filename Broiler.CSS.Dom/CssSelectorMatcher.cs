using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Broiler.Dom;

namespace Broiler.CSS.Dom;

public sealed partial class CssSelectorMatcher(ICssSelectorStateProvider? stateProvider = null)
{
    private static readonly char[] AsciiWhitespace = [' ', '\t', '\n', '\r', '\f'];
    private static readonly Regex AttributePattern = AttributeRegex();

    public bool Matches(DomElement element, string selector, DomElement? scope = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (string.IsNullOrWhiteSpace(selector))
            return false;

        if (TrySplitPartSelector(selector.Trim(), out var hostSelector, out var partNames))
            return MatchesPart(element, hostSelector, partNames, scope);

        var parts = SplitParts(selector.Trim());
        if (parts.Count == 0 || !MatchesCompound(element, parts[^1].Compound, scope))
            return false;
        return parts.Count == 1 || MatchBackwards(parts, parts.Count - 2, element, scope);
    }

    private bool MatchBackwards(IReadOnlyList<SelectorPart> parts, int index, DomElement current, DomElement? scope)
    {
        if (index < 0)
            return true;

        var compound = parts[index].Compound;
        switch (parts[index + 1].Combinator)
        {
            case ' ':
                for (var ancestor = Parent(current); ancestor is not null; ancestor = Parent(ancestor))
                {
                    if (MatchesCompound(ancestor, compound, scope) &&
                        MatchBackwards(parts, index - 1, ancestor, scope))
                        return true;
                }
                return false;
            case '>':
                var parent = Parent(current);
                return parent is not null &&
                    MatchesCompound(parent, compound, scope) &&
                    MatchBackwards(parts, index - 1, parent, scope);
            case '+':
                var previous = PreviousElementSibling(current);
                return previous is not null &&
                    MatchesCompound(previous, compound, scope) &&
                    MatchBackwards(parts, index - 1, previous, scope);
            case '~':
                for (var sibling = PreviousElementSibling(current);
                     sibling is not null;
                     sibling = PreviousElementSibling(sibling))
                {
                    if (MatchesCompound(sibling, compound, scope) &&
                        MatchBackwards(parts, index - 1, sibling, scope))
                        return true;
                }
                return false;
            default:
                return false;
        }
    }

    private bool MatchesCompound(DomElement element, string source, DomElement? scope)
    {
        if (source.Length == 0)
            return false;

        var compound = StripPseudoElement(source);

        // Process pseudo-classes BEFORE stripping attributes. A functional pseudo's
        // argument can itself contain an attribute selector (e.g. the `[open]` in
        // `:not([open])`); ProcessPseudoClasses consumes such pseudos whole (ExtractPseudos
        // is bracket-aware and the recursive matcher evaluates the nested `[open]`) and
        // removes them from the compound. Stripping attributes first would instead hoist the
        // nested `[open]` into a top-level *positive* filter and leave an empty `:not()`,
        // inverting `:not([attr])` so it matched elements that HAVE the attribute — which,
        // for the UA `dialog:not([open]){display:none}` rule, hid OPEN dialogs.
        if (!ProcessPseudoClasses(element, ref compound, scope))
            return false;

        var attributes = new List<AttributeFilter>();
        // The attribute regex only ever matches when a '[' is present; skip the
        // Replace (which otherwise scans the whole compound per element) for the
        // common attribute-free selector. Runs on the now pseudo-free compound, so only
        // top-level `[...]` remain.
        if (compound.IndexOf('[') >= 0)
        {
            compound = AttributePattern.Replace(compound, match =>
            {
                attributes.Add(new AttributeFilter(
                    match.Groups["name"].Value.Trim(),
                    match.Groups["op"].Success ? match.Groups["op"].Value : null,
                    match.Groups["value"].Success
                        ? match.Groups["value"].Value.Trim().Trim('"', '\'')
                        : null));
                return string.Empty;
            });
        }

        string? type = null;
        string? id = null;
        var classes = new List<string>();
        for (var index = 0; index < compound.Length;)
        {
            switch (compound[index])
            {
                case '#':
                    id = ReadName(compound, ref index);
                    break;
                case '.':
                    classes.Add(ReadName(compound, ref index));
                    break;
                case '*':
                    index++;
                    break;
                default:
                    if (IsNameStart(compound[index]))
                    {
                        var start = index;
                        index = ConsumeName(compound, index);
                        var candidate = compound[start..index];
                        var pipe = candidate.LastIndexOf('|');
                        type = pipe >= 0 ? candidate[(pipe + 1)..] : candidate;
                    }
                    else
                    {
                        index++;
                    }
                    break;
            }
        }

        if (type is not null && type != "*" &&
            !AsciiEquals(element.LocalName, Unescape(type)))
            return false;
        if (id is not null && !string.Equals(element.Id, Unescape(id), StringComparison.Ordinal))
            return false;

        var elementClasses = new HashSet<string>(
            (element.ClassName ?? string.Empty).Split(AsciiWhitespace, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal);
        if (classes.Any(cssClass => !elementClasses.Contains(Unescape(cssClass))))
            return false;

        return attributes.All(filter => MatchesAttribute(element, filter));
    }

    private bool ProcessPseudoClasses(DomElement element, ref string compound, DomElement? scope)
    {
        var pseudos = ExtractPseudos(compound);
        foreach (var pseudo in pseudos)
        {
            var argument = pseudo.Argument?.Trim();
            var name = pseudo.Name.ToLowerInvariant();
            var matches = name switch
            {
                "first-child" => ElementIndex(element) == 1,
                "last-child" => ElementIndexFromEnd(element) == 1,
                "only-child" => ElementSiblings(element).Count == 1,
                "first-of-type" => TypeIndex(element) == 1,
                "last-of-type" => TypeIndexFromEnd(element) == 1,
                "only-of-type" => TypeSiblings(element).Count == 1,
                "nth-child" => argument is not null && MatchesNth(element, argument, false, false),
                "nth-last-child" => argument is not null && MatchesNth(element, argument, true, false),
                "nth-of-type" => argument is not null && MatchesNth(element, argument, false, true),
                "nth-last-of-type" => argument is not null && MatchesNth(element, argument, true, true),
                "empty" => IsEmpty(element),
                "root" => IsRoot(element),
                "scope" => scope is not null && ReferenceEquals(element, scope),
                "not" => argument is null || !MatchesAny(element, argument, scope),
                "is" or "where" => argument is not null && MatchesAny(element, argument, scope),
                "has" => argument is not null && MatchesHas(element, argument),
                "lang" => argument is not null && MatchesLanguage(element, argument),
                "open" => IsNamed(element, "details", "dialog") && element.HasAttribute("open"),
                "enabled" => IsFormControl(element) && !element.HasAttribute("disabled"),
                "disabled" => IsFormControl(element) && element.HasAttribute("disabled"),
                "checked" => IsCheckable(element) &&
                    (stateProvider?.IsChecked(element) ?? element.HasAttribute("checked")),
                "link" or "visited" => IsNamed(element, "a") && element.HasAttribute("href"),
                // Interactive/user-state pseudo-classes never match in a static
                // render (nothing is focused, hovered, active, or targeted), so a UA
                // rule like `:focus { outline: thin dotted invert }` must not apply
                // to every element.
                "focus" or "focus-visible" or "focus-within"
                    or "hover" or "active"
                    or "target" or "target-within"
                    or "autofill" or "placeholder-shown"
                    or "user-valid" or "user-invalid" => false,
                // A recognized-but-unmodeled pseudo-class (e.g. :read-only,
                // :any-link, :defined, most form-state pseudos) stays lenient —
                // matching as it did before — so this is a strict narrowing.
                // A genuinely UNKNOWN pseudo-class (e.g. :unknownpseudo) is an
                // invalid selector: per the Selectors spec the whole rule must be
                // ignored, so it must NOT match. Treating unknowns as match-all
                // (the old `_ => true`) made the standard WPT "invalid selector is
                // ignored" idiom — `:bogus { background: red }` — paint red on
                // every element (CSS2 cascade/at-import-010 and siblings).
                _ => name.StartsWith('-') || RecognizedPseudoClasses.Contains(name),
            };
            if (!matches)
                return false;
        }

        compound = RemovePseudos(compound, pseudos);
        return true;
    }

    private bool MatchesAny(DomElement element, string selectorList, DomElement? scope)
    {
        foreach (var selector in SplitList(selectorList))
        {
            if (Matches(element, selector, scope))
                return true;
        }
        return false;
    }

    private bool MatchesHas(DomElement element, string selectorList)
    {
        foreach (var selector in SplitList(selectorList))
        {
            var parts = SplitParts(selector);
            if (parts.Count == 0)
                continue;
            foreach (var candidate in RelativeCandidates(element, parts[0]))
            {
                if (MatchRelative(parts, 1, candidate, element))
                    return true;
            }
        }
        return false;
    }

    private bool MatchRelative(IReadOnlyList<SelectorPart> parts, int index, DomElement current, DomElement scope)
    {
        if (index >= parts.Count)
            return true;
        foreach (var candidate in RelativeCandidates(current, parts[index], scope))
        {
            if (MatchRelative(parts, index + 1, candidate, scope))
                return true;
        }
        return false;
    }

    private IEnumerable<DomElement> RelativeCandidates(
        DomElement element,
        SelectorPart part,
        DomElement? scope = null)
    {
        var combinator = part.Combinator == '\0' ? ' ' : part.Combinator;
        IEnumerable<DomElement> candidates = combinator switch
        {
            ' ' => element.Descendants().OfType<DomElement>(),
            '>' => Children(element),
            '+' => NextElementSibling(element) is { } next ? [next] : [],
            '~' => FollowingElementSiblings(element),
            _ => [],
        };
        return candidates.Where(candidate => MatchesCompound(candidate, part.Compound, scope ?? element));
    }

    private bool MatchesNth(DomElement element, string expression, bool fromEnd, bool ofType)
    {
        SplitNthArgument(expression, out var nth, out var filter);
        var siblings = ofType
            ? TypeSiblings(element)
            : ElementSiblings(element);
        if (filter is not null)
            siblings = [.. siblings.Where(candidate => MatchesAny(candidate, filter, null))];
        // `:nth-child(An+B of S)` only matches an element that is itself in the filtered set
        // (Selectors 4 §6.6.5.1) — the index is counted "among the elements that match S". An
        // element filtered out is absent from `siblings`, so FindIndex returns -1, and the
        // from-the-end position must not be computed from it: `Count - (-1)` is Count+1, a
        // positive index that then happily satisfies `odd`/`even`. That is how
        // `:nth-last-child(odd of webkit, fast)` matched <body> — no sibling matches the list, so
        // the empty set yielded position 1 — and the lime background propagated to the canvas.
        var index = siblings.FindIndex(candidate => ReferenceEquals(candidate, element));
        if (index < 0)
            return false;
        var position = fromEnd ? siblings.Count - index : index + 1;
        return EvaluateNth(position, nth);
    }

    private static bool MatchesAttribute(DomElement element, AttributeFilter filter)
    {
        DomAttribute? attribute = element.Attributes.Values
            .Where(candidate =>
                string.Equals(candidate.QualifiedName, filter.Name, StringComparison.OrdinalIgnoreCase))
            .Cast<DomAttribute?>()
            .FirstOrDefault();
        if (attribute is null)
            return false;
        if (filter.Operator is null || filter.Value is null)
            return true;

        var actual = attribute.Value.Value;
        return filter.Operator switch
        {
            "=" => actual == filter.Value,
            "|=" => actual == filter.Value || actual.StartsWith(filter.Value + "-", StringComparison.Ordinal),
            "~=" => actual.Split(AsciiWhitespace, StringSplitOptions.RemoveEmptyEntries).Contains(filter.Value),
            "^=" => actual.StartsWith(filter.Value, StringComparison.Ordinal),
            "$=" => actual.EndsWith(filter.Value, StringComparison.Ordinal),
            "*=" => actual.Contains(filter.Value, StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool MatchesLanguage(DomElement element, string source)
    {
        string? language = null;
        for (DomElement? current = element; current is not null; current = Parent(current))
        {
            language = current.GetAttribute("lang") ??
                current.GetAttributeNS(DomNamespaces.Xml, "lang");
            if (!string.IsNullOrWhiteSpace(language))
                break;
        }
        if (string.IsNullOrWhiteSpace(language))
            return false;

        foreach (var rawRange in SplitList(source))
        {
            var range = rawRange.Trim().Trim('"', '\'');
            if (MatchesLanguageRange(language, range))
                return true;
        }
        return false;
    }

    private static bool MatchesLanguageRange(string language, string range)
    {
        var languageParts = language.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var rangeParts = range.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (rangeParts.Length == 0)
            return false;
        if (!rangeParts.Contains("*", StringComparer.Ordinal))
        {
            return rangeParts.Length <= languageParts.Length &&
                rangeParts.Select((part, index) =>
                    part.Equals(languageParts[index], StringComparison.OrdinalIgnoreCase)).All(static match => match);
        }

        var languageIndex = 0;
        for (var rangeIndex = 0; rangeIndex < rangeParts.Length; rangeIndex++)
        {
            if (rangeParts[rangeIndex] == "*")
            {
                if (rangeIndex + 1 >= rangeParts.Length)
                    return true;
                var next = rangeParts[++rangeIndex];
                while (languageIndex < languageParts.Length &&
                       !languageParts[languageIndex].Equals(next, StringComparison.OrdinalIgnoreCase))
                    languageIndex++;
                if (languageIndex >= languageParts.Length)
                    return false;
                languageIndex++;
                continue;
            }
            if (languageIndex >= languageParts.Length ||
                !languageParts[languageIndex].Equals(rangeParts[rangeIndex], StringComparison.OrdinalIgnoreCase))
                return false;
            languageIndex++;
        }
        return true;
    }

    private static bool IsEmpty(DomElement element) =>
        element.ChildNodes.All(child => child is DomComment || child is DomText text && text.Data.Length == 0);

    private static bool IsRoot(DomElement element) =>
        element.ParentNode is DomDocument || element.ParentNode is DomElement parent && parent.LocalName.StartsWith('#');

    private static List<SelectorPart> SplitParts(string selector)
    {
        selector = NormalizeImpliedDescendantStar(selector);
        var parts = new List<SelectorPart>();
        var current = new StringBuilder();
        var pending = '\0';
        var parentheses = 0;
        var brackets = 0;
        char quote = '\0';

        for (var index = 0; index < selector.Length; index++)
        {
            var character = selector[index];
            if (quote != '\0')
            {
                current.Append(character);
                if (character == '\\' && index + 1 < selector.Length)
                    current.Append(selector[++index]);
                else if (character == quote)
                    quote = '\0';
                continue;
            }
            if (character is '"' or '\'')
            {
                quote = character;
                current.Append(character);
                continue;
            }
            if (character == '(') parentheses++;
            else if (character == ')') parentheses--;
            else if (character == '[') brackets++;
            else if (character == ']') brackets--;

            if (parentheses > 0 || brackets > 0)
            {
                current.Append(character);
                continue;
            }

            if (character is '>' or '+' or '~')
            {
                AddPart(parts, current, pending);
                pending = character;
            }
            else if (char.IsWhiteSpace(character))
            {
                if (current.Length == 0)
                    continue;
                AddPart(parts, current, pending);
                pending = ' ';
                var lookahead = index + 1;
                while (lookahead < selector.Length && char.IsWhiteSpace(selector[lookahead]))
                    lookahead++;
                if (lookahead < selector.Length && selector[lookahead] is '>' or '+' or '~')
                {
                    pending = selector[lookahead];
                    index = lookahead;
                }
            }
            else
            {
                current.Append(character);
            }
        }
        AddPart(parts, current, pending);
        return parts;
    }

    private static void AddPart(List<SelectorPart> parts, StringBuilder current, char combinator)
    {
        var text = current.ToString().Trim();
        if (text.Length > 0)
            parts.Add(new SelectorPart(combinator, text));
        current.Clear();
    }

    private static List<Pseudo> ExtractPseudos(string compound)
    {
        var result = new List<Pseudo>();
        var brackets = 0;
        for (var index = 0; index < compound.Length; index++)
        {
            if (compound[index] == '[') { brackets++; continue; }
            if (compound[index] == ']') { brackets = Math.Max(0, brackets - 1); continue; }
            if (brackets > 0 || compound[index] != ':' ||
                index + 1 < compound.Length && compound[index + 1] == ':')
                continue;

            var nameStart = index + 1;
            var nameEnd = nameStart;
            while (nameEnd < compound.Length &&
                   (char.IsLetter(compound[nameEnd]) || compound[nameEnd] == '-'))
                nameEnd++;
            if (nameEnd == nameStart)
                continue;

            string? argument = null;
            var end = nameEnd;
            if (end < compound.Length && compound[end] == '(')
            {
                var close = FindMatching(compound, end, '(', ')');
                if (close < 0) close = compound.Length - 1;
                argument = compound[(end + 1)..close];
                end = close + 1;
            }
            result.Add(new Pseudo(compound[nameStart..nameEnd], argument, index, end - index));
            index = end - 1;
        }
        return result;
    }

    private static string RemovePseudos(string compound, IReadOnlyList<Pseudo> pseudos)
    {
        var result = new StringBuilder(compound.Length);
        var position = 0;
        foreach (var pseudo in pseudos)
        {
            result.Append(compound, position, pseudo.Start - position);
            position = pseudo.Start + pseudo.Length;
        }
        result.Append(compound, position, compound.Length - position);
        return result.ToString();
    }

    private static IEnumerable<string> SplitList(string source)
    {
        var start = 0;
        var parentheses = 0;
        var brackets = 0;
        char quote = '\0';
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (quote != '\0')
            {
                if (character == '\\') index++;
                else if (character == quote) quote = '\0';
                continue;
            }
            if (character is '"' or '\'') quote = character;
            else if (character == '(') parentheses++;
            else if (character == ')') parentheses--;
            else if (character == '[') brackets++;
            else if (character == ']') brackets--;
            else if (character == ',' && parentheses == 0 && brackets == 0)
            {
                var item = source[start..index].Trim();
                if (item.Length > 0) yield return item;
                start = index + 1;
            }
        }
        var tail = source[start..].Trim();
        if (tail.Length > 0) yield return tail;
    }

    private static void SplitNthArgument(string source, out string nth, out string? selector)
    {
        var lower = source.ToLowerInvariant();
        var depth = 0;
        for (var index = 0; index <= lower.Length - 4; index++)
        {
            if (lower[index] is '(' or '[') depth++;
            else if (lower[index] is ')' or ']') depth--;
            else if (depth == 0 && lower.AsSpan(index, 4).Equals(" of ", StringComparison.Ordinal))
            {
                nth = source[..index].Trim();
                selector = source[(index + 4)..].Trim();
                return;
            }
        }
        nth = source.Trim();
        selector = null;
    }

    private static bool EvaluateNth(int index, string expression)
    {
        var compact = expression.Replace(" ", "").ToLowerInvariant();
        if (compact == "odd") return index % 2 == 1;
        if (compact == "even") return index % 2 == 0;
        if (int.TryParse(compact, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exact))
            return index == exact;
        var n = compact.IndexOf('n');
        if (n < 0)
            return false;
        var aText = compact[..n];
        var a = aText is "" or "+" ? 1 : aText == "-" ? -1 :
            int.TryParse(aText, out var parsedA) ? parsedA : 0;
        var b = int.TryParse(compact[(n + 1)..], out var parsedB) ? parsedB : 0;
        return a == 0
            ? index == b
            : (index - b) % a == 0 && (index - b) / a >= 0;
    }

    private static List<DomElement> ElementSiblings(DomElement element) => element.ParentNode?.ChildNodes.OfType<DomElement>().ToList() ?? [];
    private static List<DomElement> TypeSiblings(DomElement element) => [.. ElementSiblings(element).Where(candidate => AsciiEquals(candidate.LocalName, element.LocalName))];
    private static int ElementIndex(DomElement element) => ElementSiblings(element).FindIndex(candidate => ReferenceEquals(candidate, element)) + 1;
    
    private static int ElementIndexFromEnd(DomElement element)
    {
        var siblings = ElementSiblings(element);
        var index = siblings.FindIndex(candidate => ReferenceEquals(candidate, element));
        return index < 0 ? 0 : siblings.Count - index;
    }
    
    private static int TypeIndex(DomElement element) => TypeSiblings(element).FindIndex(candidate => ReferenceEquals(candidate, element)) + 1;
    
    private static int TypeIndexFromEnd(DomElement element)
    {
        var siblings = TypeSiblings(element);
        var index = siblings.FindIndex(candidate => ReferenceEquals(candidate, element));
        return index < 0 ? 0 : siblings.Count - index;
    }

    private static DomElement? Parent(DomElement element) => element.ParentNode as DomElement;
    private static IEnumerable<DomElement> Children(DomElement element) => element.ChildNodes.OfType<DomElement>();
    
    private static DomElement? PreviousElementSibling(DomElement element)
    {
        for (var node = element.PreviousSibling; node is not null; node = node.PreviousSibling)
            if (node is DomElement sibling) return sibling;
        return null;
    }
    
    private static DomElement? NextElementSibling(DomElement element)
    {
        for (var node = element.NextSibling; node is not null; node = node.NextSibling)
            if (node is DomElement sibling) return sibling;
        return null;
    }
    
    private static IEnumerable<DomElement> FollowingElementSiblings(DomElement element)
    {
        for (var sibling = NextElementSibling(element);
             sibling is not null;
             sibling = NextElementSibling(sibling))
            yield return sibling;
    }

    private static string ReadName(string source, ref int index)
    {
        index++;
        var start = index;
        index = ConsumeName(source, index);
        return source[start..index];
    }
    
    private static int ConsumeName(string source, int index)
    {
        while (index < source.Length)
        {
            if (source[index] == '\\') index = ConsumeEscape(source, index);
            else if (IsNameCharacter(source[index]) || source[index] == '|') index++;
            else break;
        }
        return index;
    }
    
    private static int ConsumeEscape(string source, int index)
    {
        index++;
        var digits = 0;
        while (index < source.Length && digits < 6 && Uri.IsHexDigit(source[index]))
        {
            index++;
            digits++;
        }
        if (digits > 0 && index < source.Length && char.IsWhiteSpace(source[index])) index++;
        else if (digits == 0 && index < source.Length) index++;
        return index;
    }
    
    private static string Unescape(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length;)
        {
            if (value[index] != '\\')
            {
                result.Append(value[index++]);
                continue;
            }
            var start = ++index;
            var digits = 0;
            while (index < value.Length && digits < 6 && Uri.IsHexDigit(value[index]))
            {
                index++;
                digits++;
            }
            if (digits > 0)
            {
                result.Append(char.ConvertFromUtf32(int.Parse(value[start..index], NumberStyles.HexNumber)));
                if (index < value.Length && char.IsWhiteSpace(value[index])) index++;
            }
            else if (index < value.Length)
            {
                result.Append(value[index++]);
            }
        }
        return result.ToString();
    }
    
    private static int FindMatching(string source, int open, char left, char right)
    {
        var depth = 0;
        char quote = '\0';
        for (var index = open; index < source.Length; index++)
        {
            var character = source[index];
            if (quote != '\0')
            {
                if (character == '\\') index++;
                else if (character == quote) quote = '\0';
                continue;
            }
            if (character is '"' or '\'') quote = character;
            else if (character == left) depth++;
            else if (character == right && --depth == 0) return index;
        }
        return -1;
    }
    
    private static string StripPseudoElement(string source)
    {
        var index = source.IndexOf("::", StringComparison.Ordinal);
        return index >= 0 ? source[..index] : source;
    }

    /// <summary>
    /// Splits <c>&lt;host-selector&gt;::part(name…)</c> (CSS Shadow Parts §3). Returns
    /// <see langword="false"/> for every other selector, including one that only has
    /// <c>::part()</c> somewhere other than at the end (<c>::part(x)::before</c> addresses a
    /// pseudo-element of the part, which is not an element this matcher can return).
    /// </summary>
    private static bool TrySplitPartSelector(string selector, out string hostSelector, out string[] partNames)
    {
        hostSelector = string.Empty;
        partNames = [];

        var index = selector.LastIndexOf("::part(", StringComparison.OrdinalIgnoreCase);
        if (index < 0 || !selector.EndsWith(")", StringComparison.Ordinal))
            return false;

        var argumentStart = index + "::part(".Length;
        var argument = selector[argumentStart..^1];
        if (argument.IndexOf(')') >= 0)
            return false; // something follows the ::part() — not a plain part selector

        // The argument is a space-separated <ident>+ list; the element must expose all of them.
        partNames = argument.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (partNames.Length == 0)
            return false;

        hostSelector = selector[..index].TrimEnd();
        return true;
    }

    /// <summary>
    /// Matches <c>&lt;host-selector&gt;::part(name…)</c>: an element inside a shadow tree that
    /// exposes every requested name through its <c>part</c> attribute, whose shadow host matches
    /// the selector before the pseudo-element. A bare <c>::part(name)</c> is
    /// <c>*::part(name)</c> — any host.
    /// </summary>
    /// <remarks>
    /// Unlike the other pseudo-elements this matcher sees, <c>::part()</c> resolves to a real
    /// element whose own box takes the declarations, so it belongs on the ordinary element-matching
    /// path rather than the originating-element one (see
    /// <c>CssStyleEngine.ContainsPseudoElementSelector</c>).
    /// <para>
    /// The host is found by walking up to the nearest shadow root. A consumer that has already
    /// flattened its shadow trees for rendering has no such ancestor left, so the host selector is
    /// then matched against any ancestor instead — the part attribute still identifies the element,
    /// and the alternative would be to stop matching the moment the tree is flattened.
    /// </para>
    /// </remarks>
    private bool MatchesPart(DomElement element, string hostSelector, string[] partNames, DomElement? scope)
    {
        var exposed = (element.GetAttribute("part") ?? string.Empty)
            .Split(AsciiWhitespace, StringSplitOptions.RemoveEmptyEntries);
        if (exposed.Length == 0)
            return false;

        foreach (var required in partNames)
        {
            if (!exposed.Contains(required, StringComparer.Ordinal))
                return false;
        }

        if (hostSelector.Length == 0 || hostSelector == "*")
            return true;

        for (var ancestor = Parent(element); ancestor is not null; ancestor = Parent(ancestor))
        {
            if (IsShadowRoot(ancestor))
                return Parent(ancestor) is { } host && Matches(host, hostSelector, scope);

            // Flattened tree: no shadow root survives, so accept any matching ancestor.
            if (Matches(ancestor, hostSelector, scope))
                return true;
        }

        return false;
    }

    /// <summary>The <c>#shadow-root</c> wrapper element a shadow host carries before a consumer
    /// flattens it for rendering. Checked on both names because a consumer may mint the wrapper
    /// through either.</summary>
    private static bool IsShadowRoot(DomElement element) =>
        string.Equals(element.LocalName, "#shadow-root", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(element.TagName, "#shadow-root", StringComparison.OrdinalIgnoreCase);
    
    private static string NormalizeImpliedDescendantStar(string selector)
    {
        var result = new StringBuilder(selector.Length + 4);
        var brackets = 0;
        var parentheses = 0;
        for (var index = 0; index < selector.Length; index++)
        {
            var character = selector[index];
            if (character == '[') brackets++;
            else if (character == ']') brackets--;
            else if (character == '(') parentheses++;
            else if (character == ')') parentheses--;

            if (character == '*' && index > 0 && brackets == 0 && parentheses == 0)
            {
                var previous = selector[index - 1];
                var compound = index + 1 < selector.Length &&
                    selector[index + 1] is '.' or '#' or '[' or ':';
                if (!compound && (char.IsLetterOrDigit(previous) || previous is '_' or '-'))
                    result.Append(' ');
            }
            result.Append(character);
        }
        return result.ToString();
    }
    private static bool IsNameStart(char character) => char.IsLetter(character) || character is '_' or '-' || character >= 0x80;
    private static bool IsNameCharacter(char character) => IsNameStart(character) || char.IsDigit(character);
    private static bool AsciiEquals(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool IsNamed(DomElement element, params string[] names) => names.Any(name => AsciiEquals(element.LocalName, name));
    private static bool IsFormControl(DomElement element) => IsNamed(element, "input", "button", "select", "textarea");
    private static bool IsCheckable(DomElement element) => IsNamed(element, "input") && element.GetAttribute("type") is { } type && (AsciiEquals(type, "checkbox") || AsciiEquals(type, "radio"));

    // Every pseudo-class name the CSS/Selectors specs define as valid, plus the
    // four legacy single-colon pseudo-elements (:before/:after/:first-line/
    // :first-letter, which reach the pseudo-class switch because only "::" forms
    // are stripped upstream). Names the matcher already models explicitly are
    // included too so the set doubles as the full "is this a real selector"
    // vocabulary. A pseudo-class outside this set (and not vendor-prefixed) is an
    // invalid selector and its rule must be ignored rather than matched.
    private static readonly HashSet<string> RecognizedPseudoClasses = new(StringComparer.Ordinal)
    {
        // Structural
        "root", "empty", "blank", "scope",
        "first-child", "last-child", "only-child",
        "first-of-type", "last-of-type", "only-of-type",
        "nth-child", "nth-last-child", "nth-of-type", "nth-last-of-type",
        "nth-col", "nth-last-col",
        // Logical combinators
        "is", "where", "not", "has", "matches", "any",
        // Linguistic / directionality
        "lang", "dir",
        // Location / link
        "any-link", "link", "visited", "local-link",
        "target", "target-within", "current", "past", "future",
        // User action
        "hover", "active", "focus", "focus-visible", "focus-within",
        // Input / form state
        "enabled", "disabled", "read-only", "read-write", "placeholder-shown",
        "default", "checked", "indeterminate", "valid", "invalid",
        "in-range", "out-of-range", "required", "optional",
        "user-valid", "user-invalid", "autofill",
        // Tree / element identity
        "defined", "host", "host-context", "state",
        // Display / media / top-layer
        "fullscreen", "modal", "picture-in-picture", "popover-open",
        "open", "closed", "playing", "paused", "seeking", "buffering",
        "stalled", "muted", "volume-locked",
        // Legacy single-colon pseudo-elements (kept lenient).
        "before", "after", "first-line", "first-letter",
    };

    private readonly record struct SelectorPart(char Combinator, string Compound);
    private readonly record struct AttributeFilter(string Name, string? Operator, string? Value);
    private readonly record struct Pseudo(string Name, string? Argument, int Start, int Length);

    [GeneratedRegex(@"\[\s*(?<name>[^\s~|^$*=\]]+)\s*(?:(?<op>[~|^$*]?=)\s*(?<value>(?:'[^']*'|""[^""]*""|[^\]\s]+)))?\s*\]", RegexOptions.Compiled)]
    private static partial Regex AttributeRegex();
}
