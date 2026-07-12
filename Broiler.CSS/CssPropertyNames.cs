using System.Text;

namespace Broiler.CSS;

/// <summary>
/// Conversions between the two spellings of a CSS property name: the CSS
/// <em>kebab-case</em> spelling (<c>background-color</c>, <c>z-index</c>) and the
/// DOM/CSSOM <em>camelCase</em> spelling used by <c>element.style</c> and
/// <c>getComputedStyle(...)</c> (<c>backgroundColor</c>, <c>zIndex</c>). A leading
/// capital maps to a leading hyphen so vendor-prefixed accessors round-trip
/// (<c>WebkitTransform</c> ↔ <c>-webkit-transform</c>).
///
/// This is the single canonical home for the mapping; <c>Broiler.HtmlBridge</c>'s
/// CSSOM wrappers route through it instead of each carrying a private copy
/// (HtmlBridge DOM/CSS promotion roadmap, Phase 1 slice 2).
/// </summary>
public static class CssPropertyNames
{
    /// <summary>
    /// Converts a DOM/CSSOM camelCase property name to its CSS kebab-case spelling
    /// (e.g. <c>backgroundColor</c> → <c>background-color</c>, <c>WebkitTransform</c>
    /// → <c>-webkit-transform</c>). Each uppercase character becomes <c>-</c> plus
    /// its lowercase form; all other characters are passed through unchanged.
    /// </summary>
    public static string ToCssPropertyName(string domName)
    {
        var sb = new StringBuilder(domName.Length + 4);
        foreach (char c in domName)
        {
            if (char.IsUpper(c))
            {
                sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Converts a CSS kebab-case property name to its DOM/CSSOM camelCase spelling
    /// (e.g. <c>background-color</c> → <c>backgroundColor</c>, <c>-webkit-transform</c>
    /// → <c>WebkitTransform</c>). Each <c>-</c> is dropped and uppercases the next
    /// character; all other characters are passed through unchanged.
    /// </summary>
    public static string ToDomPropertyName(string cssName)
    {
        var sb = new StringBuilder(cssName.Length);
        bool upper = false;
        foreach (char c in cssName)
        {
            if (c == '-') { upper = true; continue; }
            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }
        return sb.ToString();
    }
}
