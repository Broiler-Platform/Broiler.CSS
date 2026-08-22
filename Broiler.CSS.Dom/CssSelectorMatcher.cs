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
                "dir" => argument is not null && MatchesDirectionality(element, argument),
                "open" => IsNamed(element, "details", "dialog") && element.HasAttribute("open"),
                "enabled" => IsFormControl(element) && !element.HasAttribute("disabled"),
                "disabled" => IsFormControl(element) && element.HasAttribute("disabled"),
                "checked" => IsCheckable(element) &&
                    (stateProvider?.IsChecked(element) ?? element.HasAttribute("checked")),
                // HTML §4.10.16.3: the constraint-validation pseudo-classes match only elements
                // that take part in constraint validation at all — so an element barred from it
                // matches NEITHER :valid nor :invalid, which is why each arm tests the candidacy
                // predicate rather than negating the other.
                "valid" => IsConstraintValidationCandidate(element) && !HasConstraintViolation(element),
                "invalid" => IsConstraintValidationCandidate(element) && HasConstraintViolation(element),
                "required" => SupportsRequiredState(element) && IsRequiredControl(element),
                "optional" => SupportsRequiredState(element) && !IsRequiredControl(element),
                // SVG 1.1 §17.1 / SVG 2 §14.1: an SVG <a> is a link through `xlink:href` as well
                // as `href`, and the deprecated attribute still carries the link on its own — WPT
                // svg/linking/reftests/href-a-element-attr-change removes `href` at load and
                // asserts the element keeps its link status, so `a:link rect { fill: lime }` must
                // still match. Testing `href` alone repainted that rect red.
                "link" => IsNamed(element, "a", "area") &&
                    (element.HasAttribute("href") || element.HasAttribute("xlink:href")),
                // Selectors 4 §7.1 pairs :link and :visited, but they are not synonyms —
                // :visited matches a link this user has been to. A static render has no
                // history to consult, and :visited is the one selector a page must never be
                // able to read history through, so the honest and the safe answer are the
                // same one: it never matches. Treating it as :link applied every visited
                // style to every link, which is not a subtle shading difference — the rule
                // comes later in the sheet and wins the cascade, so on www.mediawiki.org
                // every link in the article rendered in the visited purple (#6a60b0) where a
                // browser shows the unvisited blue (#36c).
                "visited" => false,
                // :any-link is the union of the two, and unlike :visited it is knowable: it
                // is any element that IS a hyperlink. It used to fall through to the lenient
                // default below and match every element, not just links.
                "any-link" => IsNamed(element, "a", "area") && element.HasAttribute("href"),
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
        // `:nth-child(An+B of S)` only matches elements that themselves match S, so an
        // element missing from the filtered list never matches. Guarding the lookup is
        // what enforces that: `siblings.Count - (-1)` is a positive position that the
        // An+B test would otherwise happily accept.
        var index = siblings.FindIndex(candidate => ReferenceEquals(candidate, element));
        if (index < 0)
            return false;
        return EvaluateNth(fromEnd ? siblings.Count - index : index + 1, nth);
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

    /// <summary>
    /// <c>:dir(ltr|rtl)</c> — HTML §3.2.6.6 "the directionality of an element".
    /// <para>
    /// Previously unmodelled, so it fell through to the lenient default and matched
    /// <em>every</em> element: <c>:dir(ltr)</c> and <c>:dir(rtl)</c> both applied to the whole
    /// document at once, which is how a single shadow-tree rule painted an entire canvas
    /// (<c>css/css-shadow/shadow-directionality-001/002</c>).
    /// </para>
    /// <para>
    /// The directionality is the document-language one, not the CSS <c>direction</c> property:
    /// it comes from the nearest ancestor-or-self carrying a valid <c>dir</c> attribute, and
    /// defaults to <c>ltr</c> at the root. An argument other than <c>ltr</c>/<c>rtl</c> matches
    /// nothing (Selectors 4 §11.2).
    /// </para>
    /// </summary>
    private static bool MatchesDirectionality(DomElement element, string source)
    {
        var wanted = source.Trim().Trim('"', '\'');
        if (!AsciiEquals(wanted, "ltr") && !AsciiEquals(wanted, "rtl"))
            return false;

        return AsciiEquals(Directionality(element), wanted);
    }

    /// <summary>
    /// The element's directionality: its own <c>dir</c> attribute when valid, else the nearest
    /// ancestor's, else <c>ltr</c>. <c>dir="auto"</c> — and a <c>&lt;bdi&gt;</c> with no valid
    /// <c>dir</c>, whose default it is — resolves from the first strong directional character
    /// of the element's text.
    /// </summary>
    private static string Directionality(DomElement element)
    {
        for (DomElement? current = element; current is not null; current = Parent(current))
        {
            var declared = current.GetAttribute("dir");

            if (declared is not null && AsciiEquals(declared, "ltr"))
                return "ltr";
            if (declared is not null && AsciiEquals(declared, "rtl"))
                return "rtl";

            var isAuto = declared is not null && AsciiEquals(declared, "auto");
            // <bdi> isolates its contents and defaults to auto directionality; an <input> or
            // <textarea> resolves auto from its value rather than its (empty) child text.
            if (isAuto || (declared is null && IsNamed(current, "bdi")))
                return AutoDirectionality(current);
        }

        return "ltr";
    }

    /// <summary>
    /// Auto directionality: <c>rtl</c> when the first strong directional character of the
    /// element's text is right-to-left, <c>ltr</c> otherwise (including "no strong character").
    /// Text inside a descendant that carries its own <c>dir</c>, and inside
    /// <c>&lt;script&gt;</c>/<c>&lt;style&gt;</c>, is skipped, per the HTML algorithm.
    /// </summary>
    private static string AutoDirectionality(DomElement element)
    {
        if (IsNamed(element, "input", "textarea"))
            return StrongDirectionality(element.GetAttribute("value") ?? string.Empty) ?? "ltr";

        return AutoDirectionalityOfChildren(element) ?? "ltr";
    }

    private static string? AutoDirectionalityOfChildren(DomElement element)
    {
        foreach (var child in element.ChildNodes)
        {
            switch (child)
            {
                case DomText text:
                    if (StrongDirectionality(text.Data) is { } found)
                        return found;
                    break;
                case DomElement childElement when
                    !IsNamed(childElement, "script", "style", "textarea", "bdi") &&
                    childElement.GetAttribute("dir") is null:
                    if (AutoDirectionalityOfChildren(childElement) is { } nested)
                        return nested;
                    break;
            }
        }

        return null;
    }

    /// <summary>
    /// The direction of the first strong (L / R / AL) character of <paramref name="text"/>, or
    /// <see langword="null"/> when it has none.
    /// </summary>
    private static string? StrongDirectionality(string text)
    {
        foreach (var character in text)
        {
            if (IsRightToLeft(character))
                return "rtl";
            if (char.IsLetter(character))
                return "ltr";
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="character"/> is strongly right-to-left (Unicode bidi class R or
    /// AL). Approximated by script block rather than by a full bidi-class table, which .NET does
    /// not expose: the RTL scripts occupy contiguous blocks, so the ranges below are exact for
    /// the BMP characters an HTML document can carry in a single UTF-16 unit.
    /// </summary>
    private static bool IsRightToLeft(char character) => character switch
    {
        // Hebrew through Arabic Extended-A — Hebrew, Arabic, Syriac, Thaana, NKo, Samaritan,
        // Mandaic and the Arabic extensions run contiguously with no left-to-right script among
        // them.
        >= '\u0590' and <= '\u08FF' => true,
        // Hebrew and Arabic presentation forms.
        >= '\uFB1D' and <= '\uFDFF' => true,
        >= '\uFE70' and <= '\uFEFF' => true,
        _ => false,
    };

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

    // ───────────────── HTML §4.10.16: constraint validation ─────────────────
    //
    // :valid/:invalid used to fall through to the recognized-but-unmodeled default and so matched
    // EVERY element — including <html> and <body>, whose background propagates to the canvas. A
    // bare `:invalid { background-color: … }` (the idiom WPT's form-validation tests use, with no
    // tag qualifier) therefore painted the whole page instead of the failing control: WPT
    // html/semantics/forms/constraints/form-validation-validity-textarea-defaultValue rendered a
    // fully pink canvas against a reference with four pink boxes on white (issue #1552 problem 21).
    // Matching is a strict narrowing of the lenient default, so it can only remove matches.
    //
    // Every rule below is what the reference browser does, measured on 43 constructed cases.

    /// <summary>The <c>input</c> types barred from constraint validation (HTML §4.10.5.3): they
    /// carry no constraints at all, so they match neither <c>:valid</c> nor <c>:invalid</c>.
    /// <c>submit</c> is deliberately absent — the reference browser reports a submit button
    /// <c>:valid</c>.</summary>
    private static bool IsBarredInputType(DomElement element) =>
        element.GetAttribute("type") is { } type &&
        (AsciiEquals(type, "hidden") || AsciiEquals(type, "reset")
            || AsciiEquals(type, "button") || AsciiEquals(type, "image"));

    /// <summary>The <c>input</c> types that ignore the <c>required</c> attribute, so they are
    /// <c>:optional</c> even when it is present.</summary>
    private static bool IgnoresRequiredAttribute(DomElement element) =>
        element.GetAttribute("type") is { } type &&
        (AsciiEquals(type, "hidden") || AsciiEquals(type, "reset") || AsciiEquals(type, "button")
            || AsciiEquals(type, "image") || AsciiEquals(type, "submit")
            || AsciiEquals(type, "color") || AsciiEquals(type, "range"));

    /// <summary>Whether the element is disabled for validation: its own <c>disabled</c> attribute,
    /// or an ancestor <c>&lt;fieldset disabled&gt;</c>, which disables the controls it contains.
    /// </summary>
    private static bool IsDisabledForValidation(DomElement element)
    {
        if (element.HasAttribute("disabled"))
            return true;

        for (var node = element.ParentNode; node is DomElement ancestor; node = ancestor.ParentNode)
        {
            if (IsNamed(ancestor, "fieldset") && ancestor.HasAttribute("disabled"))
                return true;
        }

        return false;
    }

    /// <summary>
    /// HTML §4.10.16.3: the elements <c>:valid</c>/<c>:invalid</c> can match — a
    /// <c>&lt;form&gt;</c> or <c>&lt;fieldset&gt;</c>, which take their state from the controls
    /// they contain, or a listed control that is a candidate for constraint validation. Disabled
    /// and readonly controls are barred, as are the constraint-free input types.
    /// </summary>
    private static bool IsConstraintValidationCandidate(DomElement element)
    {
        if (IsNamed(element, "form", "fieldset"))
            return true;

        if (!IsFormControl(element))
            return false;

        if (IsDisabledForValidation(element) || element.HasAttribute("readonly"))
            return false;

        return !IsNamed(element, "input") || !IsBarredInputType(element);
    }

    /// <summary>The elements <c>:required</c>/<c>:optional</c> partition — every form control,
    /// whether or not it is a validation candidate: the reference browser reports a barred
    /// <c>&lt;input type=hidden required&gt;</c> and a <c>&lt;button&gt;</c> as <c>:optional</c>.
    /// </summary>
    private static bool SupportsRequiredState(DomElement element) => IsFormControl(element);

    private static bool IsRequiredControl(DomElement element) =>
        element.HasAttribute("required")
        && !IsNamed(element, "button")
        && (!IsNamed(element, "input") || !IgnoresRequiredAttribute(element));

    /// <summary>
    /// Whether the element is suffering from a constraint violation — the <c>:invalid</c> half,
    /// asked only of an element <see cref="IsConstraintValidationCandidate"/> already admitted.
    /// <para>
    /// A <c>&lt;form&gt;</c>/<c>&lt;fieldset&gt;</c> is invalid when any control it contains is;
    /// an empty one is valid. <c>minlength</c>/<c>maxlength</c> are deliberately never consulted:
    /// HTML makes "suffering from being too short/long" conditional on the value having been
    /// <em>edited by the user</em>, and nothing in a static render ever has been — which is
    /// exactly what <c>form-validation-validity-textarea-defaultValue</c> pins, expecting
    /// <c>&lt;textarea minlength=5 required&gt;a&lt;/textarea&gt;</c> to be valid.
    /// </para>
    /// </summary>
    private static bool HasConstraintViolation(DomElement element)
    {
        if (IsNamed(element, "form", "fieldset"))
        {
            return element.Descendants().OfType<DomElement>().Any(descendant =>
                !IsNamed(descendant, "form", "fieldset")
                && IsConstraintValidationCandidate(descendant)
                && HasConstraintViolation(descendant));
        }

        if (IsNamed(element, "button"))
            return false;

        if (IsRequiredControl(element) && IsValueMissing(element))
            return true;

        if (!IsNamed(element, "input"))
            return false;

        var value = element.GetAttribute("value") ?? string.Empty;
        if (value.Length == 0)
            return false;   // an empty value is only ever a `required` violation, handled above

        var type = element.GetAttribute("type") ?? "text";
        if (AsciiEquals(type, "email") && !IsWellFormedEmailAddress(value))
            return true;
        if (AsciiEquals(type, "url") && !Uri.IsWellFormedUriString(value.Trim(), UriKind.Absolute))
            return true;

        if (element.GetAttribute("pattern") is { Length: > 0 } pattern && !MatchesPatternAttribute(value, pattern))
            return true;

        return IsOutOfRange(element, type, value);
    }

    /// <summary>
    /// Whether a required control has no value: the empty string for a text-like control, nothing
    /// checked for a checkbox, no option with a non-empty value for a select.
    /// <para>A radio button is never reported missing — the reference browser leaves an unchecked
    /// required radio <c>:valid</c>, because the state belongs to the radio group rather than to
    /// the one element.</para>
    /// </summary>
    private static bool IsValueMissing(DomElement element)
    {
        if (IsNamed(element, "textarea"))
            return element.TextContent.Length == 0;

        if (IsNamed(element, "select"))
        {
            return !element.Descendants().OfType<DomElement>().Any(option =>
                IsNamed(option, "option")
                && option.HasAttribute("selected")
                && (option.GetAttribute("value") ?? option.TextContent).Length > 0);
        }

        if (!IsNamed(element, "input"))
            return false;

        if (element.GetAttribute("type") is { } type)
        {
            if (AsciiEquals(type, "radio"))
                return false;
            if (AsciiEquals(type, "checkbox"))
                return !element.HasAttribute("checked");
        }

        return (element.GetAttribute("value") ?? string.Empty).Length == 0;
    }

    /// <summary>HTML's <c>pattern</c> attribute is anchored at both ends and matched against the
    /// whole value. A pattern that does not compile is ignored (no violation), per §4.10.5.3.
    /// </summary>
    private static bool MatchesPatternAttribute(string value, string pattern)
    {
        try
        {
            return Regex.IsMatch(value, "^(?:" + pattern + ")$", RegexOptions.None, TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    /// <summary>The <c>min</c>/<c>max</c> underflow/overflow violations, for the numeric input
    /// types whose values this static matcher can compare.</summary>
    private static bool IsOutOfRange(DomElement element, string type, string value)
    {
        if (!AsciiEquals(type, "number") && !AsciiEquals(type, "range"))
            return false;

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            return AsciiEquals(type, "number");   // a non-numeric `type=number` value is a type mismatch

        if (element.GetAttribute("min") is { } min
            && double.TryParse(min, NumberStyles.Float, CultureInfo.InvariantCulture, out double minimum)
            && number < minimum)
            return true;

        return element.GetAttribute("max") is { } max
            && double.TryParse(max, NumberStyles.Float, CultureInfo.InvariantCulture, out double maximum)
            && number > maximum;
    }

    /// <summary>HTML's <c>type=email</c> value sanity check — one <c>@</c> with a non-empty local
    /// part and a dot-bearing domain. Deliberately looser than the spec's production: this decides
    /// a paint colour, and a false <em>violation</em> is the visible failure.</summary>
    private static bool IsWellFormedEmailAddress(string value)
    {
        int at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
            return false;

        var domain = value[(at + 1)..];
        return !domain.StartsWith('.') && !domain.EndsWith('.') && domain.Contains('.');
    }

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
