using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.CSS;

public static class CssSelectorParser
{
    public static CssSelectorList Parse(string? source)
    {
        var selectors = CssSyntax.SplitTopLevel(source ?? string.Empty, ',')
            .Select(static selector => selector.Trim())
            .Where(static selector => selector.Length > 0)
            .Select(static selector => new CssSelector(selector, CalculateSpecificity(selector)));
        return new CssSelectorList(selectors);
    }

    public static CssSpecificity CalculateSpecificity(string? selector)
    {
        var maximum = default(CssSpecificity);
        foreach (var candidate in CssSyntax.SplitTopLevel(selector ?? string.Empty, ','))
        {
            var specificity = CalculateComplexSpecificity(candidate);
            if (specificity.CompareTo(maximum) > 0)
                maximum = specificity;
        }
        return maximum;
    }

    private static CssSpecificity CalculateComplexSpecificity(string selector)
    {
        var total = default(CssSpecificity);
        foreach (var compound in SplitCompounds(selector))
            total += CalculateCompoundSpecificity(compound);
        return total;
    }

    private static CssSpecificity CalculateCompoundSpecificity(string compound)
    {
        var ids = 0;
        var classes = 0;
        var types = 0;
        var index = 0;
        var typeAllowed = true;

        while (index < compound.Length)
        {
            var character = compound[index];
            if (char.IsWhiteSpace(character) || character is '>' or '+' or '~')
            {
                index++;
                typeAllowed = true;
                continue;
            }

            switch (character)
            {
                case '#':
                    ids++;
                    index = ConsumeName(compound, index + 1);
                    typeAllowed = false;
                    break;
                case '.':
                    classes++;
                    index = ConsumeName(compound, index + 1);
                    typeAllowed = false;
                    break;
                case '[':
                    classes++;
                    // An unterminated '[' swallows the rest of the compound: there is no further
                    // simple selector after it, and advancing past the end is what keeps this loop
                    // finite now that a failed search answers -1 rather than an index.
                    var closeBracket = CssSyntax.FindMatching(compound, index, '[', ']');
                    index = closeBracket < 0 ? compound.Length : closeBracket + 1;
                    typeAllowed = false;
                    break;
                case ':':
                    var pseudoElement = index + 1 < compound.Length && compound[index + 1] == ':';
                    index += pseudoElement ? 2 : 1;
                    var nameStart = index;
                    index = ConsumeName(compound, index);
                    var name = compound[nameStart..index].ToLowerInvariant();
                    string? argument = null;
                    if (index < compound.Length && compound[index] == '(')
                    {
                        var close = CssSyntax.FindMatching(compound, index, '(', ')');
                        argument = close < 0 ? compound[(index + 1)..] : compound[(index + 1)..close];
                        index = close < 0 ? compound.Length : close + 1;
                    }

                    if (pseudoElement || name is "before" or "after" or "first-line" or "first-letter")
                    {
                        types++;
                    }
                    else
                    {
                        var pseudoSpecificity = name switch
                        {
                            "where" => default,
                            "is" or "not" or "has" => CalculateSpecificity(argument),
                            "nth-child" or "nth-last-child" =>
                                new CssSpecificity(0, 1, 0) + CalculateSpecificity(ExtractNthOfSelector(argument)),
                            _ => new CssSpecificity(0, 1, 0),
                        };
                        ids += pseudoSpecificity.Ids;
                        classes += pseudoSpecificity.Classes;
                        types += pseudoSpecificity.Types;
                    }
                    typeAllowed = false;
                    break;
                case '*':
                    index++;
                    typeAllowed = false;
                    break;
                case '|':
                    index++;
                    break;
                case '\\':
                    if (typeAllowed)
                        types++;
                    index = ConsumeEscape(compound, index);
                    typeAllowed = false;
                    break;
                default:
                    if (typeAllowed && IsNameStart(character))
                    {
                        types++;
                        index = ConsumeName(compound, index);
                        typeAllowed = false;
                    }
                    else
                    {
                        index++;
                    }
                    break;
            }
        }

        return new CssSpecificity(ids, classes, types);
    }

