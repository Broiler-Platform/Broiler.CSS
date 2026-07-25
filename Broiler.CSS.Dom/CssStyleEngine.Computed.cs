using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        if (_registrations is not null)
            return _registrations;

        var registrations = new Dictionary<string, CustomPropertyRegistration>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _sheets)
            CollectPropertyRules(entry.Sheet.Rules, registrations);

        _registrations = registrations;
        return registrations;
    }

    private static void CollectPropertyRules(IReadOnlyList<CssRule> rules, Dictionary<string, CustomPropertyRegistration> registrations)
    {
        foreach (var rule in rules)
        {
            if (rule is CssAtRule atRule)
            {
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
    }

    // ---- Custom-property resolution ---------------------------------------

    private void MergeResolvedCustomProperties(Dictionary<string, string> computed, DomElement element,
        Dictionary<string, CustomPropertyRegistration> registrations, HashSet<DomElement> ancestorsInProgress)
    {
        var explicitCustomProperties = computed
            .Where(kv => kv.Key.StartsWith("--", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        var parentElement = ParentElement(element);
        var parentResolved = parentElement != null
            ? BuildResolvedCustomPropertyMap(parentElement, registrations)
            : null;
        var resolved = BuildResolvedCustomPropertyMap(element, registrations);

        foreach (var kv in explicitCustomProperties)
            resolved[kv.Key] = kv.Value;

        FinalizeResolvedCustomProperties(resolved, parentResolved, registrations);

        foreach (var key in computed.Keys.Where(k => k.StartsWith("--", StringComparison.Ordinal)).ToList())
            computed.Remove(key);

        foreach (var kv in resolved)
            computed[kv.Key] = kv.Value;
    }

    private Dictionary<string, string> BuildResolvedCustomPropertyMap(DomElement element, Dictionary<string, CustomPropertyRegistration> registrations)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? parentResolved = null;

        var parentElement = ParentElement(element);
        if (parentElement != null)
        {
            parentResolved = BuildResolvedCustomPropertyMap(parentElement, registrations);
            foreach (var kv in parentResolved)
            {
                if (!registrations.TryGetValue(kv.Key, out var registration) || registration.Inherits)
                    resolved[kv.Key] = kv.Value;
            }
        }

        foreach (var (name, value) in CollectLocalDeclarations(element))
        {
            if (name.StartsWith("--", StringComparison.Ordinal))
                resolved[name] = value;
        }

        FinalizeResolvedCustomProperties(resolved, parentResolved, registrations);
        return resolved;
    }

    private IEnumerable<(string Name, string Value)> CollectLocalDeclarations(DomElement element)
    {
        var local = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CollectCascadedDeclarations(element, pseudoElement: null, local);

        var inline = Attr(element, "style");
        if (!string.IsNullOrEmpty(inline))
        {
            foreach (var (name, value, _) in ParseDeclarations(inline))
                local[name] = value;
        }

        return local.Select(kv => (kv.Key, kv.Value));
    }

    private static void FinalizeResolvedCustomProperties(Dictionary<string, string> resolved,
        Dictionary<string, string>? parentResolved, Dictionary<string, CustomPropertyRegistration> registrations)
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
        Dictionary<string, string>? parentResolved, Dictionary<string, CustomPropertyRegistration> registrations)
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
        Dictionary<string, string>? parentResolved, Dictionary<string, CustomPropertyRegistration> registrations)
    {
        var changed = false;
        foreach (var key in resolved.Keys.Where(k => k.StartsWith("--", StringComparison.Ordinal)).ToList())
        {
            var value = resolved[key]?.Trim();
            if (string.IsNullOrEmpty(value))
                continue;

            var lower = value.ToLowerInvariant();
            if (lower is not ("initial" or "inherit" or "unset" or "revert"))
                continue;

            registrations.TryGetValue(key, out var registration);
            string? parentValue = null;
            parentResolved?.TryGetValue(key, out parentValue);

            string? replacement = lower switch
            {
                "initial" => registration?.InitialValue,
                "inherit" => parentValue ?? registration?.InitialValue,
                "unset" or "revert" => registration == null
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
        bool vertical = IsVerticalWritingMode(writingMode);

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
                        ApplyButtonLikeMultilineSizing(ref logicalInlineSize, ref logicalBlockSize, Attr(element, "value"));
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
                ApplyButtonLikeMultilineSizing(ref logicalInlineSize, ref logicalBlockSize, GetElementRenderedText(element));
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

    private static void ApplyButtonLikeMultilineSizing(ref double logicalInlineSize, ref double logicalBlockSize, string? rawText)
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
        bool vertical = IsVerticalWritingMode(writingMode);

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

    private static bool IsVerticalWritingMode(string? writingMode)
    {
        var normalized = writingMode?.Trim().ToLowerInvariant();
        return normalized is "vertical-rl" or "vertical-lr" or "sideways-rl" or "sideways-lr";
    }

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
        if (element.OwnerDocument is { } document && _observedDocuments.Add(document))
            document.Mutated += OnDocumentMutated;
    }

    private void OnDocumentMutated(DomMutationRecord record) => InvalidateAll();

    private void InvalidateAll()
    {
        // Bump first: a declared-cascade computation in flight captured the prior
        // generation and will decline to memoize its (now-stale) result.
        _cacheGeneration++;
        _registrations = null;
        if (_cache.Count > 0)
            _cache.Clear();
        if (_declaredCascadeCache.Count > 0)
            _declaredCascadeCache.Clear();
        if (_sparseCache.Count > 0)
            _sparseCache.Clear();
    }

    // ---- Property metadata -------------------------------------------------

    // Canonical initial-value and inherited-property tables live in the shared
    // CssComputedDefaults so the engine and bridge/layout consumers cannot drift.
    private static readonly IReadOnlyDictionary<string, string> CssInitialValues = CssComputedDefaults.InitialValues;

    private static readonly IReadOnlySet<string> CssInheritedProperties = CssComputedDefaults.InheritedProperties;

    [GeneratedRegex(@"attr\(\s*(?<name>[A-Za-z_][A-Za-z0-9_-]*)\s+type\(\s*<length>\s*\)\s*(?:,\s*(?<fallback>[^)]+?))?\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LengthAttrRegex();
}
