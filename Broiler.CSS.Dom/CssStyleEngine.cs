using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.Dom;

namespace Broiler.CSS.Dom;

/// <summary>
/// Shared cascade and computed-style authority over the canonical
/// <see cref="Broiler.Dom"/> tree. Stylesheets are added with an explicit
/// <see cref="CssOrigin"/>; <see cref="GetComputedStyle"/> resolves the cascade
/// (origin, importance, specificity, source order), inheritance, custom
/// properties, shorthands, and initial values into an immutable
/// <see cref="CssComputedStyle"/>.
/// </summary>
/// <remarks>
/// This engine owns CSS semantics only. Layout used-value resolution, painting,
/// resource loading, and JavaScript CSSOM wrappers remain with the consuming
/// renderer and bridge. Environment-dependent inputs (viewport) arrive through
/// <see cref="CssEnvironment"/>; dynamic form-control state arrives through
/// <see cref="ICssSelectorStateProvider"/>.
/// </remarks>
public sealed partial class CssStyleEngine
{
    private readonly CssSelectorMatcher _matcher;
    private readonly List<StyleSheetEntry> _sheets = [];
    private readonly Dictionary<(DomElement Element, string? Pseudo), CssComputedStyle> _cache = new();
    // Memoizes the declared cascade winners (stylesheets + optional inline) per
    // (element, pseudo, includeInline). The declared cascade is the hot inner step
    // shared by every GetComputedStyle/GetCascadedStyle/GetCascadedDeclaredValues
    // query — for a single box projection it is re-derived many times over — and,
    // unlike the computed-style _cache, was previously recomputed from scratch each
    // time (linear selector scan of every UA + author rule). Cleared alongside
    // _cache in InvalidateAll, so it shares the exact same correctness lifecycle.
    private readonly Dictionary<(DomElement Element, string? Pseudo, bool IncludeInline), IReadOnlyDictionary<string, string>> _declaredCascadeCache = new();
    // Bumped by InvalidateAll; a declared-cascade computation captures it up front and
    // only memoizes its result if it is unchanged at the end, so a mid-cascade
    // stylesheet re-sync (host callback during selector matching, see the _sheets
    // snapshot note in GetCascadedDeclarationMap) can never store a stale entry.
    private int _cacheGeneration;
    private readonly HashSet<DomDocument> _observedDocuments = [];
    private CssEnvironment _environment = CssEnvironment.Headless;

    public CssStyleEngine(ICssSelectorStateProvider? stateProvider = null) =>
        _matcher = new CssSelectorMatcher(stateProvider);

