using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Broiler.CSS;

/// <summary>
/// Parsing helpers for the CSS <c>animation</c> shorthand and its component
/// syntaxes — the <c>&lt;time&gt;</c> value, shorthand tokenization (splitting on
/// top-level whitespace while keeping <c>fn(...)</c> groups intact), and the
/// keyword classifiers used to assign an untyped shorthand token to a longhand.
/// </summary>
/// <remarks>
/// Promoted from the HtmlBridge animation resolver: pure CSS syntax parsing with
/// no DOM or JavaScript coupling. Timing-function keyword knowledge lives with the
/// evaluator in <see cref="CssEasing"/>.
/// </remarks>
public static class CssAnimation
{
    /// <summary>
    /// Parses a CSS <c>&lt;time&gt;</c> value (<c>s</c> or <c>ms</c>) into seconds.
    /// Returns <see langword="false"/> for anything that is not a valid time.
    /// </summary>
    public static bool TryParseTime(string text, out double seconds)
    {
        seconds = 0;
        var lower = text.Trim().ToLowerInvariant();

        if (lower.EndsWith("ms"))
        {
            if (double.TryParse(lower.AsSpan(0, lower.Length - 2),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
            {
                seconds = ms / 1000.0;
                return true;
            }
        }
        else if (lower.EndsWith('s'))
        {
            if (double.TryParse(lower.AsSpan(0, lower.Length - 1),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            {
                seconds = s;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Splits an <c>animation</c> shorthand into its top-level whitespace-separated
    /// tokens, keeping parenthesized function bodies (e.g. <c>cubic-bezier(…)</c>,
    /// <c>steps(…)</c>) as a single token.
    /// </summary>
    public static IReadOnlyList<string> TokenizeShorthand(string shorthand)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var depth = 0;

        foreach (var ch in shorthand.Trim())
        {
            if (char.IsWhiteSpace(ch) && depth == 0)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            if (ch == '(')
                depth++;
            else if (ch == ')' && depth > 0)
                depth--;

            current.Append(ch);
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    /// <summary>
    /// Whether an <c>animation</c> shorthand token is an <c>&lt;easing-function&gt;</c>
    /// (a named keyword or a <c>steps()</c>/<c>cubic-bezier()</c> function). Evaluation
    /// of the function is owned by <see cref="CssEasing"/>.
    /// </summary>
    public static bool IsTimingFunction(string text) => text switch
    {
        "ease" or "linear" or "ease-in" or "ease-out" or "ease-in-out"
            or "step-start" or "step-end" => true,
        _ when text.StartsWith("steps(", System.StringComparison.OrdinalIgnoreCase) => true,
        _ when text.StartsWith("cubic-bezier(", System.StringComparison.OrdinalIgnoreCase) => true,
        _ => false,
    };

    /// <summary>
    /// Whether an <c>animation</c> shorthand token is one of the non-time, non-name
    /// keywords (direction, fill-mode, play-state, iteration <c>infinite</c>).
    /// </summary>
    public static bool IsKnownKeyword(string text) => text switch
    {
        "normal" or "reverse" or "alternate" or "alternate-reverse"
            or "none" or "forwards" or "backwards" or "both"
            or "running" or "paused" or "infinite" => true,
        _ => false,
    };
}