    /// <summary>
    /// Locates the compounds of one complex selector and the combinators between them, as offsets
    /// into <paramref name="selector"/>. Nothing is re-serialised, because a caller rewriting one
    /// compound needs the parts it leaves alone to come through character for character.
    /// </summary>
    /// <remarks>
    /// A combinator missing a compound on one side — a leading <c>&gt;</c>, as a relative selector
    /// has, or a trailing one in malformed text — is dropped, so the combinator count is always one
    /// less than the compound count. A comment does not separate compounds, since it is not a token:
    /// <c>div/* */.a</c> is one compound whose offsets simply span the comment.
    /// </remarks>
    internal static CssSelectorStructure AnalyzeStructure(string selector)
    {
        var compounds = new List<CssCompoundSelector>();
        var combinators = new List<CssCombinator>();
        var compoundStart = -1;
        CssCombinator? pending = null;
        var index = 0;

        while (index < selector.Length)
        {
            if (selector[index] == '/' && index + 1 < selector.Length && selector[index + 1] == '*')
            {
                var commentEnd = selector.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = commentEnd < 0 ? selector.Length : commentEnd + 2;
                continue;
            }

            var combinator = CombinatorAt(selector, index, out var width);
            if (combinator is { } separator)
            {
                if (compoundStart >= 0)
                {
                    compounds.Add(DescribeCompound(selector, compoundStart, index));
                    compoundStart = -1;
                }

                // An explicit combinator outranks the white space around it, which is why the
                // descendant answer only fills a gap that nothing else has claimed.
                pending = separator == CssCombinator.Descendant ? pending ?? separator : separator;
                index += width;
                continue;
            }

            if (compoundStart < 0)
            {
                compoundStart = index;
                if (compounds.Count > 0)
                    combinators.Add(pending ?? CssCombinator.Descendant);
                pending = null;
            }

            index = SkipCompoundUnit(selector, index);
        }

        if (compoundStart >= 0)
            compounds.Add(DescribeCompound(selector, compoundStart, selector.Length));

        return new CssSelectorStructure(compounds.AsReadOnly(), combinators.AsReadOnly());
    }

    private static CssCombinator? CombinatorAt(string selector, int index, out int width)
    {
        width = 1;
        var character = selector[index];
        if (char.IsWhiteSpace(character))
            return CssCombinator.Descendant;

        switch (character)
        {
            case '>':
                return CssCombinator.Child;
            case '+':
                return CssCombinator.NextSibling;
            case '~':
                return CssCombinator.SubsequentSibling;
            // Doubled, this is the column combinator; alone it is the namespace separator and
            // belongs to the compound it sits in.
            case '|' when index + 1 < selector.Length && selector[index + 1] == '|':
                width = 2;
                return CssCombinator.Column;
            default:
                return null;
        }
    }

    private static CssCompoundSelector DescribeCompound(string selector, int start, int end) =>
        new(start, end - start, TypeSelectorEnd(selector, start, end), PseudoElementStart(selector, start, end));

    private static int TypeSelectorEnd(string selector, int start, int end)
    {
        var index = start;
        if (index < end && selector[index] == '*')
            index++;
        else if (index < end && StartsName(selector[index]))
            index = Math.Min(ConsumeName(selector, index), end);
        else if (index >= end || selector[index] != '|')
            return start;

        // `ns|type`, `*|type` and `|type` are all one type selector. The `=` test keeps an
        // attribute operator out of it, which a compound cannot really lead with but costs a char.
        if (index < end && selector[index] == '|' && (index + 1 >= end || selector[index + 1] != '='))
        {
            index++;
            if (index < end && selector[index] == '*')
                index++;
            else if (index < end && StartsName(selector[index]))
                index = Math.Min(ConsumeName(selector, index), end);
        }

        return index;
    }

    private static int PseudoElementStart(string selector, int start, int end)
    {
        var index = start;
        while (index < end)
        {
            if (selector[index] == ':' && index + 1 < end && selector[index + 1] == ':')
                return index;
            index = SkipCompoundUnit(selector, index);
        }
        return -1;
    }

