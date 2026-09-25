using System;
using System.Collections.Generic;

namespace Broiler.CSS.Dom;

/// <summary>How the cascade answers for a part of a selector it does not model as written.</summary>
public enum CssSelectorGapKind
{
    /// <summary>
    /// A pseudo-class the specs define and the matcher does not implement, or any vendor-prefixed
    /// one (<c>:read-only</c>, <c>:-moz-focusring</c>, <c>:-webkit-autofill</c>): it matches EVERY
    /// element, so its rule applies where a browser would apply it only to some.
    /// </summary>
    Guessed,

    /// <summary>
    /// A pseudo-class no spec defines, or one browsers removed (<c>:bogus</c>, <c>:matches()</c>): it
    /// matches nothing. A browser drops the whole rule; the cascade here drops only the selector it is
    /// in, so the rest of a selector list still applies.
    /// </summary>
    Invalid,

    /// <summary>
    /// A pseudo-class that needs a user or a history (<c>:hover</c>, <c>:focus</c>, <c>:visited</c>,
    /// …): nothing is hovered, focused or visited in a still render, so it matches nothing — as in a
    /// browser nobody has touched.
    /// </summary>
    Interactive,

    /// <summary>
    /// A pseudo-class the matcher answers "no" for, where a browser can match it on a page nobody has
    /// touched: <c>:placeholder-shown</c> (an empty field with a placeholder) and <c>:target</c> and
    /// <c>:target-within</c> (the URL's fragment).
    /// </summary>
    NotModeled,

    /// <summary>
    /// A pseudo-element the cascade does not style (<c>::placeholder</c>, <c>::-webkit-scrollbar</c>,
    /// <c>::part()</c>, or a supported one followed by more, such as <c>::before:hover</c>): its rule
    /// reaches nothing.
    /// </summary>
    UnstyledPseudoElement,
}

/// <summary>A part of a selector the cascade does not model as written, and how it answers for it.</summary>
/// <param name="Text">The part as written: <c>:read-only</c>, <c>:host(.dark)</c>, <c>::-webkit-scrollbar</c>.</param>
/// <param name="Kind">How the cascade answers for it.</param>
public readonly record struct CssSelectorGap(string Text, CssSelectorGapKind Kind);

public sealed partial class CssSelectorMatcher
{
    /// <summary>
    /// The parts of <paramref name="selectorList"/> — one selector, or a comma-separated list such as a
    /// style rule's prelude — that the cascade does not model as written, in source order, with how it
    /// answers for each. An empty list means every pseudo-class and pseudo-element in it is modeled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a tool asking which rules of a style sheet a page cannot count on here, without a document
    /// to match against: the answer depends on the selector's text alone. <see cref="TryMatch"/> is the
    /// per-element question; this is the per-rule one, and a <see cref="CssSelectorGapKind.Guessed"/>
    /// part is exactly what makes <see cref="TryMatch"/> decline to answer.
    /// </para>
    /// <para>
    /// The selector arguments of <c>:is()</c>, <c>:where()</c>, <c>:not()</c>, <c>:has()</c>,
    /// <c>:-webkit-any()</c> and the <c>of S</c> of the <c>:nth-*()</c> pseudo-classes are looked into.
    /// A pseudo-element is judged as the cascade judges it: by the text after the selector's last
    /// <c>::</c>, which must be one <see cref="CssStyleEngine.NormalizePseudoElement"/> knows. The
    /// legacy single-colon <c>:before</c>, <c>:after</c>, <c>:first-line</c> and <c>:first-letter</c>
    /// are pseudo-elements the cascade styles. Parts the compound scanner skips without a pseudo-class
    /// (a namespace prefix, an escaped type selector, the column combinator) are not reported.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<CssSelectorGap> DescribeGaps(string? selectorList)
    {
        var gaps = new List<CssSelectorGap>();
        if (!string.IsNullOrWhiteSpace(selectorList))
        {
            foreach (var selector in SplitList(selectorList))
                DescribeGaps(selector, gaps, depth: 0);
        }

        return gaps;
    }

