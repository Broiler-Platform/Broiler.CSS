using System;
using System.Collections.Generic;

namespace Broiler.CSS.Dom;

/// <summary>
/// What a stylesheet set can see of an element's attributes: every attribute name any selector
/// filters on, every class name and id any selector names, and every attribute any declaration
/// reads through <c>attr()</c>. The question it answers is the one a DOM mutation alone cannot —
/// <em>could any rule match differently if this attribute changed?</em> — and it is the second
/// half of multithreading roadmap item #14.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> Item #14's first slice elides a rebuild when the mutated node does not
/// hang off the bound document. Everything connected still rebuilds, which leaves the case the
/// relayout harness sizes at <b>997.8 ms on the rule-heavy corpus page</b>: one <c>data-*</c> write
/// on a connected element that no selector in the document can reach. A full box-tree rebuild and a
/// whole-document re-cascade, to produce the page that was already on screen.
/// </para>
/// <para>
/// <b>The rule, and why it holds under every combinator.</b> An attribute write can only change what
/// a selector matches if the selector mentions that attribute <em>somewhere</em> — not necessarily in
/// its subject compound. <c>.a .b</c> is affected by a class write on an ancestor; <c>[hidden] + p</c>
/// by one on a sibling; <c>:has([open])</c> by one on a descendant. So this set is built by scanning
/// the <em>whole</em> selector text, every compound and the inside of every functional pseudo, and
/// the test is pure membership: a name that appears nowhere in any selector cannot change any match,
/// whatever the combinators between it and the subject. That is why this is sound without modelling
/// combinators at all — which is also why it is coarser than a browser's invalidation set, and
/// deliberately so.
/// </para>
/// <para>
/// <b>Three sources, not one.</b> Selectors are the obvious one. <c>attr()</c> is the one that is
/// easy to miss: <c>content: attr(data-label)</c> and
/// <c>width: attr(data-w type(&lt;length&gt;))</c> make a <c>data-*</c> write change the rendering
/// with no selector mentioning it anywhere, so declaration values are scanned too. The third is the
/// <see cref="IsConservative"/> escape hatch below.
/// </para>
/// <para>
/// <b>Every uncertainty resolves to "affects".</b> The scanner is a scanner, not the selector
/// engine: an escape sequence, a namespaced attribute (<c>[xlink|href]</c>), an unbalanced bracket
/// or anything else it does not model makes the whole set
/// <see cref="IsConservative"/>, and a conservative set answers <see langword="true"/> for every
/// attribute — which is the behaviour that was there before it existed. A set that is wrong by
/// including too much costs a rebuild; one that is wrong by omitting a name renders a stale page.
/// </para>
/// <para>
/// <b>Case.</b> Membership is <see cref="StringComparer.OrdinalIgnoreCase"/> throughout. HTML
/// attribute names are ASCII case-insensitive, so that is exact for them; class names and ids are
/// case-<em>sensitive</em> in standards mode and case-insensitive in quirks, so ignoring case
/// over-approximates in standards mode — <c>.Row</c> in a sheet makes a write of <c>row</c> look
/// style-affecting. That is one unnecessary rebuild, in the safe direction, and it removes the need
/// for this type to know the document's mode.
/// </para>
/// <para>
/// <b>Where it is built.</b> Broiler.HTML builds one over the sheets of each render-tree rebuild and
/// hands it to Broiler.Layout's <c>RenderTreeInvalidation.MarkRebuilt</c>, which asks it about
/// every later attribute write: the set describes the sheets the tree standing right now was built
/// from, so installing it anywhere else would let a mutation be classified against a stylesheet set
/// the tree does not reflect. The rest of that decision — attributes layout reads directly, and
/// whether a mutation reaches the render tree at all — stays in Layout.
/// </para>
/// <para>
/// <b>Immutable once built</b>, like the rule index (<see cref="CssCascadeRuleIndex"/>) it is the
/// invalidation counterpart of, and for the same reason: it is a function of the stylesheets alone,
/// so DOM churn must not throw it away.
/// </para>
/// </remarks>
public sealed class CascadeInvalidationSet
{
    private static readonly char[] AsciiWhitespace = [' ', '\t', '\n', '\r', '\f'];

    private readonly HashSet<string> _attributeNames;
    private readonly HashSet<string> _classNames;
    private readonly HashSet<string> _ids;

    private CascadeInvalidationSet(
        HashSet<string> attributeNames,
        HashSet<string> classNames,
        HashSet<string> ids,
        bool conservative)
    {
        _attributeNames = attributeNames;
        _classNames = classNames;
        _ids = ids;
        IsConservative = conservative;
    }

    /// <summary>
    /// The set that claims every attribute could matter. What an unscannable stylesheet produces,
    /// and what a caller with no stylesheets to offer should use.
    /// </summary>
    public static CascadeInvalidationSet Everything { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        conservative: true);