    /// <summary>
    /// Advances past one unit of a compound. A string, a bracketed or parenthesised group and an
    /// escape are each skipped whole, which is what keeps the <c>~</c> of <c>[a~="b"]</c>, the
    /// <c>+</c> of <c>:nth-child(2n+1)</c> and the space of <c>.a\ b</c> from reading as
    /// combinators.
    /// </summary>
    private static int SkipCompoundUnit(string selector, int index) => selector[index] switch
    {
        '"' or '\'' => SkipString(selector, index),
        '[' => SkipBracketed(selector, index, '[', ']'),
        '(' => SkipBracketed(selector, index, '(', ')'),
        '\\' => ConsumeEscape(selector, index),
        _ => index + 1,
    };

    private static int SkipBracketed(string selector, int index, char open, char close)
    {
        // An unterminated group runs to the end of the selector, which is what FindMatching's -1
        // says has happened.
        var closeIndex = CssSyntax.FindMatching(selector, index, open, close);
        return closeIndex < 0 ? selector.Length : closeIndex + 1;
    }

    private static int SkipString(string selector, int index)
    {
        var quote = selector[index];
        for (var i = index + 1; i < selector.Length; i++)
        {
            if (selector[i] == '\\')
                i++;
            else if (selector[i] == quote)
                return i + 1;
        }
        return selector.Length;
    }

    private static IEnumerable<string> SplitCompounds(string selector)
    {
        var start = 0;
        var bracketDepth = 0;
        var parenthesisDepth = 0;
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
            {
                quote = character;
                continue;
            }
            if (character == '[') bracketDepth++;
            else if (character == ']') bracketDepth--;
            else if (character == '(') parenthesisDepth++;
            else if (character == ')') parenthesisDepth--;
            else if (bracketDepth == 0 && parenthesisDepth == 0 &&
                     (char.IsWhiteSpace(character) || character is '>' or '+' or '~'))
            {
                if (index > start)
                    yield return selector[start..index];
                start = index + 1;
            }
        }

