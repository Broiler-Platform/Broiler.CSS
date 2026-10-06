using System;
using Broiler.Dom;

namespace Broiler.CSS.Dom;

/// <summary>
/// The state of a document that its markup does not hold, for the selectors that match it: a form
/// control's checkedness and value once they differ from its markup's, what the user is doing to each
/// element, and the rest of what an element is in a page someone is using (<see cref="CssElementState"/>).
/// </summary>
/// <remarks>
/// Every member has a default that answers as a document nobody has touched: <see langword="null"/>
/// for a checkedness or a value, which the markup then answers, and <see cref="CssUserActionState.None"/>
/// and <see cref="CssElementState.None"/> for the states, so a provider implements only what it knows.
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

    /// <summary>
    /// The states of <paramref name="element"/> that are neither in its markup nor a user action: whether
    /// it is its document's target, whether the user has interacted with it or edited its value, and
    /// whether it is a link to a page the user has visited.
    /// </summary>
    /// <remarks>
    /// <see cref="CssElementState.Visited"/> is for a renderer's provider alone. <c>:visited</c> is how a
    /// page would read the user's history, so a provider that answers a page's own scripts -- its
    /// <c>querySelector</c>, <c>matches</c> and <c>getComputedStyle</c> -- never reports it, and the style
    /// engine applies it only to colours (<see cref="CssStyleEngine.GetCascadedStyle"/>).
    /// </remarks>
    CssElementState GetElementState(DomElement element) => CssElementState.None;

    /// <summary>
    /// A form control's value -- an <c>input</c>'s, a <c>textarea</c>'s, a <c>select</c>'s -- when it is not
    /// the one its markup gives: what the user typed or a script set. <see langword="null"/> to have the
    /// markup answer (the <c>value</c> attribute, the text area's text, the <c>selected</c> option).
    /// </summary>
    /// <remarks>
    /// The value is what <c>:valid</c>, <c>:invalid</c>, <c>:user-valid</c>, <c>:user-invalid</c> and
    /// <c>:placeholder-shown</c> judge.
    /// </remarks>
    string? GetValue(DomElement element) => null;
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

/// <summary>
/// The states of an element in a page someone is using that are neither in its markup nor a user
/// action, which <c>:target</c>, <c>:user-valid</c>, <c>:user-invalid</c>, <c>:visited</c>,
/// <c>:popover-open</c> and <c>:modal</c> match.
/// </summary>
[Flags]
public enum CssElementState
{
    /// <summary>None of them.</summary>
    None = 0,

    /// <summary>
    /// <c>:target</c>: the element is its document's target -- the one the fragment of the URL it was
    /// last navigated to names (HTML's "indicated part of the document").
    /// </summary>
    Target = 1,

    /// <summary>
    /// HTML's user validity: the user has committed a change to the form control, or tried to submit its
    /// form. <c>:user-valid</c> and <c>:user-invalid</c> match only a control in this state.
    /// </summary>
    UserInteracted = 2,

    /// <summary>
    /// The control's value was last changed by the user editing it. Only such a value can be too short
    /// for its <c>minlength</c> or too long for its <c>maxlength</c>.
    /// </summary>
    UserEdited = 4,

    /// <summary>
    /// <c>:visited</c>: the element is a link to a page the user has visited. Never carried in markup;
    /// see <see cref="ICssSelectorStateProvider.GetElementState"/>.
    /// </summary>
    Visited = 8,

    /// <summary>
    /// <c>:popover-open</c>: the element is a popover that is showing (HTML's popover visibility state
    /// "showing"), which only a script or the user's activation of an invoker puts it in.
    /// </summary>
    PopoverOpen = 16,

    /// <summary>
    /// <c>:modal</c>: the element is a dialog open as a modal one -- <c>showModal()</c>, which an
    /// <c>open</c> attribute alone does not do -- or the document's fullscreen element.
    /// </summary>
    Modal = 32,
}
