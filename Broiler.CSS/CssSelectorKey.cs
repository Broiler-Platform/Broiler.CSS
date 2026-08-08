using System;

namespace Broiler.CSS;

/// <summary>What kind of simple selector a rule is keyed on. See <see cref="CssSelectorKey"/>.</summary>
public enum CssSelectorKeyKind
{
    /// <summary>No narrowing key — the selector must be tested against every element.</summary>
    Universal,

    /// <summary>The subject element's tag name.</summary>
    Type,

    /// <summary>One class the subject element must carry.</summary>
    Class,

    /// <summary>The subject element's id.</summary>
    Id,
}

/// <summary>
/// A simple selector the subject of a complex selector must satisfy — the bucket a rule is filed
/// under in a cascade rule index. Produced by <see cref="CssSelectorParser.GetKey"/>, whose
/// remarks carry the one-sided contract this rests on.
/// </summary>
/// <param name="Kind">Which kind of key, or <see cref="CssSelectorKeyKind.Universal"/> for none.</param>
/// <param name="Name">
/// The id, class or tag name; empty for <see cref="CssSelectorKeyKind.Universal"/>.
/// </param>
public readonly record struct CssSelectorKey(CssSelectorKeyKind Kind, string Name)
{
    /// <summary>The key of a selector that could match anything.</summary>
    public static CssSelectorKey Universal { get; } = new(CssSelectorKeyKind.Universal, string.Empty);

    public override string ToString() => Kind switch
    {
        CssSelectorKeyKind.Id => "#" + Name,
        CssSelectorKeyKind.Class => "." + Name,
        CssSelectorKeyKind.Type => Name,
        _ => "*",
    };
}
