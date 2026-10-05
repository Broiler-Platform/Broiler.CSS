using System;
using System.Collections.Generic;
using Broiler.Dom;

namespace Broiler.CSS.Dom;

/// <summary>
/// The user-action state of an element written into a document's markup, for a matcher with no state
/// provider to ask: the renderer's, handed a page that a scripting host serialized while someone was
/// using it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why markup.</b> The host that knows what the pointer is over and what has focus -- the HTML
/// bridge running the page's scripts -- hands the renderer the page as markup, and a frame's document as
/// markup inside its frame element's, which the renderer parses again. A provider object does not
/// survive that; an attribute does. The bridge writes <see cref="AttributeName"/> on each element in a
/// user-action state, and a matcher without a provider reads it.
/// </para>
/// <para>
/// <b><c>data-broiler-*</c> is the engine's own namespace</b>, as it is for the scroll offsets, top-layer
/// order and frame documents the layout engine reads from the same markup. A page that writes the
/// attribute itself styles its own elements as hovered or focused, which it could do with any class;
/// a matcher with a provider -- the bridge's own, answering for the live document -- never reads it.
/// </para>
/// </remarks>
public static class CssUserActionStateMarkup
{
    /// <summary>The attribute: the states' pseudo-class names, separated by spaces -- <c>"hover focus focus-visible"</c>.</summary>
    public const string AttributeName = "data-broiler-user-action";

    // ASCII whitespace, which separates the names as it separates a class attribute's.
    private static readonly char[] Whitespace = [' ', '\t', '\n', '\r', '\f'];

    private static readonly (CssUserActionState State, string Name)[] Names =
    [
        (CssUserActionState.Hover, "hover"),
        (CssUserActionState.Active, "active"),
        (CssUserActionState.Focus, "focus"),
        (CssUserActionState.FocusVisible, "focus-visible"),
        (CssUserActionState.FocusWithin, "focus-within"),
    ];

    /// <summary>The attribute value that carries <paramref name="state"/>, or <see langword="null"/> for none.</summary>
    public static string? Format(CssUserActionState state)
    {
        if (state == CssUserActionState.None)
            return null;

        var names = new List<string>(Names.Length);
        foreach (var (flag, name) in Names)
        {
            if ((state & flag) != 0)
                names.Add(name);
        }

        return string.Join(' ', names);
    }

    /// <summary>The state an attribute value carries; names it does not know are ignored.</summary>
    public static CssUserActionState Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return CssUserActionState.None;

        var state = CssUserActionState.None;
        foreach (var token in value.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var (flag, name) in Names)
            {
                if (string.Equals(token, name, StringComparison.Ordinal))
                    state |= flag;
            }
        }

        return state;
    }

    /// <summary>The state <paramref name="element"/>'s markup carries.</summary>
    public static CssUserActionState Read(DomElement element) =>
        Parse(element.GetAttribute(AttributeName));
}