    /// <summary>
    /// Whether this set gave up on scanning and claims everything. <see cref="AffectsStyle"/>
    /// returns <see langword="true"/> unconditionally when it does.
    /// </summary>
    public bool IsConservative { get; }

    /// <summary>Distinct attribute names any selector filters on or any <c>attr()</c> reads.</summary>
    public int AttributeNameCount => _attributeNames.Count;

    /// <summary>Distinct class names any selector names.</summary>
    public int ClassNameCount => _classNames.Count;

    /// <summary>Distinct ids any selector names.</summary>
    public int IdCount => _ids.Count;

    /// <summary>
    /// Builds the set over <paramref name="sheets"/> in any order — the set is unordered, so cascade
    /// order is irrelevant to it. Null sheets are skipped; a null or empty sequence produces
    /// <see cref="Everything"/>, because "no sheets were offered" is not "no sheets exist".
    /// </summary>
    public static CascadeInvalidationSet Build(IEnumerable<CssStyleSheet?>? sheets)
    {
        if (sheets is null)
            return Everything;

        var attributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var classNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conservative = false;
        var sawSheet = false;

        void Walk(IReadOnlyList<CssRule> rules)
        {
            foreach (var rule in rules)
            {
                switch (rule)
                {
                    case CssStyleRule styleRule:
                        foreach (var selector in styleRule.Selectors.Selectors)
                            conservative |= !ScanSelector(selector.Text, attributeNames, classNames, ids);

                        conservative |= !ScanDeclarations(styleRule.Declarations, attributeNames);
                        break;

                    // Conditional groups are walked whether or not they currently apply. @media and
                    // @supports are fixed for a build and the rule index drops the ones that do not
                    // apply, but this set outlives no build and costs one hash entry per name: a
                    // group that is inert today is not worth a second code path.
                    case CssAtRule atRule:
                        conservative |= !ScanDeclarations(atRule.Declarations, attributeNames);
                        Walk(atRule.Rules);
                        break;
                }
            }
        }

        foreach (var sheet in sheets)
        {
            if (sheet is null)
                continue;

            sawSheet = true;
            Walk(sheet.Rules);
        }

        if (!sawSheet || conservative)
            return Everything;

        return new CascadeInvalidationSet(attributeNames, classNames, ids, conservative: false);
    }

    /// <summary>
    /// Whether a write of <paramref name="attributeName"/> from <paramref name="oldValue"/> to
    /// <paramref name="newValue"/> could change what any rule in these sheets matches, or what any
    /// declaration in them resolves to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>class</c> and <c>id</c> are answered by value rather than by name, which is the whole
    /// point of an invalidation set: <c>el.classList.add("is-open")</c> matters only if some
    /// selector says <c>.is-open</c>. The comparison is over the <em>changed</em> tokens — the
    /// symmetric difference of the two token lists — because reordering and duplication change no
    /// match. Any <c>[class…]</c> or <c>[id…]</c> selector, or an <c>attr(class)</c>, files the name
    /// itself and short-circuits all of that: a substring match like <c>[class*="btn"]</c> cannot be
    /// answered token by token.
    /// </para>
    /// <para>
    /// Every other attribute is answered by name alone. Values would need the selector's operator
    /// and operand to be modelled — <c>[data-k="3"]</c> against a write of <c>4</c> — which is real
    /// selectivity this deliberately does not take: the name test already answers the case the
    /// harness sizes, and each operator modelled is another way to be wrong in the direction that
    /// renders a stale page.
    /// </para>
    /// </remarks>
    public bool AffectsStyle(string? attributeName, string? oldValue, string? newValue)
    {
        if (IsConservative)
            return true;

        if (string.IsNullOrEmpty(attributeName))
            return true;

        if (_attributeNames.Contains(attributeName))
            return true;

        if (attributeName.Equals("class", StringComparison.OrdinalIgnoreCase))
            return ChangedTokenIsNamed(oldValue, newValue, _classNames);

        if (attributeName.Equals("id", StringComparison.OrdinalIgnoreCase))
            return Named(oldValue, _ids) || Named(newValue, _ids);

        return false;
    }

    private static bool Named(string? value, HashSet<string> names) =>
        !string.IsNullOrEmpty(value) && names.Contains(value);

    /// <summary>
    /// Whether any token that <paramref name="oldValue"/> and <paramref name="newValue"/> do not
    /// share appears in <paramref name="names"/>.
    /// </summary>
    private static bool ChangedTokenIsNamed(string? oldValue, string? newValue, HashSet<string> names)
    {
        if (names.Count == 0)
            return false;

        var before = Tokens(oldValue);
        var after = Tokens(newValue);

        foreach (var token in before)
        {
            if (!after.Contains(token) && names.Contains(token))
                return true;
        }

        foreach (var token in after)
        {
            if (!before.Contains(token) && names.Contains(token))
                return true;
        }

        return false;
    }

