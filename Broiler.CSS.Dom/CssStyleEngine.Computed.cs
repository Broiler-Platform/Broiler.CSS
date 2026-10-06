using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Broiler.Dom;

namespace Broiler.CSS.Dom;

// Property metadata tables, custom-property resolution, attr() substitution,
// form-control sizing, logical-size aliases, and mutation-driven cache
// invalidation for the computed-style engine.
public sealed partial class CssStyleEngine
{
    private sealed class CustomPropertyRegistration
    {
        public bool Inherits { get; init; } = true;

        public string? InitialValue { get; init; }
    }

    private Dictionary<string, CustomPropertyRegistration>? _registrations;

    private static readonly Regex LengthAttrFunctionPattern = LengthAttrRegex();

    // ---- @property registrations ------------------------------------------

    private Dictionary<string, CustomPropertyRegistration> CollectCustomPropertyRegistrations()
    {
        // Snapshot the memo, the sheet list and the cache generation together (see the _sync note in
        // the primary partial): _sheets is mutated from other threads, so a live foreach here can
        // corrupt/abort under the same race as the cascade. Compute outside the lock, then publish
        // with a double-checked store so a concurrent computation's instance is reused rather than
        // replaced — callers get a stable identity for a given generation. A collection that raced
        // an InvalidateAll was built from the old sheets, so it is returned without being published:
        // storing it would undo the reset InvalidateAll just made.
        StyleSheetEntry[] sheetsSnapshot;
        int generation;
        lock (_sync)
        {
            if (_registrations is not null)
                return _registrations;
            sheetsSnapshot = [.. _sheets];
            generation = _cacheGeneration;
        }

        var registrations = new Dictionary<string, CustomPropertyRegistration>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in sheetsSnapshot)
            CollectPropertyRules(entry.Sheet.Rules, registrations);

