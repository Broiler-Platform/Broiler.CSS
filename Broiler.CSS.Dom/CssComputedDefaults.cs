using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Broiler.CSS.Dom;

/// <summary>
/// Canonical CSS property metadata shared by the cascade/computed-style engine
/// and by consumers (such as the HtmlBridge layout/anchor projections) that need
/// the same initial values and inherited-property set. Publishing these tables
/// here removes the parallel bridge-local copies that could — and did — drift
/// from <see cref="CssStyleEngine"/>.
/// </summary>
public static class CssComputedDefaults
{
    /// <summary>
    /// Initial (default) computed values for the properties Broiler models.
    /// <c>getComputedStyle()</c> returns these when no rule, inline style, or
    /// inheritance supplies a value.
    /// </summary>
    public static IReadOnlyDictionary<string, string> InitialValues { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["display"] = "inline",
            ["position"] = "static",
            ["float"] = "none",
            ["visibility"] = "visible",
            ["overflow"] = "visible",
            ["overflow-x"] = "visible",
            ["overflow-y"] = "visible",
            ["text-transform"] = "none",
            ["text-decoration"] = "none",
            ["text-align"] = "start",
            ["text-align-last"] = "auto",
            ["text-indent"] = "0px",
            ["text-shadow"] = "none",
            ["white-space"] = "normal",
            ["cursor"] = "auto",
            ["font-style"] = "normal",
            ["font-variant"] = "normal",
            ["font-weight"] = "normal",
            ["font-size"] = "16px",
            ["font-family"] = "serif",
            ["line-height"] = "normal",
            ["letter-spacing"] = "normal",
            ["word-spacing"] = "normal",
            ["color"] = "rgb(0, 0, 0)",
            ["background-color"] = "rgba(0, 0, 0, 0)",
            ["background-image"] = "none",
            ["background-position"] = "0% 0%",
            ["background-repeat"] = "repeat",
            ["margin"] = "0px",
            ["margin-top"] = "0px",
            ["margin-right"] = "0px",
            ["margin-bottom"] = "0px",
            ["margin-left"] = "0px",
            ["padding"] = "0px",
            ["padding-top"] = "0px",
            ["padding-right"] = "0px",
            ["padding-bottom"] = "0px",
            ["padding-left"] = "0px",
            ["border-style"] = "none",
            ["border-width"] = "0px",
            ["border-color"] = "rgb(0, 0, 0)",
            ["border-top-width"] = "0px",
            ["border-right-width"] = "0px",
            ["border-bottom-width"] = "0px",
            ["border-left-width"] = "0px",
            ["border-top-style"] = "none",
            ["border-right-style"] = "none",
            ["border-bottom-style"] = "none",
            ["border-left-style"] = "none",
            ["border-top-color"] = "rgb(0, 0, 0)",
            ["border-right-color"] = "rgb(0, 0, 0)",
            ["border-bottom-color"] = "rgb(0, 0, 0)",
            ["border-left-color"] = "rgb(0, 0, 0)",
            ["border-collapse"] = "separate",
            ["border-spacing"] = "0px",
            ["opacity"] = "1",
            ["vertical-align"] = "baseline",
            ["clear"] = "none",
            ["z-index"] = "auto",
            ["top"] = "auto",
            ["right"] = "auto",
            ["bottom"] = "auto",
            ["left"] = "auto",
            ["width"] = "auto",
            ["height"] = "auto",
            ["min-width"] = "0px",
            ["min-height"] = "0px",
            ["max-width"] = "none",
            ["max-height"] = "none",
            ["box-sizing"] = "content-box",
            ["list-style-type"] = "disc",
            ["list-style-position"] = "outside",
            ["content"] = "normal",
            ["transform"] = "none",
            ["mix-blend-mode"] = "normal",
            ["background-blend-mode"] = "normal",
            ["isolation"] = "auto",
            ["filter"] = "none",
            ["writing-mode"] = "horizontal-tb",
            ["zoom"] = "1",
        });

    /// <summary>
    /// The set of properties that inherit by default (CSS cascade §inheritance).
    /// A property absent from a cascade takes its parent's computed value when it
    /// is in this set, and its <see cref="InitialValues"/> entry otherwise.
    /// </summary>
    public static IReadOnlySet<string> InheritedProperties { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "color",
            "cursor",
            "font-family",
            "font-size",
            "font-style",
            "font-variant",
            "font-weight",
            "letter-spacing",
            "line-height",
            "text-align",
            "text-align-last",
            "text-indent",
            "text-shadow",
            "text-transform",
            "visibility",
            "white-space",
            "word-spacing",
            "writing-mode",
        };
}