    private static void DescribeGaps(string selector, List<CssSelectorGap> gaps, int depth)
    {
        // Selector arguments nest; a page's selectors do not nest deeply, and a hostile one is cut off.
        if (depth > 32)
            return;

        var pseudoElement = selector.LastIndexOf("::", StringComparison.Ordinal);
        var subject = pseudoElement >= 0 ? selector[..pseudoElement] : selector;
        foreach (var part in SplitParts(subject))
        {
            foreach (var pseudo in ExtractPseudos(part.Compound))
            {
                var name = pseudo.Name.ToLowerInvariant();
                if (ClassifyPseudoClass(name) is { } kind)
                {
                    var text = pseudo.Argument is null ? ":" + pseudo.Name : $":{pseudo.Name}({pseudo.Argument})";
                    gaps.Add(new CssSelectorGap(text, kind));
                    continue;
                }

                foreach (var argument in SelectorArguments(name, pseudo.Argument))
                    DescribeGaps(argument, gaps, depth + 1);
            }
        }

        if (pseudoElement >= 0 && depth == 0)
        {
            var text = selector[pseudoElement..].Trim();
            var normalized = CssStyleEngine.NormalizePseudoElement(text);
            if (normalized is null || !normalized.Equals(text, StringComparison.OrdinalIgnoreCase))
                gaps.Add(new CssSelectorGap(text, CssSelectorGapKind.UnstyledPseudoElement));
        }
        else if (pseudoElement >= 0)
        {
            // A pseudo-element inside a selector argument (`:is(::before)`) is not a selector the
            // matcher answers for at all.
            gaps.Add(new CssSelectorGap(selector[pseudoElement..].Trim(), CssSelectorGapKind.UnstyledPseudoElement));
        }
    }

    /// <summary>The selectors a modeled functional pseudo-class matches its argument against.</summary>
    private static IEnumerable<string> SelectorArguments(string name, string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return [];

        switch (name)
        {
            case "is" or "where" or "not" or "has" or "-webkit-any":
                return SplitList(argument);
            case "nth-child" or "nth-last-child":
            {
                var of = argument.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);
                return of >= 0 ? SplitList(argument[(of + 4)..]) : [];
            }
            default:
                return [];
        }
    }

    /// <summary>
    /// How the pseudo-class switch in <see cref="ProcessPseudoClasses"/> answers for
    /// <paramref name="name"/> (lower-case), or <see langword="null"/> for one it models.
    /// </summary>
    /// <remarks>
    /// This mirrors that switch rather than driving it, so a name given an arm there must leave the
    /// guessed default here too. The mirror is pinned against the matcher's own answers by
    /// <c>CssSelectorGapTests</c>, which asks <see cref="TryMatch"/> about every recognized name.
    /// </remarks>
    internal static CssSelectorGapKind? ClassifyPseudoClass(string name) => name switch
    {
        "first-child" or "last-child" or "only-child" or "first-of-type" or "last-of-type" or "only-of-type"
            or "nth-child" or "nth-last-child" or "nth-of-type" or "nth-last-of-type"
            or "empty" or "root" or "scope" or "not" or "is" or "where" or "-webkit-any" or "has"
            or "lang" or "dir" or "open" or "enabled" or "disabled" or "checked" or "valid" or "invalid"
            or "required" or "optional" or "link" or "any-link" => null,
        // The legacy single-colon pseudo-elements: the cascade styles them as pseudo-elements.
        "before" or "after" or "first-line" or "first-letter" => null,
        "matches" or "any" or "-moz-any" => CssSelectorGapKind.Invalid,
        "hover" or "active" or "focus" or "focus-visible" or "focus-within" or "visited"
            or "autofill" or "user-valid" or "user-invalid" => CssSelectorGapKind.Interactive,
        "target" or "target-within" or "placeholder-shown" => CssSelectorGapKind.NotModeled,
        _ when name.StartsWith('-') || RecognizedPseudoClasses.Contains(name) => CssSelectorGapKind.Guessed,
        _ => CssSelectorGapKind.Invalid,
    };

    /// <summary>Every pseudo-class name the matcher recognizes, for the test that pins the mirror.</summary>
    internal static IReadOnlyCollection<string> RecognizedPseudoClassNames => RecognizedPseudoClasses;
}
