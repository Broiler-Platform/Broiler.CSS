namespace Broiler.CSS.Dom;

/// <summary>
/// Host-supplied loader that resolves and fetches external stylesheets for
/// <c>@import</c> rules.
/// </summary>
public interface ICssStyleSheetLoader
{
    /// <summary>
    /// Loads the CSS text for the given <paramref name="href"/>, optionally
    /// resolved against <paramref name="referrerUrl"/>.
    /// Returns the CSS text, or <see langword="null"/> if the stylesheet is
    /// unavailable, blocked by security policy, or has reached an import cycle.
    /// </summary>
    /// <param name="href">The target stylesheet URI or specifier.</param>
    /// <param name="referrerUrl">
    /// The URI of the importing stylesheet, or <see langword="null"/> if imported
    /// from a document-level stylesheet.
    /// </param>
    string? LoadStyleSheet(string href, string? referrerUrl = null);
}
