using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.CSS.Cssom;

namespace Broiler.CSS.Dom;

/// <summary>
/// Resolves and expands leading <c>@import</c> rules according to CSS Cascade 5 §2.
/// </summary>
public static class CssImportResolver
{
    /// <summary>
    /// Default maximum nesting depth for recursive <c>@import</c> resolution.
    /// </summary>
    public const int DefaultMaxDepth = 32;

    /// <summary>
    /// Resolves leading <c>@import</c> rules in <paramref name="sheet"/> using the provided
    /// <paramref name="loader"/>, returning a new <see cref="CssStyleSheet"/> with imports expanded.
    /// If no <c>@import</c> rules are present, returns <paramref name="sheet"/> unchanged.
    /// </summary>
    public static CssStyleSheet ResolveImports(
        CssStyleSheet sheet,
        ICssStyleSheetLoader loader,
        string? baseUrl = null,
        int maxDepth = DefaultMaxDepth)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(loader);

        var resolvedRules = ResolveImports(sheet.Rules, loader, baseUrl, maxDepth);
        if (ReferenceEquals(resolvedRules, sheet.Rules))
            return sheet;

        return new CssStyleSheet(resolvedRules, sheet.Diagnostics);
    }

    /// <summary>
    /// Resolves leading <c>@import</c> rules in <paramref name="rules"/> using the provided
    /// <paramref name="loader"/>, returning the expanded rule list.
    /// </summary>
    public static IReadOnlyList<CssRule> ResolveImports(
        IReadOnlyList<CssRule> rules,
        ICssStyleSheetLoader loader,
        string? baseUrl = null,
        int maxDepth = DefaultMaxDepth)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(loader);

        if (rules.Count == 0)
            return rules;

        var hasImports = false;
        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i] is CssAtRule at && at.Name.Equals("import", StringComparison.OrdinalIgnoreCase))
            {
                hasImports = true;
                break;
            }
        }

        if (!hasImports)
            return rules;

        var chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(baseUrl))
            chain.Add(baseUrl);

        return ResolveImportsCore(rules, loader, baseUrl, depth: 0, maxDepth, chain);
    }

    private static List<CssRule> ResolveImportsCore(
        IReadOnlyList<CssRule> rules,
        ICssStyleSheetLoader loader,
        string? referrerUrl,
        int depth,
        int maxDepth,
        HashSet<string> chain)
    {
        var result = new List<CssRule>(rules.Count);
        var importsAllowed = true;
        var sawImport = false;

        foreach (var rule in rules)
        {
            if (rule is CssAtRule atRule)
            {
                var isCharset = atRule.Name.Equals("charset", StringComparison.OrdinalIgnoreCase);
                var isLayerStatement = atRule.Name.Equals("layer", StringComparison.OrdinalIgnoreCase) && !atRule.HasBlock;
                var isImport = atRule.Name.Equals("import", StringComparison.OrdinalIgnoreCase);

                if (isCharset)
                {
                    result.Add(rule);
                    continue;
                }

                if (isLayerStatement)
                {
                    if (sawImport)
                        importsAllowed = false;
                    result.Add(rule);
                    continue;
                }

                if (isImport)
                {
                    if (importsAllowed)
                    {
                        sawImport = true;
                        ExpandSingleImport(atRule, loader, referrerUrl, depth, maxDepth, chain, result);
                    }
                    // Misplaced @import after other rules: invalid per CSS Cascade 5 §2; ignored.
                    continue;
                }

                // Any other at-rule (@media, @supports, @layer with block, etc.) ends the import sequence.
                importsAllowed = false;
                result.Add(rule);
            }
            else
            {
                // Style rules end the import sequence.
                importsAllowed = false;
                result.Add(rule);
            }
        }

        return result;
    }

    private static void ExpandSingleImport(
        CssAtRule atRule,
        ICssStyleSheetLoader loader,
        string? referrerUrl,
        int depth,
        int maxDepth,
        HashSet<string> chain,
        List<CssRule> result)
    {
        var meta = CssomRuleMetadata.GetImport(atRule);
        if (string.IsNullOrWhiteSpace(meta.Href))
        {
            if (meta.Layer == CssImportLayer.Named && meta.LayerName is not null)
                result.Add(CreateLayerStatement(meta.LayerName));
            return;
        }

        // CSS Cascade 5 §2: supports() condition must be evaluated before fetching.
        // If false, the stylesheet must not be fetched.
        if (meta.Supports is not null && !EvaluatesSupports(meta.Supports))
        {
            if (meta.Layer == CssImportLayer.Named && meta.LayerName is not null)
                result.Add(CreateLayerStatement(meta.LayerName));
            return;
        }

        if (depth >= maxDepth)
        {
            if (meta.Layer == CssImportLayer.Named && meta.LayerName is not null)
                result.Add(CreateLayerStatement(meta.LayerName));
            return;
        }

        var targetUrl = ResolveUrl(meta.Href, referrerUrl);
        if (!chain.Add(targetUrl))
        {
            // Cycle detected.
            if (meta.Layer == CssImportLayer.Named && meta.LayerName is not null)
                result.Add(CreateLayerStatement(meta.LayerName));
            return;
        }

        try
        {
            var cssText = loader.LoadStyleSheet(meta.Href, referrerUrl);
            if (cssText is null)
            {
                // Unavailable, blocked by CSP, etc.
                if (meta.Layer == CssImportLayer.Named && meta.LayerName is not null)
                    result.Add(CreateLayerStatement(meta.LayerName));
                return;
            }

            var importedSheet = new CssParser().ParseStyleSheet(cssText);
            var resolvedChildren = ResolveImportsCore(
                importedSheet.Rules,
                loader,
                targetUrl,
                depth + 1,
                maxDepth,
                chain);

            // Filter out any top-level @charset from imported sheets.
            var cleanedChildren = resolvedChildren.Where(static r =>
                !(r is CssAtRule at && at.Name.Equals("charset", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            IReadOnlyList<CssRule> rulesToWrap;
            if (meta.Layer == CssImportLayer.Named && meta.LayerName is not null)
            {
                var layerRule = new CssAtRule("layer", meta.LayerName, blockText: string.Empty, declarations: null, rules: cleanedChildren, default);
                rulesToWrap = [layerRule];
            }
            else if (meta.Layer == CssImportLayer.Anonymous)
            {
                var layerRule = new CssAtRule("layer", string.Empty, blockText: string.Empty, declarations: null, rules: cleanedChildren, default);
                rulesToWrap = [layerRule];
            }
            else
            {
                rulesToWrap = cleanedChildren;
            }

            if (!string.IsNullOrWhiteSpace(meta.Media))
            {
                var mediaRule = new CssAtRule("media", meta.Media.Trim(), blockText: string.Empty, declarations: null, rules: rulesToWrap, default);
                result.Add(mediaRule);
            }
            else
            {
                result.AddRange(rulesToWrap);
            }
        }
        finally
        {
            chain.Remove(targetUrl);
        }
    }

    private static CssAtRule CreateLayerStatement(string layerName) =>
        new("layer", layerName, blockText: null, declarations: null, rules: null, default);

    private static bool EvaluatesSupports(string rawCondition)
    {
        if (string.IsNullOrWhiteSpace(rawCondition))
            return false;

        var cleaned = CssSyntax.RemoveComments(rawCondition).Trim();
        if (cleaned.Length == 0)
            return false;

        if (CssStyleEngine.EvaluatesSupportsCondition(cleaned))
            return true;

        if (!cleaned.StartsWith('(') || !cleaned.EndsWith(')'))
            return CssStyleEngine.EvaluatesSupportsCondition($"({cleaned})");

        return false;
    }

    private static string ResolveUrl(string href, string? referrerUrl)
    {
        if (string.IsNullOrWhiteSpace(referrerUrl))
            return href;

        if (Uri.TryCreate(referrerUrl, UriKind.Absolute, out var baseUri))
        {
            if (Uri.TryCreate(baseUri, href, out var resolvedUri))
                return resolvedUri.AbsoluteUri;
        }
        else
        {
            if (href.Contains("://", StringComparison.Ordinal) || href.StartsWith('/'))
                return href;

            var lastSlash = referrerUrl.LastIndexOf('/');
            if (lastSlash >= 0)
                return referrerUrl[..(lastSlash + 1)] + href;
        }

        return href;
    }
}
