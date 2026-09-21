using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace Broiler.CSS;

public readonly record struct CssSpecificity(int Ids, int Classes, int Types)
    : IComparable<CssSpecificity>
{
    public int Encoded => checked((Ids * 1_000_000) + (Classes * 1_000) + Types);

    public int CompareTo(CssSpecificity other)
    {
        var ids = Ids.CompareTo(other.Ids);
        if (ids != 0)
            return ids;
        var classes = Classes.CompareTo(other.Classes);
        return classes != 0 ? classes : Types.CompareTo(other.Types);
    }

    public static CssSpecificity operator +(CssSpecificity left, CssSpecificity right) =>
        new(left.Ids + right.Ids, left.Classes + right.Classes, left.Types + right.Types);
}

/// <summary>How two compounds of a complex selector are joined (Selectors 4 §15).</summary>
public enum CssCombinator
{
    /// <summary>White space: the left compound matches an ancestor.</summary>
    Descendant,

    /// <summary><c>&gt;</c>: the left compound matches the parent.</summary>
    Child,

    /// <summary><c>+</c>: the left compound matches the immediately preceding sibling.</summary>
    NextSibling,

    /// <summary><c>~</c>: the left compound matches any preceding sibling.</summary>
    SubsequentSibling,

    /// <summary><c>||</c>: the left compound matches the column the element belongs to.</summary>
    Column,
}

/// <summary>
/// One compound selector, given as offsets into the <see cref="CssSelector.Text"/> it was read
/// from rather than as re-serialised pieces, so that a caller rewriting one part of a selector can
/// copy every part it does not touch through byte-identically.
/// </summary>
/// <param name="Start">Offset of the compound's first character.</param>
/// <param name="Length">Its length in characters.</param>
/// <param name="TypeSelectorEnd">
/// Offset just past the leading type selector, including any namespace prefix (<c>ns|type</c>,
/// <c>*|type</c>, <c>|type</c>) and <c>*</c>; equal to <see cref="Start"/> when the compound has
/// no type selector, which is where an added simple selector belongs.
/// </param>
/// <param name="PseudoElementStart">
/// Offset of the compound's <c>::</c>, or <c>-1</c> when it has none. The four legacy one-colon
/// spellings are not reported, which is the same line the selector matcher draws when it strips a
/// pseudo-element off a compound.
/// </param>
public readonly record struct CssCompoundSelector(
    int Start,
    int Length,
    int TypeSelectorEnd,
    int PseudoElementStart)
{
    /// <summary>Offset just past the compound's last character.</summary>
    public int End => Start + Length;
}

public sealed class CssSelector(string text, CssSpecificity specificity)
{
    private CssSelectorStructure? _structure;

    public string Text { get; } = text;

    public CssSpecificity Specificity { get; } = specificity;

    /// <summary>
    /// The compounds of this complex selector, left to right, located in <see cref="Text"/>. A
    /// functional pseudo-class argument is part of the compound it sits in and is not descended
    /// into, so <c>:is(a > b)</c> is one compound.
    /// </summary>
    public IReadOnlyList<CssCompoundSelector> Compounds => Structure.Compounds;

    /// <summary>
    /// The combinators between the compounds: <c>Combinators[i]</c> joins <c>Compounds[i]</c> to
    /// <c>Compounds[i + 1]</c>, so there is always exactly one fewer of them than there are
    /// compounds.
    /// </summary>
    public IReadOnlyList<CssCombinator> Combinators => Structure.Combinators;

    /// <summary>
    /// The subject: the rightmost compound, the one the matched element itself has to satisfy.
    /// Null only for a selector with no compound at all, which takes text that is empty or nothing
    /// but a comment.
    /// </summary>
    public CssCompoundSelector? Subject => Compounds.Count == 0 ? null : Compounds[^1];

    public override string ToString() => Text;

    // Derived on demand and published atomically. The cascade reads Text and Specificity and
    // nothing else, so a parsed sheet should not carry a compound list per selector that no one
    // asks for; two threads that race here compute equal structures, and the loser's is dropped.
    private CssSelectorStructure Structure => Volatile.Read(ref _structure) ?? Analyze();

    private CssSelectorStructure Analyze()
    {
        var structure = CssSelectorParser.AnalyzeStructure(Text);
        return Interlocked.CompareExchange(ref _structure, structure, null) ?? structure;
    }
}

/// <summary>The compounds and combinators of one complex selector, derived together.</summary>
internal sealed class CssSelectorStructure(
    IReadOnlyList<CssCompoundSelector> compounds,
    IReadOnlyList<CssCombinator> combinators)
{
    public IReadOnlyList<CssCompoundSelector> Compounds { get; } = compounds;

    public IReadOnlyList<CssCombinator> Combinators { get; } = combinators;
}

public sealed class CssSelectorList
{
    private readonly ReadOnlyCollection<CssSelector> _selectors;

    /// <summary>
    /// Wraps selectors into a list. Public so that a caller that has rewritten a rule's selectors
    /// can hand them back, which the public <see cref="CssStyleRule"/> constructor asks for and
    /// only <see cref="CssSelectorParser.Parse"/> could supply.
    /// </summary>
    public CssSelectorList(IEnumerable<CssSelector> selectors)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        _selectors = selectors.ToList().AsReadOnly();
    }

    public IReadOnlyList<CssSelector> Selectors => _selectors;
}