        if (start < selector.Length)
            yield return selector[start..];
    }

    private static string? ExtractNthOfSelector(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return null;

        var lower = argument.ToLowerInvariant();
        var depth = 0;
        for (var index = 0; index <= lower.Length - 4; index++)
        {
            if (lower[index] == '(') depth++;
            else if (lower[index] == ')') depth--;
            else if (depth == 0 &&
                     lower.AsSpan(index, 4).Equals(" of ", StringComparison.Ordinal))
            {
                return argument[(index + 4)..].Trim();
            }
        }
        return null;
    }

    private static int ConsumeName(string text, int index)
    {
        while (index < text.Length)
        {
            if (text[index] == '\\')
            {
                index = ConsumeEscape(text, index);
                continue;
            }
            if (!IsNameCharacter(text[index]))
                break;
            index++;
        }
        return index;
    }

    private static int ConsumeEscape(string text, int index)
    {
        index++;
        var digits = 0;
        while (index < text.Length && digits < 6 && Uri.IsHexDigit(text[index]))
        {
            index++;
            digits++;
        }
        if (digits > 0 && index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
        else if (digits == 0 && index < text.Length)
            index++;
        return index;
    }

    /// <summary>
    /// The <em>key</em> of a complex selector: a simple selector its subject element must carry
    /// for the selector to have any chance of matching. This is what lets a cascade bucket rules
    /// by their rightmost simple selector and test only the handful an element could match,
    /// instead of every rule of every sheet (multithreading roadmap item #11).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key comes from the <em>rightmost</em> compound, because that is the compound the
    /// subject element itself has to satisfy — everything to its left constrains ancestors and
    /// siblings. <c>#main .row > td.cell:hover</c> keys on class <c>cell</c>: an element without
    /// that class cannot match, whatever its ancestry.
    /// </para>
    /// <para>
    /// <b>The contract is one-sided and that is the whole safety argument.</b> A key is a
    /// <em>necessary</em> condition, never a sufficient one — a keyed rule is a candidate that
    /// still goes through the full matcher. So the only way this can be wrong is by returning a
    /// key an element lacks for a selector that would have matched it, and every case that is not
    /// certainly narrower resolves to <see cref="CssSelectorKeyKind.Universal"/>: a bare
    /// <c>:is(…)</c>/<c>:not(…)</c>, an attribute-only compound, a namespaced type, an escaped
    /// identifier, or anything this scanner does not recognise. Losing selectivity costs time;
    /// losing a rule loses the page.
    /// </para>
    /// <para>
    /// Within one compound an id beats a class beats a type, which is simply the order of how
    /// much each one narrows the candidate set.
    /// </para>
    /// </remarks>
    public static CssSelectorKey GetKey(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return CssSelectorKey.Universal;

        string? rightmost = null;
        foreach (var compound in SplitCompounds(selector))
            rightmost = compound;

        return rightmost is null ? CssSelectorKey.Universal : KeyOfCompound(rightmost);
    }

    private static CssSelectorKey KeyOfCompound(string compound)
    {
        // An escaped identifier would have to be unescaped to compare against a DOM value, and a
        // namespaced type (`svg|rect`) is not the plain tag name a bucket is keyed on. Both are
        // rare; neither is worth a subtle bug.
        if (compound.IndexOf('\\') >= 0 || compound.IndexOf('|') >= 0)
            return CssSelectorKey.Universal;

        string? id = null;
        string? className = null;
        string? tag = null;
        var index = 0;

        if (index < compound.Length && IsNameStart(compound[index]))
        {
            var end = ConsumeName(compound, index);
            tag = compound[index..end];
            index = end;
        }
        else if (index < compound.Length && compound[index] == '*')
        {
            index++;
        }

        while (index < compound.Length)
        {
            switch (compound[index])
            {
                case '#':
                {
                    var end = ConsumeName(compound, index + 1);
                    if (end == index + 1)
                        return CssSelectorKey.Universal;
                    id ??= compound[(index + 1)..end];
                    index = end;
                    break;
                }

                case '.':
                {
                    var end = ConsumeName(compound, index + 1);
                    if (end == index + 1)
                        return CssSelectorKey.Universal;
                    className ??= compound[(index + 1)..end];
                    index = end;
                    break;
                }

                case '[':
                {
                    // An attribute filter narrows nothing this index can key on, but it also does
                    // not widen the compound — skip it and keep whatever id/class/type it sits on.
                    var end = SkipBalanced(compound, index, '[', ']');
                    if (end <= index)
                        return CssSelectorKey.Universal;
                    index = end;
                    break;
                }

                case ':':
                {
                    // Same for a pseudo-class or pseudo-element: `td.cell:hover` and
                    // `div::before` still require the td/div they are attached to. A functional
                    // pseudo's argument is skipped wholesale — a key from inside `:not(...)` would
                    // be exactly backwards, and one from inside `:is(...)` holds only if every
                    // branch agrees, which is not worth deciding here.
                    index++;
                    if (index < compound.Length && compound[index] == ':')
                        index++;

                    var end = ConsumeName(compound, index);
                    if (end == index)
                        return CssSelectorKey.Universal;
                    index = end;

                    if (index < compound.Length && compound[index] == '(')
                    {
                        var close = SkipBalanced(compound, index, '(', ')');
                        if (close <= index)
                            return CssSelectorKey.Universal;
                        index = close;
                    }

                    break;
                }

                default:
                    // Something this scanner does not model; assume it could match anything.
                    return CssSelectorKey.Universal;
            }
        }

        if (id is not null)
            return new CssSelectorKey(CssSelectorKeyKind.Id, id);
        if (className is not null)
            return new CssSelectorKey(CssSelectorKeyKind.Class, className);
        if (tag is not null)
            return new CssSelectorKey(CssSelectorKeyKind.Type, tag);

        return CssSelectorKey.Universal;
    }

    /// <summary>Index just past the <paramref name="close"/> matching the <paramref name="open"/>
    /// at <paramref name="index"/>, or <paramref name="index"/> when it is unbalanced.</summary>
    private static int SkipBalanced(string text, int index, char open, char close)
    {
        var depth = 0;
        char quote = '\0';
        for (var i = index; i < text.Length; i++)
        {
            var character = text[i];
            if (quote != '\0')
            {
                if (character == '\\')
                    i++;
                else if (character == quote)
                    quote = '\0';
                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }

            if (character == open)
            {
                depth++;
            }
            else if (character == close)
            {
                depth--;
                if (depth == 0)
                    return i + 1;
            }
        }

        return index;
    }

    private static bool IsNameStart(char character) =>
        char.IsLetter(character) || character is '_' or '-' || character >= 0x80;

    private static bool StartsName(char character) => IsNameStart(character) || character == '\\';

    private static bool IsNameCharacter(char character) =>
        IsNameStart(character) || char.IsDigit(character);
}