    private static HashSet<string> Tokens(string? value)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(value))
            return tokens;

        foreach (var token in value.Split(AsciiWhitespace, StringSplitOptions.RemoveEmptyEntries))
            tokens.Add(token);

        return tokens;
    }

    /// <summary>
    /// Files every class, id and attribute name mentioned anywhere in <paramref name="selector"/>.
    /// Returns <see langword="false"/> when the text contains something it does not model, which
    /// makes the whole set conservative.
    /// </summary>
    /// <remarks>
    /// A flat scan rather than a parse, and it is flat on purpose: a <c>.name</c> inside
    /// <c>:not(…)</c>, <c>:is(…)</c> or <c>:has(…)</c> has to be filed exactly like one in a subject
    /// compound, because a change to it changes what the selector matches either way. Quoted strings
    /// are skipped so that <c>[title="a.b"]</c> and <c>[href*="#x"]</c> file no class and no id.
    /// </remarks>
    private static bool ScanSelector(
        string? selector,
        HashSet<string> attributeNames,
        HashSet<string> classNames,
        HashSet<string> ids)
    {
        if (string.IsNullOrEmpty(selector))
            return true;

        for (var i = 0; i < selector.Length; i++)
        {
            var c = selector[i];
            switch (c)
            {
                // An escaped identifier would have to be unescaped before it could be compared
                // against a DOM value, which is a different job from scanning. Rare enough that
                // giving up on the whole sheet costs nothing measurable.
                case '\\':
                    return false;

                case '\'':
                case '"':
                {
                    var end = SkipString(selector, i);
                    if (end < 0)
                        return false;
                    i = end;
                    break;
                }

                case '.':
                {
                    var end = ScanName(selector, i + 1);
                    if (end == i + 1)
                        return false;
                    classNames.Add(selector[(i + 1)..end]);
                    i = end - 1;
                    break;
                }

                case '#':
                {
                    var end = ScanName(selector, i + 1);
                    if (end == i + 1)
                        return false;
                    ids.Add(selector[(i + 1)..end]);
                    i = end - 1;
                    break;
                }

                case '[':
                {
                    var nameEnd = ScanName(selector, i + 1);
                    if (nameEnd == i + 1)
                        return false;

                    // `[svg|href]` names an attribute in a namespace, and a namespaced attribute is
                    // not the one a plain `AttributeName` write reports. Give up rather than file
                    // the wrong name.
                    if (nameEnd < selector.Length && selector[nameEnd] == '|')
                        return false;

                    attributeNames.Add(selector[(i + 1)..nameEnd]);

                    var close = SkipToClose(selector, nameEnd);
                    if (close < 0)
                        return false;
                    i = close;
                    break;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Files every attribute name any value in <paramref name="declarations"/> reads through
    /// <c>attr()</c>. Returns <see langword="false"/> on an <c>attr(</c> whose argument this cannot
    /// read.
    /// </summary>
    private static bool ScanDeclarations(CssDeclarationBlock? declarations, HashSet<string> attributeNames)
    {
        if (declarations is null)
            return true;

        foreach (var declaration in declarations.Declarations)
        {
            var text = declaration.Value?.Text;
            if (string.IsNullOrEmpty(text))
                continue;

            var index = text.IndexOf("attr(", StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                var start = index + "attr(".Length;
                while (start < text.Length && char.IsWhiteSpace(text[start]))
                    start++;

                var end = ScanName(text, start);
                if (end == start)
                    return false;

                attributeNames.Add(text[start..end]);
                index = text.IndexOf("attr(", end, StringComparison.OrdinalIgnoreCase);
            }
        }

        return true;
    }

    /// <summary>Index just past the identifier starting at <paramref name="index"/>.</summary>
    private static int ScanName(string text, int index)
    {
        var i = index;

        // A leading digit is not a valid identifier start; returning `index` makes the caller give
        // up, which is what should happen to `[3d]` and to anything else this does not model.
        if (i < text.Length && char.IsAsciiDigit(text[i]))
            return index;

        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c >= 0x80)
            {
                i++;
                continue;
            }

            break;
        }

        return i;
    }

    /// <summary>Index of the closing quote of the string starting at <paramref name="index"/>, or -1.</summary>
    private static int SkipString(string text, int index)
    {
        var quote = text[index];
        for (var i = index + 1; i < text.Length; i++)
        {
            if (text[i] == '\\')
                return -1;
            if (text[i] == quote)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Index of the <c>]</c> closing the attribute selector whose name ends at
    /// <paramref name="index"/>, or -1 when it is unbalanced or contains an escape.
    /// </summary>
    private static int SkipToClose(string text, int index)
    {
        for (var i = index; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\')
                return -1;

            if (c == '\'' || c == '"')
            {
                var end = SkipString(text, i);
                if (end < 0)
                    return -1;
                i = end;
                continue;
            }

            if (c == ']')
                return i;
        }

        return -1;
    }
}
