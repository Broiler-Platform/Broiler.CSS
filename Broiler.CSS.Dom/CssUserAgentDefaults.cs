using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Broiler.CSS.Dom;

/// <summary>
/// User-agent default metadata for HTML elements, shared so bridge/layout
/// consumers no longer keep a private copy that can drift. Currently exposes the
/// initial <c>display</c> value implied by an element's tag name in the HTML UA
/// stylesheet (the value used when no author rule sets <c>display</c>).
/// </summary>
public static class CssUserAgentDefaults
{
    /// <summary>
    /// Tag name → initial <c>display</c> value from the HTML user-agent
    /// stylesheet. Lookups are case-insensitive. A tag absent from this table has
    /// no UA display default (its display is the CSS initial value, <c>inline</c>).
    /// </summary>
    public static IReadOnlyDictionary<string, string> DisplayValues { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["html"] = "block",
            ["address"] = "block",
            ["blockquote"] = "block",
            ["body"] = "block",
            ["dd"] = "block",
            ["div"] = "block",
            ["dl"] = "block",
            ["dt"] = "block",
            ["fieldset"] = "block",
            ["form"] = "block",
            ["frame"] = "block",
            ["frameset"] = "block",
            ["h1"] = "block",
            ["h2"] = "block",
            ["h3"] = "block",
            ["h4"] = "block",
            ["h5"] = "block",
            ["h6"] = "block",
            ["noframes"] = "block",
            ["ol"] = "block",
            ["p"] = "block",
            ["ul"] = "block",
            ["center"] = "block",
            ["dir"] = "block",
            ["menu"] = "block",
            ["pre"] = "block",
            ["section"] = "block",
            ["article"] = "block",
            ["nav"] = "block",
            ["aside"] = "block",
            ["header"] = "block",
            ["footer"] = "block",
            ["main"] = "block",
            ["figure"] = "block",
            ["figcaption"] = "block",
            ["details"] = "block",
            ["li"] = "list-item",
            ["summary"] = "list-item",
            ["table"] = "table",
            ["tr"] = "table-row",
            ["thead"] = "table-header-group",
            ["tbody"] = "table-row-group",
            ["tfoot"] = "table-footer-group",
            ["col"] = "table-column",
            ["colgroup"] = "table-column-group",
            ["td"] = "table-cell",
            ["th"] = "table-cell",
            ["caption"] = "table-caption",
            ["button"] = "inline-block",
            ["textarea"] = "inline-block",
            ["input"] = "inline-block",
            ["select"] = "inline-block",
            ["iframe"] = "inline-block",
            ["object"] = "inline-block",
            ["head"] = "none",
            ["style"] = "none",
            ["title"] = "none",
            ["script"] = "none",
            ["link"] = "none",
            ["meta"] = "none",
            ["area"] = "none",
            ["base"] = "none",
            ["param"] = "none",
            ["template"] = "none",
            ["dialog"] = "none",
        });
}