    /// <summary>Registers a parsed stylesheet under the given cascade origin.</summary>
    public void AddStyleSheet(CssStyleSheet sheet, CssOrigin origin = CssOrigin.Author)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        _sheets.Add(new StyleSheetEntry(sheet, origin));
        InvalidateAll();
    }

    /// <summary>Removes all registered stylesheets.</summary>
    public void ClearStyleSheets()
    {
        if (_sheets.Count == 0)
            return;
        _sheets.Clear();
        InvalidateAll();
    }

    /// <summary>
    /// Updates the environment (viewport) used for media-query and
    /// viewport-relative length resolution, clearing cached results.
    /// </summary>
    public void UpdateEnvironment(CssEnvironment environment)
    {
        if (_environment.Equals(environment))
            return;
        _environment = environment;
        InvalidateAll();
    }

    /// <summary>
    /// Evaluates a media-query list against the supplied CSS environment using the
    /// same evaluator that filters stylesheet rules in this engine.
    /// </summary>
    public static bool MatchesMediaQuery(string query, CssEnvironment environment) =>
        EvaluateMediaQuery(query, environment.ViewportWidth, environment.ViewportHeight);

    /// <summary>
    /// Reports whether an <c>@supports</c> prelude is a grammatically valid
    /// <c>&lt;supports-condition&gt;</c>, using the same check that gates whether the
    /// rule's contents participate in the cascade. A malformed condition (for
    /// example a declaration missing its parentheses) is invalid and its rules do
    /// not apply.
    /// </summary>
    public static bool IsValidSupportsCondition(string condition) =>
        SupportsConditionSyntax.IsValidCondition(condition);

    /// <summary>
    /// Computes the immutable computed style for <paramref name="element"/>,
    /// optionally for a <paramref name="pseudoElement"/> such as
    /// <c>"::before"</c>. Results are cached until a relevant DOM mutation or a
    /// stylesheet/environment change invalidates them.
    /// </summary>
    public CssComputedStyle GetComputedStyle(
        DomElement element,
        CssEnvironment? environment = null,
        string? pseudoElement = null)
    {
        if (element is null)
            return CssComputedStyle.Empty;

        if (environment is { } env && !env.Equals(_environment))
            UpdateEnvironment(env);

        ObserveDocument(element);

        var normalizedPseudo = NormalizePseudoElement(pseudoElement);
        var key = (element, normalizedPseudo);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var computed = ComputeStyle(element, normalizedPseudo, []);
        var snapshot = new CssComputedStyle(computed);
        _cache[key] = snapshot;
        return snapshot;
    }

    /// <summary>
    /// Returns the cascade-winning <em>declared</em> values for
    /// <paramref name="element"/> from the registered stylesheets only: the raw
    /// author values after origin/importance/specificity/source-order resolution
    /// and value validation, but <em>without</em> inline styles, inheritance,
    /// shorthand expansion, custom-property resolution, or initial-value backfill.
    /// This is the specified-value view consumers such as anchor positioning need,
    /// where an undeclared property must be reported as absent rather than as its
    /// initial value (which <see cref="GetComputedStyle"/> would supply).
    /// </summary>
    public IReadOnlyDictionary<string, string> GetCascadedDeclaredValues(
        DomElement element,
        string? pseudoElement = null)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (element is null)
            return result;

        ObserveDocument(element);
        CollectCascadedDeclarations(element, NormalizePseudoElement(pseudoElement), result);
        return result;
    }

    /// <summary>
    /// Returns the cascade-resolved values for <paramref name="element"/> intended for
    /// the HTML renderer's box projection: cascade winners from all registered
    /// stylesheets (origin/importance/specificity/source order), with custom-property
    /// and <c>var()</c> resolution, CSS-wide keyword handling, shorthand expansion,
    /// <c>attr()</c> length substitution, and relative font-weight resolution — but
    /// <em>without</em> inheritance backfill, initial-value backfill, or the
    /// form-control/logical-size synthesis that <see cref="GetComputedStyle"/> performs.
    /// </summary>
    /// <remarks>
    /// This mirrors what the legacy renderer cascade assigns from matched rules: only
    /// explicitly-cascaded longhands, so the renderer keeps its own per-box defaults and
    /// its own inheritance pass for everything not declared here. The <c>inherit</c>
    /// keyword is folded to the parent element's computed value (its used meaning), since
    /// the renderer projects concrete values rather than CSS-wide keywords. Inline
    /// <c>style=</c> participates in the same cascade when <paramref name="includeInlineStyle"/>
    /// is set; the default remains stylesheet-only for declared-style consumers.
    /// </remarks>
    public IReadOnlyDictionary<string, string> GetCascadedStyle(
        DomElement element,
        string? pseudoElement = null,
        bool includeInlineStyle = false)
    {
        if (element is null)
            return EmptyReadOnlyMap;

        ObserveDocument(element);
        return ComputeCascadedStyle(
            element,
            NormalizePseudoElement(pseudoElement),
            new HashSet<DomElement>(),
            includeInlineStyle);
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyReadOnlyMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, string> ComputeCascadedStyle(
        DomElement element,
        string? pseudoElement,
        HashSet<DomElement> ancestorsInProgress,
        bool includeInlineStyle)
    {
        var computed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Parent computed style — used only to resolve relative font-weight and to fold
        // the `inherit` keyword, never to backfill inherited properties (the renderer's
        // own InheritStyle does that).
        var parentElement = ParentElement(element);
        IReadOnlyDictionary<string, string>? parentProps = null;
        if (parentElement is not null && ancestorsInProgress.Add(parentElement))
        {
            try
            {
                parentProps = GetComputedStyleInternal(parentElement, ancestorsInProgress).AsMap();
            }
            finally
            {
                ancestorsInProgress.Remove(parentElement);
            }
        }

        var registrations = CollectCustomPropertyRegistrations();

        // 1. Cascade declarations from all registered stylesheets and, when requested,
        // the inline declaration block at author origin with inline specificity.
        CollectCascadedDeclarations(element, pseudoElement, computed, includeInlineStyle);

        // 2. Custom properties: inheritance, registered defaults, var().
        MergeResolvedCustomProperties(computed, element, registrations, ancestorsInProgress);
        ResolveKnownCustomProperties(computed);

        // 3. CSS-wide keywords (initial/unset/revert resolved; inherit preserved here).
        ResolveCssWideKeywordProperties(computed, parentProps);

        // 4. Shorthand expansion and attr() length substitution.
        ExpandCssShorthands(computed);
        ResolveLengthAttrFunctions(computed, element);

        // 4b. Border shorthand reset: a `border` / `border-<side>` shorthand resets ALL of
        // its longhands, so a component the shorthand omits (commonly the color) must be
        // projected as its initial value rather than left absent — otherwise the renderer
        // keeps a stale value (e.g. an important `border: 1px solid` would fail to override
        // a prior `border: ... red` because no border-color longhand is produced).
        ApplyBorderShorthandResets(computed);

        // 5. Relative font-weight keywords need the inherited numeric weight.
        var parentWeight = parentProps != null && parentProps.TryGetValue("font-weight", out var pw) && int.TryParse(pw, out var pwn)
            ? pwn
            : 400;
        ResolveFontWeightKeywords(computed, parentWeight);

        // 6. Fold any remaining `inherit` to the parent's computed (used) value so the
        // renderer projects a concrete value. Drop it if the parent has none.
        FoldInheritKeyword(computed, parentProps);

        return computed;
    }

    private static readonly string[] BorderSides = ["top", "right", "bottom", "left"];
    private static readonly string[] BorderComponents = ["width", "style", "color"];

    private static void ApplyBorderShorthandResets(Dictionary<string, string> computed)
    {
        bool allSides = computed.ContainsKey("border");
        foreach (var side in BorderSides)
        {
            if (!allSides && !computed.ContainsKey($"border-{side}"))
                continue;

            foreach (var component in BorderComponents)
            {
                var longhand = $"border-{side}-{component}";
                if (computed.ContainsKey(longhand))
                    continue;

                if (component == "width")
                {
                    // border-width's initial is 'medium', not 0 — the plain
                    // computed-initial (0px) dropped the border for the common
                    // `border: solid <color>` form, which omits the width (WPT
                    // css/CSS2/box-display/root-canvas-001's lime border).  The
                    // shorthand that triggered this reset supplies a border-style,
                    // so project the *used* width: 'medium' when that style paints,
                    // but 0 for 'none'/'hidden' (their used border-width is 0).  A
                    // missing style is its own initial 'none' → 0.
                    var borderStyle = computed.TryGetValue($"border-{side}-style", out var st)
                        && !string.IsNullOrWhiteSpace(st)
                        ? st.Trim().ToLowerInvariant()
                        : "none";
                    computed[longhand] = borderStyle is "none" or "hidden" ? "0px" : "medium";
                }
                else if (CssInitialValues.TryGetValue(longhand, out var initial))
                {
                    computed[longhand] = initial;
                }
            }
        }
    }

    private static void FoldInheritKeyword(
        Dictionary<string, string> computed,
        IReadOnlyDictionary<string, string>? parentProps)
    {
        foreach (var key in computed.Keys.ToList())
        {
            if (key.StartsWith("--", StringComparison.Ordinal))
                continue;
            if (!computed.TryGetValue(key, out var value) ||
                !string.Equals(value?.Trim(), "inherit", StringComparison.OrdinalIgnoreCase))
                continue;

            if (parentProps != null && parentProps.TryGetValue(key, out var inherited) && !string.IsNullOrWhiteSpace(inherited))
                computed[key] = inherited;
            else
                computed.Remove(key);
        }
    }

    private CssComputedStyle GetComputedStyleInternal(DomElement element, HashSet<DomElement> ancestorsInProgress)
    {
        var key = ((DomElement, string?))(element, null);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var computed = ComputeStyle(element, pseudoElement: null, ancestorsInProgress);
        var snapshot = new CssComputedStyle(computed);
        _cache[key] = snapshot;
        return snapshot;
    }

    private Dictionary<string, string> ComputeStyle(
        DomElement element,
        string? pseudoElement,
        HashSet<DomElement> ancestorsInProgress)
    {
        var computed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Inheritance source: the parent element's computed style. Guard against
        // cycles in malformed trees.
        var parentElement = ParentElement(element);
        IReadOnlyDictionary<string, string>? parentProps = null;
        if (parentElement is not null && ancestorsInProgress.Add(parentElement))
        {
            try
            {
                parentProps = GetComputedStyleInternal(parentElement, ancestorsInProgress).AsMap();
            }
            finally
            {
                ancestorsInProgress.Remove(parentElement);
            }
        }

        var registrations = CollectCustomPropertyRegistrations();

        // 1-2. Stylesheet and inline declarations share one origin-aware cascade.
        CollectCascadedDeclarations(element, pseudoElement, computed, includeInlineStyle: true);

        // 3. Custom properties: resolve inheritance, registered defaults, and var().
        MergeResolvedCustomProperties(computed, element, registrations, ancestorsInProgress);
        ResolveKnownCustomProperties(computed);

        // 4. CSS-wide keywords (initial / unset / revert; inherit preserved).
        ResolveCssWideKeywordProperties(computed, parentProps);

        // 5. Shorthand expansion and attr() length substitution.
        ExpandCssShorthands(computed);
        ResolveLengthAttrFunctions(computed, element);

        // 6. Relative font-weight keywords need the inherited numeric weight.
        var parentWeight = parentProps != null && parentProps.TryGetValue("font-weight", out var pw) && int.TryParse(pw, out var pwn)
            ? pwn
            : 400;
        ResolveFontWeightKeywords(computed, parentWeight);

        // 7. Inheritance backfill for inherited properties not otherwise set.
        if (parentProps != null)
        {
            foreach (var property in CssInheritedProperties)
            {
                if (computed.ContainsKey(property))
                    continue;
                if (parentProps.TryGetValue(property, out var inherited) && !string.IsNullOrWhiteSpace(inherited))
                    computed[property] = inherited;
            }
        }

        // 8. Initial values for everything still unset.
        foreach (var kv in CssInitialValues)
        {
            if (!computed.ContainsKey(kv.Key))
                computed[kv.Key] = kv.Value;
        }

        ApplyApproximateFormControlComputedSizes(computed, element);
        ApplyLogicalSizeAliases(computed);

        return computed;
    }

    // ---- Cascade -----------------------------------------------------------

    private void CollectCascadedDeclarations(
        DomElement element,
        string? pseudoElement,
        Dictionary<string, string> computed,
        bool includeInlineStyle = false)
    {
        // GetCascadedDeclarationMap returns a shared, read-only map — copy from it.
        foreach (var kv in GetCascadedDeclarationMap(element, pseudoElement, includeInlineStyle))
            computed[kv.Key] = kv.Value;
    }

    /// <summary>
    /// Returns the cascade-winning declared values (registered stylesheets, plus the
    /// inline declaration block when <paramref name="includeInlineStyle"/> is set) for
    /// (<paramref name="element"/>, <paramref name="pseudoElement"/>), memoized for the
    /// engine's current cache generation. The returned map is shared and must be treated
    /// as read-only by callers. The memo is cleared by <see cref="InvalidateAll"/> on any
    /// stylesheet / environment / document-mutation change — the same lifecycle that
    /// governs the computed-style <see cref="_cache"/> — and a generation guard skips
    /// caching a result whose computation raced an invalidation.
    /// </summary>
    private IReadOnlyDictionary<string, string> GetCascadedDeclarationMap(
        DomElement element,
        string? pseudoElement,
        bool includeInlineStyle)
    {
        var key = (element, pseudoElement, includeInlineStyle);
        if (_declaredCascadeCache.TryGetValue(key, out var cached))
            return cached;

        var generation = _cacheGeneration;
        var winners = new Dictionary<string, CascadeSlot>(StringComparer.OrdinalIgnoreCase);
        var order = 0;

        // Snapshot the sheet list before iterating: selector matching can call
        // back into the host (e.g. the DOM bridge) which may re-sync this engine's
        // stylesheets mid-cascade (ClearStyleSheets + AddStyleSheet) when the
        // document mutated — e.g. while anchor positioning rewrites styles. That
        // structural change to a live foreach would throw "Collection was
        // modified" (WPT content-visibility-anchor-positioning); the snapshot
        // keeps the in-progress cascade self-consistent and crash-free.
        foreach (var entry in _sheets.ToArray())
            CollectFromRules(entry.Sheet.Rules, entry.Origin, element, pseudoElement, winners, ref order);

        if (includeInlineStyle && pseudoElement is null)
        {
            var inline = Attr(element, "style");
            if (!string.IsNullOrEmpty(inline))
            {
                foreach (var (name, value, important) in ParseDeclarations(inline))
                {
                    if (IsAcceptableDeclarationValue(name, value))
                        AddDeclaration(winners, name, value, important, CssOrigin.Author, int.MaxValue, order++);
                    else
                        CssEngineDiagnostics.ReportRejected(name, value);
                }
            }
        }

        var map = new Dictionary<string, string>(winners.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in winners)
            map[kv.Key] = kv.Value.Value;

        // Only memoize when no invalidation occurred while this cascade was computed:
        // a host re-sync (see the snapshot note above) bumps the generation and clears
        // the cache, so a result derived from the pre-re-sync sheet snapshot is stale.
        if (generation == _cacheGeneration)
            _declaredCascadeCache[key] = map;

        return map;
    }

    private void CollectFromRules(
        IReadOnlyList<CssRule> rules,
        CssOrigin origin,
        DomElement element,
        string? pseudoElement,
        Dictionary<string, CascadeSlot> winners,
        ref int order)
    {
        foreach (var rule in rules)
        {
            switch (rule)
            {
                case CssStyleRule styleRule:
                    ApplyStyleRule(styleRule, origin, element, pseudoElement, winners, ref order);
                    break;

                case CssAtRule atRule when atRule.Name.Equals("media", StringComparison.OrdinalIgnoreCase):
                    if (EvaluateMediaQuery(atRule.Prelude, _environment.ViewportWidth, _environment.ViewportHeight))
                        CollectFromRules(atRule.Rules, origin, element, pseudoElement, winners, ref order);
                    break;

                case CssAtRule atRule when atRule.Name.Equals("supports", StringComparison.OrdinalIgnoreCase):
                    // Feature detection is optimistic — we assume support for any well-formed
                    // feature query rather than modelling which properties/values Broiler
                    // actually implements. But an @supports rule whose condition does not match
                    // the <supports-condition> grammar (e.g. a declaration missing its required
                    // parentheses, or "and"/"or" mixed without grouping) has a false condition
                    // and MUST NOT apply its rules. Gate the descent on grammatical validity so
                    // those malformed conditions are correctly ignored.
                    if (SupportsConditionSyntax.IsValidCondition(atRule.Prelude))
                        CollectFromRules(atRule.Rules, origin, element, pseudoElement, winners, ref order);
                    break;
            }
        }
    }

    private void ApplyStyleRule(
        CssStyleRule styleRule,
        CssOrigin origin,
        DomElement element,
        string? pseudoElement,
        Dictionary<string, CascadeSlot> winners,
        ref int order)
    {
        var bestSpecificity = -1;
        foreach (var selector in styleRule.Selectors.Selectors)
        {
            if (SelectorMatchesComputedStyleTarget(element, selector.Text.Trim(), pseudoElement))
            {
                var spec = CssSelectorParser.CalculateSpecificity(selector.Text).Encoded;
                if (spec > bestSpecificity)
                    bestSpecificity = spec;
            }
        }

        if (bestSpecificity < 0)
            return;

        foreach (var declaration in styleRule.Declarations.Declarations)
        {
            if (!IsPropertyAllowedForPseudoElement(pseudoElement, declaration.Name))
                continue;

            // CSS error recovery: drop invalid declarations so a previously
            // cascaded valid value wins rather than the invalid last-parsed one.
            if (!IsAcceptableDeclarationValue(declaration.Name, declaration.Value.Text))
            {
                CssEngineDiagnostics.ReportRejected(declaration.Name, declaration.Value.Text);
                continue;
            }

            var currentOrder = order++;
            AddDeclaration(winners, declaration.Name, declaration.Value.Text, declaration.Important, origin, bestSpecificity, currentOrder);
        }
    }

    private static void AddDeclaration(
        Dictionary<string, CascadeSlot> winners,
        string property,
        string value,
        bool important,
        CssOrigin origin,
        int specificity,
        int order)
    {
        var slot = new CascadeSlot(value, CascadeRank(origin, important), specificity, order);
        if (!winners.TryGetValue(property, out var existing) || slot.Beats(existing))
            winners[property] = slot;

        var unprefixed = StripVendorPrefix(property);
        if (!string.Equals(unprefixed, property, StringComparison.Ordinal))
        {
            var aliasSlot = new CascadeSlot(value, CascadeRank(origin, important), specificity, order);
            if (!winners.TryGetValue(unprefixed, out var existingAlias) || aliasSlot.Beats(existingAlias))
                winners[unprefixed] = aliasSlot;
        }
    }

    private static int CascadeRank(CssOrigin origin, bool important) =>
        important
            ? origin switch { CssOrigin.Author => 3, CssOrigin.User => 4, _ => 5 }
            : origin switch { CssOrigin.UserAgent => 0, CssOrigin.User => 1, _ => 2 };

    private readonly record struct CascadeSlot(string Value, int Rank, int Specificity, int Order)
    {
        // Later declarations of equal rank and specificity win (source order).
        public bool Beats(CascadeSlot other) =>
            Rank != other.Rank ? Rank > other.Rank
            : Specificity != other.Specificity ? Specificity > other.Specificity
            : Order >= other.Order;
    }

    private readonly record struct StyleSheetEntry(CssStyleSheet Sheet, CssOrigin Origin);

    // ---- Declaration parsing ----------------------------------------------

    private static IEnumerable<(string Name, string Value, bool Important)> ParseDeclarations(string declarationText)
    {
        var block = new CssParser().ParseDeclarations(declarationText);
        foreach (var declaration in block.Declarations)
            yield return (declaration.Name, declaration.Value.Text, declaration.Important);
    }

    private static string StripVendorPrefix(string property)
    {
        if (property.StartsWith("-webkit-", StringComparison.OrdinalIgnoreCase))
            return property[8..];
        if (property.StartsWith("-moz-", StringComparison.OrdinalIgnoreCase))
            return property[5..];
        if (property.StartsWith("-ms-", StringComparison.OrdinalIgnoreCase))
            return property[4..];
        if (property.StartsWith("-o-", StringComparison.OrdinalIgnoreCase))
            return property[3..];
        return property;
    }

    // ---- Pseudo-element targeting -----------------------------------------

    private bool SelectorMatchesComputedStyleTarget(DomElement element, string selector, string? pseudoElement)
    {
        if (pseudoElement is null)
            return !ContainsPseudoElementSelector(selector) && _matcher.Matches(element, selector);

        if (!TryStripPseudoElementSelector(selector, pseudoElement, out var baseSelector))
            return false;

        return _matcher.Matches(element, baseSelector);
    }

    private static bool ContainsPseudoElementSelector(string selector)
    {
        if (selector.IndexOf("::", StringComparison.Ordinal) >= 0)
            return true;

        return selector.EndsWith(":before", StringComparison.OrdinalIgnoreCase)
            || selector.EndsWith(":after", StringComparison.OrdinalIgnoreCase)
            || selector.EndsWith(":first-line", StringComparison.OrdinalIgnoreCase)
            || selector.EndsWith(":first-letter", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryStripPseudoElementSelector(string selector, string pseudoElement, out string baseSelector)
    {
        baseSelector = selector;
        var normalized = pseudoElement[2..];
        var doubleColonIndex = selector.LastIndexOf("::", StringComparison.Ordinal);
        if (doubleColonIndex >= 0)
        {
            var suffix = selector[doubleColonIndex..];
            if (!suffix.Equals(pseudoElement, StringComparison.OrdinalIgnoreCase))
                return false;

            baseSelector = selector[..doubleColonIndex].TrimEnd();
            return baseSelector.Length > 0;
        }

        var singleColonSuffix = ":" + normalized;
        if (!selector.EndsWith(singleColonSuffix, StringComparison.OrdinalIgnoreCase))
            return false;

        baseSelector = selector[..^singleColonSuffix.Length].TrimEnd();
        return baseSelector.Length > 0;
    }

    private static bool IsPropertyAllowedForPseudoElement(string? pseudoElement, string propertyName) =>
        pseudoElement switch
        {
            null => true,
            "::first-line" or "::first-letter" => IsFirstLineOrLetterProperty(propertyName),
            _ => true,
        };

    private static bool IsFirstLineOrLetterProperty(string propertyName) =>
        propertyName.StartsWith("--", StringComparison.Ordinal)
        || propertyName is "color"
        or "background-color"
        or "font"
        or "font-family"
        or "font-size"
        or "font-style"
        or "font-variant"
        or "font-weight"
        or "line-height"
        or "letter-spacing"
        or "word-spacing"
        or "text-decoration"
        or "text-transform";

    private static string? NormalizePseudoElement(string? pseudoElement)
    {
        if (string.IsNullOrWhiteSpace(pseudoElement))
            return null;

        pseudoElement = pseudoElement.Trim();
        if (pseudoElement.Equals("::before", StringComparison.OrdinalIgnoreCase) ||
            pseudoElement.Equals(":before", StringComparison.OrdinalIgnoreCase))
            return "::before";
        if (pseudoElement.Equals("::after", StringComparison.OrdinalIgnoreCase) ||
            pseudoElement.Equals(":after", StringComparison.OrdinalIgnoreCase))
            return "::after";
        if (pseudoElement.Equals("::first-line", StringComparison.OrdinalIgnoreCase) ||
            pseudoElement.Equals(":first-line", StringComparison.OrdinalIgnoreCase))
            return "::first-line";
        if (pseudoElement.Equals("::first-letter", StringComparison.OrdinalIgnoreCase) ||
            pseudoElement.Equals(":first-letter", StringComparison.OrdinalIgnoreCase))
            return "::first-letter";
        if (pseudoElement.Equals("::selection", StringComparison.OrdinalIgnoreCase))
            return "::selection";
        if (pseudoElement.Equals("::backdrop", StringComparison.OrdinalIgnoreCase))
            return "::backdrop";
        if (pseudoElement.Equals("::marker", StringComparison.OrdinalIgnoreCase))
            return "::marker";

        return null;
    }

    // ---- Element access over the canonical DOM ----------------------------

    private static DomElement? ParentElement(DomElement element) =>
        element.ParentNode as DomElement;

    private static string? Attr(DomElement element, string name) =>
        element.GetAttribute(name);

    private static string TagLower(DomElement element) =>
        element.LocalName.ToLowerInvariant();
}
