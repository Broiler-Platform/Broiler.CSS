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
                    index = CssSyntax.FindMatching(compound, index, '[', ']') + 1;
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
                        argument = close > index ? compound[(index + 1)..close] : compound[(index + 1)..];
                        index = close >= index ? close + 1 : compound.Length;
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

    private static bool IsNameCharacter(char character) =>
        IsNameStart(character) || char.IsDigit(character);
}