        lock (_sync)
        {
            if (generation != _cacheGeneration)
                return registrations;
            return _registrations ??= registrations;
        }
    }

    private static void CollectPropertyRules(IReadOnlyList<CssRule> rules, Dictionary<string, CustomPropertyRegistration> registrations)
    {
        foreach (var rule in rules)
        {
            if (rule is not CssAtRule atRule)
                continue;

            if (atRule.Name.Equals("property", StringComparison.OrdinalIgnoreCase) &&
                atRule.Declarations is { } declarations)
            {
                var name = atRule.Prelude.Trim();
                if (name.StartsWith("--", StringComparison.Ordinal))
                {
                    var inheritsValue = declarations.GetPropertyValue("inherits");
                    registrations[name] = new CustomPropertyRegistration
                    {
                        Inherits = inheritsValue is null ||
                                   !inheritsValue.Trim().Equals("false", StringComparison.OrdinalIgnoreCase),
                        InitialValue = declarations.GetPropertyValue("initial-value"),
                    };
                }
            }

            if (atRule.Rules.Count > 0)
                CollectPropertyRules(atRule.Rules, registrations);
        }
    }

    // ---- Custom-property resolution ---------------------------------------

    /// <summary>
    /// Replaces the custom properties in <paramref name="computed"/>, the cascade of
    /// <paramref name="element"/> (or of its <paramref name="pseudoElement"/>), with the resolved ones:
    /// inherited, registered defaults applied, and <c>var()</c> and CSS-wide keywords resolved.
    /// </summary>
    private void MergeResolvedCustomProperties(Dictionary<string, string> computed, DomElement element,
        string? pseudoElement, Dictionary<string, CustomPropertyRegistration> registrations)
    {
        var resolvedForElement = GetResolvedCustomProperties(element, registrations);

        if (pseudoElement is null)
        {
            // The element's own map already holds every custom property its cascade declares, inline
            // style among them, so nothing in `computed` adds to it. Overlaying them again would also
            // let a cascade without inline style (the renderer's) put a sheet's value back over the
            // element's inline one.
            RemoveCustomProperties(computed);
            foreach (var kv in resolvedForElement)
                computed[kv.Key] = kv.Value;
            return;
        }

        // A pseudo-element's own custom properties go over its originating element's.
        Dictionary<string, string>? explicitCustomProperties = null;
        foreach (var kv in computed)
        {
            if (kv.Key.StartsWith("--", StringComparison.Ordinal))
                (explicitCustomProperties ??= new(StringComparer.OrdinalIgnoreCase))[kv.Key] = kv.Value;
        }

        var parentResolved = element.ParentElement is { } parentElement
            ? GetResolvedCustomProperties(parentElement, registrations)
            : null;
        var resolved = new Dictionary<string, string>(resolvedForElement, StringComparer.OrdinalIgnoreCase);
        if (explicitCustomProperties is not null)
        {
            foreach (var kv in explicitCustomProperties)
                resolved[kv.Key] = kv.Value;
        }

        FinalizeResolvedCustomProperties(resolved, parentResolved, registrations);

        RemoveCustomProperties(computed);
        foreach (var kv in resolved)
            computed[kv.Key] = kv.Value;
    }

    private static void RemoveCustomProperties(Dictionary<string, string> computed)
    {
        List<string>? customProperties = null;
        foreach (var key in computed.Keys)
        {
            if (key.StartsWith("--", StringComparison.Ordinal))
                (customProperties ??= []).Add(key);
        }

        if (customProperties is null)
            return;

        foreach (var key in customProperties)
            computed.Remove(key);
    }

    /// <summary>
    /// The resolved custom properties of <paramref name="element"/>: its parent's, less the registered
    /// ones that do not inherit, with its own cascaded ones (inline style included) over them, finalized.
    /// The map is shared and must not be changed.
    /// </summary>
    /// <remarks>
    /// An element's map is a function of its parent's and its own cascade, so it is memoized per element
    /// with the other caches' lifecycle. Each element's styles used to rebuild the maps of all its
    /// ancestors, twice, from their cascades, which made styling a document cost the sum of its
    /// elements' depths in cascades even on a page that declares no custom property at all: on
    /// html5test.com, 40% of each pointer move. The chain is walked up to the nearest ancestor already
    /// known and resolved downwards from there, so a deep tree costs no stack.
    /// </remarks>
    private IReadOnlyDictionary<string, string> GetResolvedCustomProperties(DomElement element,
        Dictionary<string, CustomPropertyRegistration> registrations)
    {
        var mode = RenderMode.Current;
        if (_customPropertyCache.TryGetValue((element, mode), out var cached))
            return cached;

        // Captured before any ancestor's map is read, so that a map derived from one an invalidation
        // made stale is returned but never stored.
        var generation = CaptureCacheGeneration();

        var chain = new List<DomElement>();
        var seen = new HashSet<DomElement>(ReferenceEqualityComparer.Instance);
        IReadOnlyDictionary<string, string>? inherited = null;
        for (var current = element; current is not null && seen.Add(current); current = current.ParentElement)
        {
            if (_customPropertyCache.TryGetValue((current, mode), out var known))
            {
                inherited = known;
                break;
            }

            chain.Add(current);
        }

        var resolved = inherited ?? EmptyReadOnlyMap;
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            resolved = ResolveCustomProperties(chain[index], resolved, registrations);
            StoreIfCurrent(_customPropertyCache, (chain[index], mode), resolved, generation);
        }

        return resolved;
    }

    /// <summary>How many elements' custom properties have been resolved: a test's measure of the memo.</summary>
    internal int CustomPropertyResolutionCount => _customPropertyResolutionCount;

    private int _customPropertyResolutionCount;

    private IReadOnlyDictionary<string, string> ResolveCustomProperties(DomElement element,
        IReadOnlyDictionary<string, string>? parentResolved, Dictionary<string, CustomPropertyRegistration> registrations)
    {
        Interlocked.Increment(ref _customPropertyResolutionCount);
        Dictionary<string, string>? resolved = null;
        if (parentResolved is { Count: > 0 })
        {
            resolved = new Dictionary<string, string>(parentResolved.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in parentResolved)
            {
                if (!registrations.TryGetValue(kv.Key, out var registration) || registration.Inherits)
                    resolved[kv.Key] = kv.Value;
            }
        }

        foreach (var kv in GetCascadedDeclarationMap(element, pseudoElement: null, includeInlineStyle: true))
        {
            if (kv.Key.StartsWith("--", StringComparison.Ordinal))
                (resolved ??= new(StringComparer.OrdinalIgnoreCase))[kv.Key] = kv.Value;
        }

        // Nothing inherited, declared or registered: the common case, and nothing to finalize.
        if (resolved is null && registrations.Count == 0)
            return EmptyReadOnlyMap;

        resolved ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        FinalizeResolvedCustomProperties(resolved, parentResolved, registrations);
        return resolved;
    }

    private static void FinalizeResolvedCustomProperties(Dictionary<string, string> resolved,
        IReadOnlyDictionary<string, string>? parentResolved, Dictionary<string, CustomPropertyRegistration> registrations)
    {
        for (var pass = 0; pass < MaxCustomPropertyResolutionPasses; pass++)
        {
            var changed = false;
            foreach (var key in resolved.Keys.Where(k => k.StartsWith("--", StringComparison.Ordinal)).ToList())
            {
                if (!resolved.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                    continue;

                var normalized = ResolveKnownCustomProperties(value, resolved);
                if (string.Equals(normalized, value, StringComparison.Ordinal))
                    continue;

                resolved[key] = normalized;
                changed = true;
            }

            if (ResolveCssWideKeywordCustomProperties(resolved, parentResolved, registrations))
                changed = true;
            if (ApplyRegisteredCustomPropertyDefaults(resolved, parentResolved, registrations))
                changed = true;

            if (!changed)
                break;
        }
    }

    private static bool ApplyRegisteredCustomPropertyDefaults(Dictionary<string, string> resolved,
        IReadOnlyDictionary<string, string>? parentResolved, Dictionary<string, CustomPropertyRegistration> registrations)
    {
        var changed = false;
        foreach (var (propertyName, registration) in registrations)
        {
            if (resolved.ContainsKey(propertyName))
                continue;

            if (registration.Inherits &&
                parentResolved != null &&
                parentResolved.TryGetValue(propertyName, out var inheritedValue))
            {
                resolved[propertyName] = inheritedValue;
                changed = true;
            }
            else if (!string.IsNullOrWhiteSpace(registration.InitialValue))
            {
                resolved[propertyName] = registration.InitialValue!;
                changed = true;
            }
        }

        return changed;
    }

    private static bool ResolveCssWideKeywordCustomProperties(Dictionary<string, string> resolved, 
        IReadOnlyDictionary<string, string>? parentResolved, Dictionary<string, CustomPropertyRegistration> registrations)
    {
        var changed = false;
        foreach (var key in resolved.Keys.Where(k => k.StartsWith("--", StringComparison.Ordinal)).ToList())
        {
            var value = resolved[key]?.Trim();
            if (string.IsNullOrEmpty(value))
                continue;

            var lower = value.ToLowerInvariant();
            if (lower is not ("initial" or "inherit" or "unset" or "revert" or "revert-layer"))
                continue;

            registrations.TryGetValue(key, out var registration);
            string? parentValue = null;
            parentResolved?.TryGetValue(key, out parentValue);

            string? replacement = lower switch
            {
                "initial" => registration?.InitialValue,
                "inherit" => parentValue ?? registration?.InitialValue,
                "unset" or "revert" or "revert-layer" => registration == null
                    ? parentValue
                    : registration.Inherits
                        ? parentValue ?? registration.InitialValue
                        : registration.InitialValue,
                _ => value,
            };

            if (string.IsNullOrWhiteSpace(replacement))
            {
                resolved.Remove(key);
                changed = true;
            }
            else if (!string.Equals(resolved[key], replacement, StringComparison.Ordinal))
            {
                resolved[key] = replacement;
                changed = true;
            }
            else
            {
                resolved[key] = replacement;
            }
        }

        return changed;
    }

    // ---- attr() length substitution ---------------------------------------

    /// <summary>
    /// Substitutes <c>attr(&lt;name&gt; type(&lt;length&gt;) [, &lt;fallback&gt;])</c>
    /// references in a computed-value map with the element's attribute value (or the
    /// fallback), keeping only recognizable lengths. Exposed as the single canonical
    /// implementation so bridge/layout consumers no longer maintain a private copy.
    /// </summary>
    public static void ResolveLengthAttrFunctions(IDictionary<string, string> computed, DomElement element)
    {
        foreach (var key in computed.Keys.ToList())
        {
            var value = computed[key];
            if (string.IsNullOrWhiteSpace(value) ||
                value.IndexOf("attr(", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            computed[key] = LengthAttrFunctionPattern.Replace(
                value,
                match =>
                {
                    var attrName = match.Groups["name"].Value;
                    var fallback = match.Groups["fallback"].Success
                        ? match.Groups["fallback"].Value.Trim()
                        : string.Empty;
                    var attributeValue = Attr(element, attrName)?.Trim() ?? string.Empty;

                    if (!string.IsNullOrEmpty(attributeValue) && IsRecognizedLengthValue(attributeValue))
                        return attributeValue;

                    if (!string.IsNullOrEmpty(fallback) && IsRecognizedLengthValue(fallback))
                        return fallback;

                    return string.Empty;
                });
        }
    }

    private static bool IsRecognizedLengthValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return trimmed == "0" || !double.IsNaN(CssLengthParser.ParseToPixels(trimmed));
    }

    // ---- Form-control approximate sizing -----------------------------------

    private static void ApplyApproximateFormControlComputedSizes(Dictionary<string, string> computed, DomElement element)
    {
        string tag = TagLower(element);
        if (tag is not ("input" or "button" or "select" or "textarea" or "progress" or "meter"))
            return;

        string writingMode = computed.GetValueOrDefault("writing-mode") ?? "horizontal-tb";
        bool vertical = CssWritingMode.IsVertical(writingMode);

        double logicalInlineSize = 60;
        double logicalBlockSize = 20;

        switch (tag)
        {
            case "input":
                string type = Attr(element, "type")?.ToLowerInvariant() ?? "text";
                switch (type)
                {
                    case "hidden":
                        logicalInlineSize = 0;
                        logicalBlockSize = 0;
                        break;
                    case "checkbox":
                    case "radio":
                        logicalInlineSize = 13;
                        logicalBlockSize = 13;
                        break;
                    case "submit":
                    case "button":
                    case "reset":
                        logicalInlineSize = 72;
                        logicalBlockSize = 20;
                        ApplyButtonLikeMultilineSizing(ref logicalBlockSize, Attr(element, "value"));
                        break;
                    default:
                        logicalInlineSize = 173;
                        logicalBlockSize = 16;
                        break;
                }
                break;
            case "button":
                logicalInlineSize = 72;
                logicalBlockSize = 20;
                ApplyButtonLikeMultilineSizing(ref logicalBlockSize, GetElementRenderedText(element));
                break;
            case "select":
                logicalInlineSize = 60;
                logicalBlockSize = 19;
                ApplySelectListBoxSizing(ref logicalInlineSize, ref logicalBlockSize, element);
                break;
            case "textarea":
                logicalInlineSize = 170;
                logicalBlockSize = 40;
                break;
            case "progress":
            case "meter":
                logicalInlineSize = 120;
                logicalBlockSize = 16;
                break;
        }

        double physicalWidth = vertical ? logicalBlockSize : logicalInlineSize;
        double physicalHeight = vertical ? logicalInlineSize : logicalBlockSize;

        if (!HasExplicitPhysicalOrLogicalSize(computed, "width", vertical ? "block-size" : "inline-size") && physicalWidth > 0)
            computed["width"] = FormatPx(physicalWidth);
        if (!HasExplicitPhysicalOrLogicalSize(computed, "height", vertical ? "inline-size" : "block-size") && physicalHeight > 0)
            computed["height"] = FormatPx(physicalHeight);
    }

    private static void ApplyButtonLikeMultilineSizing(ref double logicalBlockSize, string? rawText)
    {
        int lineCount = CountRenderedLines(rawText);
        if (lineCount <= 1)
            return;

        logicalBlockSize = 20 * lineCount;
    }

    private static void ApplySelectListBoxSizing(ref double logicalInlineSize, ref double logicalBlockSize, DomElement element)
    {
        int visibleRows = GetSelectVisibleRowCount(element);
        if (visibleRows <= 1)
            return;

        const double rowBlockSize = 16;
        const double chromeBlockSize = 4;
        logicalInlineSize = Math.Max(logicalInlineSize, 72);
        logicalBlockSize = (visibleRows * rowBlockSize) + chromeBlockSize;
    }

    private static int GetSelectVisibleRowCount(DomElement element)
    {
        bool isMultiple = element.HasAttribute("multiple");
        var rawSize = Attr(element, "size");
        if (rawSize != null &&
            int.TryParse(rawSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSize) &&
            parsedSize > 0)
        {
            return parsedSize;
        }

        return isMultiple ? 4 : 1;
    }

    private static int CountRenderedLines(string? rawText)
    {
        if (string.IsNullOrEmpty(rawText))
            return 1;

        return System.Net.WebUtility.HtmlDecode(rawText)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Length;
    }

    private static string GetElementRenderedText(DomElement element)
    {
        var builder = new StringBuilder();
        AppendRenderedText(element, builder);
        return builder.ToString();
    }

    private static void AppendRenderedText(DomElement element, StringBuilder builder)
    {
        foreach (var child in element.ChildNodes)
        {
            if (child is DomText text)
            {
                if (!string.IsNullOrEmpty(text.Data))
                    builder.Append(text.Data);
                continue;
            }

            if (child is DomElement childElement)
            {
                if (childElement.LocalName.Equals("br", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Append('\n');
                    continue;
                }

                AppendRenderedText(childElement, builder);
            }
        }
    }

    private static bool HasExplicitPhysicalOrLogicalSize(Dictionary<string, string> computed, string physicalProperty, string logicalProperty) =>
        HasExplicitSpecifiedSize(computed.GetValueOrDefault(physicalProperty)) ||
        HasExplicitSpecifiedSize(computed.GetValueOrDefault(logicalProperty));

    private static void ApplyLogicalSizeAliases(Dictionary<string, string> computed)
    {
        string writingMode = computed.GetValueOrDefault("writing-mode") ?? "horizontal-tb";
        bool vertical = CssWritingMode.IsVertical(writingMode);

        string width = computed.GetValueOrDefault("width") ?? "auto";
        string height = computed.GetValueOrDefault("height") ?? "auto";
        string inlineSize = computed.GetValueOrDefault("inline-size") ?? "auto";
        string blockSize = computed.GetValueOrDefault("block-size") ?? "auto";

        if (!HasExplicitSpecifiedSize(width))
            width = ResolveLogicalPhysicalFallback(width, vertical ? blockSize : inlineSize);

        if (!HasExplicitSpecifiedSize(height))
            height = ResolveLogicalPhysicalFallback(height, vertical ? inlineSize : blockSize);

        computed["width"] = width;
        computed["height"] = height;
        computed["block-size"] = HasExplicitSpecifiedSize(blockSize) ? blockSize : (vertical ? width : height);
        computed["inline-size"] = HasExplicitSpecifiedSize(inlineSize) ? inlineSize : (vertical ? height : width);
    }

    private static string ResolveLogicalPhysicalFallback(string currentPhysicalValue, string mappedLogicalValue) =>
        HasExplicitSpecifiedSize(mappedLogicalValue) ? mappedLogicalValue : currentPhysicalValue;

    private static bool HasExplicitSpecifiedSize(string? value)
    {
        value = value?.Trim() ?? string.Empty;
        return value.Length > 0 && !string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPx(double value) =>
        $"{Math.Round(value).ToString(CultureInfo.InvariantCulture)}px";

    // ---- Mutation-driven invalidation -------------------------------------

    private void ObserveDocument(DomElement element)
    {
        if (element.OwnerDocument is not { } document)
            return;

        bool firstObservation;
        lock (_sync)
            firstObservation = _observedDocuments.Add(document);

        // Subscribe outside the lock (event add is itself synchronized); the Add guard ensures
        // exactly one subscription per document even under concurrent first-time observation.
        if (firstObservation)
            document.Mutated += OnDocumentMutated;
    }

    private void OnDocumentMutated(DomMutationRecord record) => InvalidateAll();

    private void InvalidateAll()
    {
        // The generation bump and _registrations reset are under _sync (reentrant: callers such as
        // AddStyleSheet already hold it). Bump first, inside the lock, and clear the memos inside
        // it too: the caches are concurrent now and would not need the lock for their own sake,
        // but the generation-guarded stores in GetCascadedStyle / GetCascadedDeclarationMap
        // compare and publish under this same lock, and a clear that slipped between their
        // compare and their store would leave a stale entry behind for good.
        lock (_sync)
        {
            _cacheGeneration++;
            _registrations = null;
            if (!_cache.IsEmpty)
                _cache.Clear();
            if (!_declaredCascadeCache.IsEmpty)
                _declaredCascadeCache.Clear();
            if (!_cascadedStyleCache.IsEmpty)
                _cascadedStyleCache.Clear();
            if (!_sparseCache.IsEmpty)
                _sparseCache.Clear();
            if (!_customPropertyCache.IsEmpty)
                _customPropertyCache.Clear();
        }
    }

    // ---- Property metadata -------------------------------------------------

    // Canonical initial-value and inherited-property tables live in the shared
    // CssComputedDefaults so the engine and bridge/layout consumers cannot drift.
    private static readonly IReadOnlyDictionary<string, string> CssInitialValues = CssComputedDefaults.InitialValues;

    private static readonly IReadOnlySet<string> CssInheritedProperties = CssComputedDefaults.InheritedProperties;

    [GeneratedRegex(@"attr\(\s*(?<name>[A-Za-z_][A-Za-z0-9_-]*)\s+type\(\s*<length>\s*\)\s*(?:,\s*(?<fallback>[^)]+?))?\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LengthAttrRegex();
}
