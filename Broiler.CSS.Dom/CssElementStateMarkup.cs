using System;
using System.Collections.Generic;
using Broiler.Dom;

namespace Broiler.CSS.Dom;

/// <summary>
/// The element states of <see cref="CssElementState"/> written into a document's markup, for a matcher
/// with no state provider to ask, as <see cref="CssUserActionStateMarkup"/> writes the user-action
/// states: the renderer's, handed a page a scripting host serialized.
/// </summary>
/// <remarks>
/// <para>
/// The host that knows them -- the HTML bridge, which follows the page's fragment navigations and the
/// user's edits -- writes <see cref="AttributeName"/> on each element in one of them, and a matcher
/// without a provider reads it. A matcher with one never does.
/// </para>
/// <para>
/// <b><see cref="CssElementState.Visited"/> is never carried.</b> <see cref="Format"/> leaves it out and
/// <see cref="Parse"/> ignores it, so a document's markup can never make a link <c>:visited</c>: that
/// answer comes from a renderer's provider, which has the user's history, or from nowhere.
/// </para>
/// </remarks>
public static class CssElementStateMarkup
{
    /// <summary>The attribute: the states' names, separated by spaces -- <c>"user-interacted user-edited"</c>.</summary>
    public const string AttributeName = "data-broiler-state";

    // ASCII whitespace, which separates the names as it separates a class attribute's.
    private static readonly char[] Whitespace = [' ', '\t', '\n', '\r', '\f'];

    private static readonly (CssElementState State, string Name)[] Names =
    [
        (CssElementState.Target, "target"),
        (CssElementState.UserInteracted, "user-interacted"),
        (CssElementState.UserEdited, "user-edited"),
        (CssElementState.PopoverOpen, "popover-open"),
        (CssElementState.Modal, "modal"),
    ];

    /// <summary>The attribute value that carries <paramref name="state"/>, or <see langword="null"/> for none of the states markup carries.</summary>
    public static string? Format(CssElementState state)
    {
        var names = new List<string>(Names.Length);
        foreach (var (flag, name) in Names)
        {
            if ((state & flag) != 0)
                names.Add(name);
        }

        return names.Count == 0 ? null : string.Join(' ', names);
    }

    /// <summary>The states an attribute value carries; names it does not know, <c>visited</c> among them, are ignored.</summary>
    public static CssElementState Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return CssElementState.None;

        var state = CssElementState.None;
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

    /// <summary>The states <paramref name="element"/>'s markup carries.</summary>
    public static CssElementState Read(DomElement element) =>
        Parse(element.GetAttribute(AttributeName));
}
