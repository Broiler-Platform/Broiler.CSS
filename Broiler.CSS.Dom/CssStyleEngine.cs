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
    private readonly Dictionary<(DomElement Element, string? Pseudo), CssComputedStyle> _cache = [];
    // Sparse computed-style memo: the specified + sparse-inheritance projection (no
    // initial-value backfill) that the HtmlBridge's GetComputedProps consumes. Keyed by
    // element (principal box; pseudo-elements are not on this recursion). Cleared with
    // _cache in InvalidateAll, so it shares the exact same correctness lifecycle.
    private readonly Dictionary<(DomElement Element, string? Pseudo), IReadOnlyDictionary<string, string>> _sparseCache = [];
    // Memoizes the declared cascade winners (stylesheets + optional inline) per
    // (element, pseudo, includeInline). The declared cascade is the hot inner step
    // shared by every GetComputedStyle/GetCascadedStyle/GetCascadedDeclaredValues
    // query — for a single box projection it is re-derived many times over — and,
    // unlike the computed-style _cache, was previously recomputed from scratch each
    // time (linear selector scan of every UA + author rule). Cleared alongside
    // _cache in InvalidateAll, so it shares the exact same correctness lifecycle.
    private readonly Dictionary<(DomElement Element, string? Pseudo, bool IncludeInline), IReadOnlyDictionary<string, string>> _declaredCascadeCache = [];
    // Bumped by InvalidateAll; a declared-cascade computation captures it up front and
    // only memoizes its result if it is unchanged at the end, so a mid-cascade
    // stylesheet re-sync (host callback during selector matching, see the _sheets
    // snapshot note in GetCascadedDeclarationMap) can never store a stale entry.
    private int _cacheGeneration;
    private readonly HashSet<DomDocument> _observedDocuments = [];
    private CssEnvironment _environment = CssEnvironment.Headless;
    // Host-supplied override for an element's inline declaration block. The HtmlBridge
    // sets this to its live ElementRuntimeState inline map (the authoritative inline the
    // JS `el.style.X=` setters and the anchor resolver mutate, which never reaches the DOM
    // `style` attribute). Null = read the `style` attribute as usual.
    private Func<DomElement, string?>? _inlineStyleProvider;

    public CssStyleEngine(ICssSelectorStateProvider? stateProvider = null) =>
        _matcher = new CssSelectorMatcher(stateProvider);

    /// <summary>
    /// Overrides the source of each element's inline declaration block. When set, the
    /// cascade reads inline style from <paramref name="provider"/> (returning the inline
    /// CSS text, or <c>null</c> for none) instead of the element's <c>style</c> attribute.
    /// The HtmlBridge uses this to feed its live ElementRuntimeState inline map — the
    /// authoritative inline source its JS setters and anchor resolver mutate but which
    /// never reaches the DOM <c>style</c> attribute. Invalidates cached results, since the
    /// inline source feeding the cascade has changed.
    /// </summary>
    public void SetInlineStyleSource(Func<DomElement, string?>? provider)
    {
        _inlineStyleProvider = provider;
        InvalidateAll();
    }

    /// <summary>
    /// Clears all memoized cascade/computed-style results. The HtmlBridge calls this when
    /// it mutates the inline source supplied via <see cref="SetInlineStyleSource"/> — a
    /// change the engine's DOM-mutation subscription does not observe.
    /// </summary>
    public void InvalidateComputedStyleCaches() => InvalidateAll();

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
    /// <c>&lt;supports-condition&gt;</c>. A malformed condition (for example a
    /// declaration missing its parentheses) is invalid and its rules do not apply.
    /// Note this is grammatical validity only — whether the rule's contents
    /// participate in the cascade additionally requires the condition to evaluate
    /// to true (an unsupported feature query is valid syntax but false).
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
    /// Returns the full computed-style pipeline for <paramref name="element"/> —
    /// cascade winners (stylesheets + inline), custom-property/<c>var()</c> resolution,
    /// CSS-wide keyword handling, shorthand expansion, <c>attr()</c> length substitution,
    /// relative font-weight resolution, inheritance backfill, form-control size synthesis
    /// and logical-size aliases — but <em>without</em> the initial-value backfill that
    /// <see cref="GetComputedStyle"/> applies. An undeclared, non-inherited property is
    /// therefore <em>absent</em> from the returned map (reads back as <c>null</c>) instead
    /// of resolving to its initial value.
    /// </summary>
    /// <remarks>
    /// This is the "specified + inherited, no initials" view that layout-adjacent bridge
    /// consumers (anchor positioning, sticky, position-area, hit-testing) depend on: their
    /// branch logic keys on a property being undeclared (<c>null</c>) rather than sitting at
    /// its initial value, so the full-initials <see cref="GetComputedStyle"/> map would flip
    /// those branches. It differs from <see cref="GetCascadedStyle"/> by performing the
    /// inheritance backfill, form-control sizing, and logical-size aliasing that computed
    /// style needs; it differs from <see cref="GetComputedStyle"/> only by skipping the
    /// initial-value backfill. The result is a fresh, caller-owned map (not cached and not
    /// shared) — callers that need memoization cache it on their side.
    /// <param name="sparseInheritance">When <c>true</c>, inherited properties are backfilled
    /// from the parent's <em>sparse</em> projection (a nowhere-declared inherited property stays
    /// absent) rather than the parent's full computed style. This is the "specified + sparse
    /// inheritance" view the HtmlBridge's layout/anchor consumers depend on; the default
    /// (<c>false</c>) keeps the full-inheritance behaviour.</param>
    /// </remarks>
    public IReadOnlyDictionary<string, string> GetSparseComputedStyle(
        DomElement element,
        string? pseudoElement = null,
        bool sparseInheritance = false)
    {
        if (element is null)
            return EmptyReadOnlyMap;

        ObserveDocument(element);

        // Principal-box sparse inheritance goes through the cached recursion so the ancestor
        // chain is materialised once per invalidation generation (bridge callers query many
        // elements of the same subtree). Pseudo-elements are off this recursion.
        if (sparseInheritance && pseudoElement is null)
            return GetSparseComputedStyleInternal(element, []);

        return ComputeStyle(
            element,
            NormalizePseudoElement(pseudoElement),
            [],
            backfillInitials: false,
            sparseInheritance: sparseInheritance);
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
            [],
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

    // Cached sparse projection used as the inheritance source when sparseInheritance is on.
    // Mirrors GetComputedStyleInternal (element-keyed, cleared by InvalidateAll) so the
    // sparse recursion stays ~O(N) instead of recomputing every ancestor per query.
    private IReadOnlyDictionary<string, string> GetSparseComputedStyleInternal(
        DomElement element, HashSet<DomElement> ancestorsInProgress)
    {
        var key = ((DomElement, string?))(element, null);
        if (_sparseCache.TryGetValue(key, out var cached))
            return cached;

        IReadOnlyDictionary<string, string> computed = ComputeStyle(
            element, pseudoElement: null, ancestorsInProgress,
            backfillInitials: false, sparseInheritance: true);
        _sparseCache[key] = computed;
        return computed;
    }

    private Dictionary<string, string> ComputeStyle(
        DomElement element,
        string? pseudoElement,
        HashSet<DomElement> ancestorsInProgress,
        bool backfillInitials = true,
        bool sparseInheritance = false)
    {
        var computed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Inheritance source: the parent element's computed style (full), or its sparse
        // projection when sparseInheritance is set. Guard against cycles in malformed trees.
        var parentElement = ParentElement(element);
        IReadOnlyDictionary<string, string>? parentProps = null;
        if (parentElement is not null && ancestorsInProgress.Add(parentElement))
        {
            try
            {
                parentProps = sparseInheritance
                    ? GetSparseComputedStyleInternal(parentElement, ancestorsInProgress)
                    : GetComputedStyleInternal(parentElement, ancestorsInProgress).AsMap();
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

        // 8. Initial values for everything still unset. Skipped for the sparse
        // projection (see GetSparseComputedStyle), whose consumers require an
        // undeclared property to read back as absent (null) rather than as its
        // initial value — the same non-clobbering contract GetCascadedStyle keeps.
        if (backfillInitials)
        {
            foreach (var kv in CssInitialValues)
            {
                if (!computed.ContainsKey(kv.Key))
                    computed[kv.Key] = kv.Value;
            }
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
            var inline = _inlineStyleProvider is { } inlineProvider
                ? inlineProvider(element)
                : Attr(element, "style");
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
                    // An @supports rule applies its contents only when its condition both
                    // parses as a <supports-condition> AND evaluates to true. A malformed
                    // condition (a declaration missing its parentheses, "and"/"or" mixed
                    // without grouping, …) is false; so is a well-formed but unsupported
                    // feature query — an unknown property ("unknown: green"), an invalid value
                    // ("color: rainbow"), or a <general-enclosed> block — resolved here through
                    // the feature-support oracle (IsFeatureQuerySupported).
                    if (SupportsConditionSyntax.EvaluatesTrue(atRule.Prelude, IsFeatureQuerySupported))
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
                // Specificity is computed once at parse time and stored on the
                // selector (CssSelector.Specificity); reuse it rather than
                // re-parsing the selector text on every match in the cascade hot
                // path.
                var spec = selector.Specificity.Encoded;
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

        // A box shorthand (margin/padding) must also compete for its physical
        // longhands within the cascade, so a higher-origin shorthand overrides a
        // lower-origin longhand — most visibly an author `margin: 0` (or
        // `padding: 0`) resetting the user-agent list indent (`ol, ul { margin-left:
        // 40px }`), which the post-cascade expansion could not do because it keeps
        // any already-present longhand regardless of origin. Seeding each longhand
        // with the shorthand's rank/specificity/order lets the normal cascade
        // comparison resolve shorthand-vs-longhand precedence by source order.
        AddBoxShorthandLonghandSlots(winners, property, slot);

        var unprefixed = CssPropertyNames.StripVendorPrefix(property);
        if (!string.Equals(unprefixed, property, StringComparison.Ordinal))
        {
            var aliasSlot = new CascadeSlot(value, CascadeRank(origin, important), specificity, order);
            if (!winners.TryGetValue(unprefixed, out var existingAlias) || aliasSlot.Beats(existingAlias))
                winners[unprefixed] = aliasSlot;
        }
    }

    /// <summary>Physical-side longhands for the <c>margin</c>/<c>padding</c> box
    /// shorthands, expanded into cascade slots so a shorthand and a longhand of the
    /// same property resolve against each other by origin/specificity/source order.</summary>
    private static readonly Dictionary<string, string[]> BoxShorthandLonghands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["margin"] = ["margin-top", "margin-right", "margin-bottom", "margin-left"],
            ["padding"] = ["padding-top", "padding-right", "padding-bottom", "padding-left"],
        };

    /// <summary>
    /// When <paramref name="property"/> is a <c>margin</c>/<c>padding</c> box
    /// shorthand, adds a cascade slot for each of its four physical longhands
    /// (top/right/bottom/left, per the 1–4 value CSS box grammar) carrying the
    /// shorthand's rank/specificity/order, so the ordinary <see cref="CascadeSlot.Beats"/>
    /// comparison lets it override lower-origin longhands and be overridden by later
    /// same-or-higher-origin longhands. No-op for every other property.
    /// </summary>
    private static void AddBoxShorthandLonghandSlots(
        Dictionary<string, CascadeSlot> winners, string property, CascadeSlot shorthand)
    {
        if (!BoxShorthandLonghands.TryGetValue(property, out var sides))
            return;
        var parts = SplitCssValues(shorthand.Value);
        if (parts.Length is 0 or > 4)
            return;
        // CSS box grammar: 1 → all; 2 → v/h; 3 → top/h/bottom; 4 → t/r/b/l.
        string top = parts[0];
        string right = parts.Length >= 2 ? parts[1] : top;
        string bottom = parts.Length >= 3 ? parts[2] : top;
        string left = parts.Length >= 4 ? parts[3] : right;
        var values = new[] { top, right, bottom, left };
        for (int i = 0; i < sides.Length; i++)
        {
            var sideSlot = shorthand with { Value = values[i] };
            if (!winners.TryGetValue(sides[i], out var existing) || sideSlot.Beats(existing))
                winners[sides[i]] = sideSlot;
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
            // A bare pseudo-element selector (e.g. `::backdrop`) has an empty base
            // and is equivalent to `*::backdrop`: it targets the pseudo-element of
            // any originating element. Match it as the universal selector rather
            // than rejecting it (which dropped author `::backdrop { … }` rules).
            if (baseSelector.Length == 0)
                baseSelector = "*";
            return true;
        }

        var singleColonSuffix = ":" + normalized;
        if (!selector.EndsWith(singleColonSuffix, StringComparison.OrdinalIgnoreCase))
            return false;

        baseSelector = selector[..^singleColonSuffix.Length].TrimEnd();
        if (baseSelector.Length == 0)
            baseSelector = "*";
        return true;
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
