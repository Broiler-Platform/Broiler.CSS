using System;

namespace Broiler.CSS.Dom;

/// <summary>
/// Shared CSS declaration-value validator implementing the CSS error-recovery
/// rules (CSS Syntax §4 / CSS 2.1 §4.1.8): a declaration whose value is invalid
/// for its property is dropped so a previously cascaded valid value wins.
/// </summary>
/// <remarks>
/// This is the single canonical entry point for closed-keyword value validation.
/// It projects the cascade engine's own <see cref="CssStyleEngine"/> logic so
/// consumers such as the HtmlBridge inline-style ingestion path validate against
/// exactly the same table the cascade uses, instead of maintaining a parallel,
/// narrower copy that can drift.
/// </remarks>
public static class CssDeclarationValidator
{
    /// <summary>
    /// Returns <c>false</c> for values that are clearly invalid for the given
    /// property. Only properties with a closed set of keyword values are
    /// validated; every other property accepts any non-empty value. CSS-wide
    /// keywords (<c>inherit</c>/<c>initial</c>/<c>unset</c>/<c>revert</c>) and
    /// deferred substitutions (<c>var()</c>/<c>env()</c>) are always accepted and
    /// resolved later.
    /// </summary>
    /// <param name="property">The CSS property name (case-insensitive).</param>
    /// <param name="value">
    /// The declaration value with its <c>!important</c> flag already stripped;
    /// importance is tracked separately by callers.
    /// </param>
    public static bool IsAcceptableDeclarationValue(string property, string value)
    {
        ArgumentNullException.ThrowIfNull(property);
        return CssStyleEngine.IsAcceptableDeclarationValue(property, value);
    }
}
