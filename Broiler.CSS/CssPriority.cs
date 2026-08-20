using System;
using System.Text.RegularExpressions;

namespace Broiler.CSS;

/// <summary>
/// String-level handling of the CSS declaration <c>!important</c> priority for
/// CSSOM value manipulation — splitting a raw property value into its value and
/// priority, and re-attaching a priority. The trailing <c>!important</c> is
/// matched leniently (optional whitespace around the <c>!</c> and before the
/// keyword, case-insensitive), mirroring what browsers accept.
///
/// This is the canonical home used by <c>Broiler.HtmlBridge</c>'s CSSOM
/// <c>CSSStyleDeclaration</c> surface (<c>getPropertyValue</c> /
/// <c>getPropertyPriority</c> / <c>setProperty</c>) instead of a bridge-private
/// copy (HtmlBridge DOM/CSS promotion roadmap, Phase 1 slice 2). The parse-time
/// declaration path (<c>CssParser</c>) has its own internal handling; this type
/// is the reusable string utility for post-parse CSSOM string values.
/// </summary>
public static partial class CssPriority
{
    private static readonly Regex ImportantSuffixPattern = ImportantSuffixRegex();

    /// <summary>
    /// Returns <paramref name="value"/> with any trailing <c>!important</c> removed
    /// and surrounding whitespace trimmed. Returns the empty string for a null or
    /// empty input.
    /// </summary>
    public static string Strip(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : ImportantSuffixPattern.Replace(value, string.Empty).Trim();

    /// <summary>
    /// Returns <c>"important"</c> when <paramref name="value"/> ends with an
    /// <c>!important</c> priority, otherwise the empty string — matching the CSSOM
    /// <c>getPropertyPriority</c> contract.
    /// </summary>
    public static string Parse(string? value)
        => !string.IsNullOrEmpty(value) && ImportantSuffixPattern.IsMatch(value) ? "important" : string.Empty;

    /// <summary>
    /// Returns <paramref name="value"/> (with any existing priority stripped) plus a
    /// trailing <c>!important</c> when <paramref name="priority"/> is <c>"important"</c>
    /// (case-insensitive, trimmed); otherwise returns the stripped value alone.
    /// </summary>
    public static string Apply(string value, string? priority)
        => string.Equals(priority?.Trim(), "important", StringComparison.OrdinalIgnoreCase)
            ? $"{Strip(value)} !important".Trim()
            : Strip(value);

    [GeneratedRegex(@"\s*!\s*important\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex ImportantSuffixRegex();
}
