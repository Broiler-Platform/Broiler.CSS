using System;
using Broiler.Dom;

namespace Broiler.CSS.Dom;

/// <summary>
/// The state of a document that its markup does not hold, for the selectors that match it: a form
/// control's checkedness once it differs from its <c>checked</c> attribute, and what the user is doing
/// to each element.
/// </summary>
/// <remarks>
/// Every member has a default that answers as a document nobody has touched: <see langword="null"/>
/// for a checkedness, which the attribute then answers, and <see cref="CssUserActionState.None"/>
/// for the user's actions, so a provider implements only what it knows.
/// </remarks>
public interface ICssSelectorStateProvider
{
    /// <summary><c>:checked</c>: the element's checkedness, or <see langword="null"/> to have its <c>checked</c> attribute answer.</summary>
    bool? IsChecked(DomElement element) => null;

    /// <summary>
    /// The user-action pseudo-classes <paramref name="element"/> matches (Selectors 4 §9): what the
    /// pointer is over and pressing, and what has focus.
    /// </summary>
    /// <remarks>
    /// Each state is the element's own: <see cref="CssUserActionState.Hover"/> on every element the
    /// pointer is over, its ancestors included, and <see cref="CssUserActionState.FocusWithin"/> on the
    /// focused element and each of its ancestors. The matcher asks about the element it is matching
    /// and nothing more.
    /// </remarks>
    CssUserActionState GetUserActionState(DomElement element) => CssUserActionState.None;
}

/// <summary>What the user is doing to an element, which the user-action pseudo-classes match (Selectors 4 §9).</summary>
[Flags]
public enum CssUserActionState
{
    /// <summary>Nothing: the element matches none of them.</summary>
    None = 0,

    /// <summary><c>:hover</c>: the pointer is over the element, or over one of its descendants.</summary>
    Hover = 1,

    /// <summary><c>:active</c>: the element, or one of its descendants, is being activated -- a pointer button, or a key, is held down on it.</summary>
    Active = 2,

    /// <summary><c>:focus</c>: the element has focus.</summary>
    Focus = 4,

    /// <summary><c>:focus-visible</c>: the element has focus and shows it, as a browser decides it should.</summary>
    FocusVisible = 8,

    /// <summary><c>:focus-within</c>: the element, or one of its descendants, has focus.</summary>
    FocusWithin = 16,
}
