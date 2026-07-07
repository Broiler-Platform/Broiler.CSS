#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Broiler.CSS;

public static class RendererStyleQueries
{
    public sealed record FontFace(string Family, string Source, string FeatureSettings);

    private sealed record Metadata(
        IReadOnlyList<FontFace> FontFaces,
        IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, int[]>>> FontFeatureValues,
        bool HasBeforeRules,
        bool HasAfterRules);

    private static readonly ConditionalWeakTable<CssStyleSheet, Metadata> Cache = [];

    public static IReadOnlyList<FontFace> GetFontFaces(CssStyleSheet styleSheet) =>
        Cache.GetValue(styleSheet, ParseMetadata).FontFaces;

    public static IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, int[]>>> GetFontFeatureValues(
        CssStyleSheet styleSheet) =>
        Cache.GetValue(styleSheet, ParseMetadata).FontFeatureValues;

    public static bool HasGeneratedPseudoElementRules(CssStyleSheet styleSheet, bool before)
    {
        var metadata = Cache.GetValue(styleSheet, ParseMetadata);
        return before ? metadata.HasBeforeRules : metadata.HasAfterRules;
    }

    public static string UnescapeIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0)
            return value ?? string.Empty;

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length;)
        {
            if (value[index] != '\\')
            {
                builder.Append(value[index++]);
                continue;
            }

            index++;
            if (index >= value.Length)
                break;

            var start = index;
            var count = 0;
            while (index < value.Length && count < 6 && Uri.IsHexDigit(value[index]))
            {
                index++;
                count++;
            }

            if (count == 0)
            {
                builder.Append(value[index++]);
                continue;
            }

            var codePoint = Convert.ToInt32(value.Substring(start, count), 16);
            if (codePoint > 0 && codePoint <= 0x10ffff)
                builder.Append(char.ConvertFromUtf32(codePoint));
            if (index < value.Length && char.IsWhiteSpace(value[index]))
                index++;
        }

        return builder.ToString();
    }

    private static Metadata ParseMetadata(CssStyleSheet styleSheet)
    {
        var faces = new List<FontFace>();
        var featureValues = new Dictionary<string, Dictionary<string, Dictionary<string, int[]>>>(
            StringComparer.OrdinalIgnoreCase);

        var allRules = EnumerateRules(styleSheet.Rules).ToArray();
        var hasBeforeRules = allRules.OfType<CssStyleRule>().Any(static rule =>
            rule.Selectors.Selectors.Any(static selector =>
                selector.Text.Contains(":before", StringComparison.OrdinalIgnoreCase)));
        var hasAfterRules = allRules.OfType<CssStyleRule>().Any(static rule =>
            rule.Selectors.Selectors.Any(static selector =>
                selector.Text.Contains(":after", StringComparison.OrdinalIgnoreCase)));

        foreach (var atRule in allRules.OfType<CssAtRule>())
        {
            if (atRule.Name == "font-face" && atRule.Declarations is not null)
            {
                var family = Unquote(atRule.Declarations.GetPropertyValue("font-family"));
                var source = ExtractFirstUrl(atRule.Declarations.GetPropertyValue("src"));
                var features = atRule.Declarations.GetPropertyValue("font-feature-settings") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(family) && !string.IsNullOrWhiteSpace(source))
                    faces.Add(new FontFace(UnescapeIdentifier(family), source, features));
            }
            else if (atRule.Name == "font-feature-values" && !string.IsNullOrWhiteSpace(atRule.BlockText))
            {
                ParseFontFeatureValues(atRule, featureValues);
            }
        }

        return new Metadata(faces, featureValues, hasBeforeRules, hasAfterRules);
    }

    private static IEnumerable<CssRule> EnumerateRules(IEnumerable<CssRule> rules)
    {
        foreach (var rule in rules)
        {
            yield return rule;
            if (rule is CssAtRule atRule)
            {
                foreach (var nested in EnumerateRules(atRule.Rules))
                    yield return nested;
            }
        }
    }

    private static void ParseFontFeatureValues(
        CssAtRule atRule,
        Dictionary<string, Dictionary<string, Dictionary<string, int[]>>> destination)
    {
        foreach (var rawFamily in atRule.Prelude.Split(','))
        {
            var family = UnescapeIdentifier(Unquote(rawFamily.Trim()));
            if (family.Length == 0)
                continue;

            if (!destination.TryGetValue(family, out var typeMap))
            {
                typeMap = new Dictionary<string, Dictionary<string, int[]>>(StringComparer.OrdinalIgnoreCase);
                destination[family] = typeMap;
            }

            var body = atRule.BlockText!;
            for (var index = 0; index < body.Length;)
            {
                var at = body.IndexOf('@', index);
                if (at < 0)
                    break;
                var open = body.IndexOf('{', at);
                if (open < 0)
                    break;
                var close = FindMatchingBrace(body, open);
                if (close < 0)
                    break;

                var type = body.Substring(at + 1, open - at - 1).Trim().ToLowerInvariant();
                if (!typeMap.TryGetValue(type, out var nameMap))
                {
                    nameMap = new Dictionary<string, int[]>(StringComparer.Ordinal);
                    typeMap[type] = nameMap;
                }

                var declarations = new CssParser().ParseDeclarations(body.Substring(open + 1, close - open - 1));
                foreach (var declaration in declarations.Declarations)
                {
                    var values = declaration.Value.Text
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .Select(static part => int.TryParse(part, out var value) ? (int?)value : null)
                        .Where(static value => value.HasValue)
                        .Select(static value => value!.Value)
                        .ToArray();
                    if (values.Length > 0)
                        nameMap[UnescapeIdentifier(declaration.Name)] = values;
                }

                index = close + 1;
            }
        }
    }

    private static int FindMatchingBrace(string value, int openIndex)
    {
        var depth = 0;
        for (var index = openIndex; index < value.Length; index++)
        {
            if (value[index] == '{')
                depth++;
            else if (value[index] == '}' && --depth == 0)
                return index;
        }
        return -1;
    }

    private static string ExtractFirstUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var start = value.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return Unquote(value.Trim());
        start += 4;
        var end = value.IndexOf(')', start);
        return end < 0 ? string.Empty : Unquote(value[start..end].Trim());
    }

    private static string Unquote(string? value)
    {
        value = value?.Trim() ?? string.Empty;
        return value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;
    }
}
