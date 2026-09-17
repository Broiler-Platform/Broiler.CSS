using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Broiler.CSS.Dom;

/// <summary>
/// User-agent default metadata for HTML elements, shared so bridge/layout
/// consumers no longer keep a private copy that can drift. Exposes the initial
/// <c>display</c> value implied by an element's tag name in the HTML UA stylesheet
/// (<see cref="DisplayValues"/>) and the font-family and white-space declarations of
/// the preformatted-text elements and <c>textarea</c> (<see cref="PropertyValues"/>).
/// </summary>
public static class CssUserAgentDefaults
{
    /// <summary>
    /// Tag name → initial <c>display</c> value from the HTML user-agent
    /// stylesheet. Lookups are case-insensitive. A tag absent from this table has
    /// no UA display default (its display is the CSS initial value, <c>inline</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rule that depends on an attribute, state or media query is not a tag-name default, so
    /// neither <c>[hidden]</c> nor the scripting-only <c>noscript { display: none }</c> is in the
    /// table; hosts apply those.
    /// </para>
    /// <para>
    /// The table departs from the HTML Standard's stylesheet in three places, all tracked in
    /// <c>docs/roadmap.md</c>:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>dialog</c> is <c>none</c>, although the stylesheet's tag-name rule is
    /// <c>dialog { display: block }</c>. A one-value-per-tag table cannot express
    /// <c>dialog:not([open]) { display: none }</c>, so it keeps the closed-dialog value, and an open
    /// dialog's display has to come from the host. This is a known gap, not the spec's default.
    /// </description></item>
    /// <item><description>
    /// <c>basefont</c> and <c>rp</c> are left out of the hidden elements, as in Broiler.HTML's
    /// default stylesheet, so a consumer's computed display agrees with what that renderer paints.
    /// Broiler.Dom.Html does not parse <c>basefont</c> as a void element, so a <c>basefont</c>
    /// contains its following siblings and hiding it would hide them too. Broiler.HTML has no
    /// ruby layout, so it paints the <c>rp</c> parentheses as the only separator between an
    /// <c>rt</c> and its base text.
    /// </description></item>
    /// </list>
    /// </remarks>
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
            ["legend"] = "block",
            ["form"] = "block",
            ["frame"] = "block",
            ["frameset"] = "block",
            ["h1"] = "block",
            ["h2"] = "block",
            ["h3"] = "block",
            ["h4"] = "block",
            ["h5"] = "block",
            ["h6"] = "block",
            ["ol"] = "block",
            ["p"] = "block",
            ["ul"] = "block",
            ["center"] = "block",
            ["dir"] = "block",
            ["menu"] = "block",
            ["pre"] = "block",
            // HTML Rendering, Flow content: listing, plaintext, pre, xmp { display: block }. The HTML
            // Standard tokenizes xmp as RAWTEXT and plaintext as PLAINTEXT, so each holds one text
            // node, which renders as a preformatted block like pre.
            ["listing"] = "block",
            ["plaintext"] = "block",
            ["xmp"] = "block",
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
            // HTML Rendering, Hidden elements hides these without condition. The HTML Standard
            // tokenizes noembed and noframes as RAWTEXT, so their fallback markup is one text node
            // that would otherwise paint as literal text; noframes is hidden outside a frameset too.
            // basefont and rp from the same rule are left out on purpose; see the remarks.
            ["datalist"] = "none",
            ["noembed"] = "none",
            ["noframes"] = "none",
            // Known gap: the stylesheet has dialog { display: block } and dialog:not([open]) { display: none };
            // this table can only hold the closed-dialog value. See the remarks.
            ["dialog"] = "none",
        });

    /// <summary>
    /// Tag name → (property → value) for the HTML user-agent stylesheet's <c>font-family</c> and
    /// <c>white-space</c> declarations on the preformatted-text elements (<c>listing</c>,
    /// <c>plaintext</c>, <c>pre</c>, <c>xmp</c>) and <c>white-space</c> on <c>textarea</c>. Nothing
    /// else from the stylesheet is here. Tag and property lookups are case-insensitive.
    /// <c>display</c> is never here; <see cref="DisplayValues"/> holds it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a small part of the stylesheet, not all of it. It holds the font-family and
    /// white-space declarations of the HTML Standard's preformatted-text rule (Rendering, Flow
    /// content: <c>listing, plaintext, pre, xmp { font-family: monospace; white-space: pre }</c>)
    /// and its form-control rule <c>textarea { white-space: pre-wrap }</c>. The same section's
    /// <c>margin-block: 1em</c> for those four elements is not included, and neither are other
    /// tag-name declarations (such as <c>code, kbd, samp, tt { font-family: monospace }</c> or heading
    /// sizes) or rules that depend on an attribute or state (such as <c>pre[wrap]</c>). A consumer
    /// that feeds this table into its cascade still needs its own margins, and a missing tag or
    /// property says nothing about what the stylesheet sets.
    /// </para>
    /// <para>
    /// The values are declarations, not computed values. <c>font-family</c> and <c>white-space</c>
    /// inherit, so a consumer applies them as <see cref="CssOrigin.UserAgent"/> rules through the
    /// cascade, where descendants inherit them and author rules override them, rather than seeding
    /// them on the matching element alone as a consumer can with <see cref="DisplayValues"/>.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> PropertyValues { get; } =
        new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                // The HTML Standard tokenizes xmp as RAWTEXT and plaintext as PLAINTEXT; listing and
                // pre share their rendering, so all four show their text as written in a monospace font.
                ["listing"] = PreformattedText(),
                ["plaintext"] = PreformattedText(),
                ["pre"] = PreformattedText(),
                ["xmp"] = PreformattedText(),
                // HTML Rendering, Form controls: the control's text keeps its line breaks and still wraps.
                ["textarea"] = Declarations(("white-space", "pre-wrap")),
            });

    // Built by methods rather than a shared static field, so the order of static initializers
    // cannot leave an entry null.
    private static IReadOnlyDictionary<string, string> PreformattedText() =>
        Declarations(("font-family", "monospace"), ("white-space", "pre"));

    private static IReadOnlyDictionary<string, string> Declarations(params (string Property, string Value)[] declarations)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (property, value) in declarations)
            map.Add(property, value);
        return new ReadOnlyDictionary<string, string>(map);
    }
}
