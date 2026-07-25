using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Broiler.CSS;

/// <summary>
/// Evaluates a CSS <c>&lt;easing-function&gt;</c> (an animation/transition timing
/// function): the named keywords (<c>linear</c>, <c>ease</c>, <c>ease-in</c>,
/// <c>ease-out</c>, <c>ease-in-out</c>, <c>step-start</c>, <c>step-end</c>),
/// <c>steps(n, position)</c>, and <c>cubic-bezier(x1,y1,x2,y2)</c> (solved by
/// Newton-Raphson). Maps an input progress fraction in [0,1] to the eased output.
/// </summary>
/// <remarks>
/// Promoted from the HtmlBridge animation resolver — pure CSS timing math with no
/// DOM or JavaScript coupling, so it belongs in the canonical CSS component.
/// </remarks>
public static partial class CssEasing
{
    private static readonly Regex StepsPattern = StepsPatternRegex();

    private static readonly Regex CubicBezierPattern = CubicBezierPatternRegex();

    /// <summary>
    /// Maps <paramref name="progress"/> (an input time fraction in [0,1]) through the
    /// given <paramref name="timingFunction"/> to the eased output fraction. An
    /// unrecognized function falls back to <c>linear</c> (identity).
    /// </summary>
    public static double Evaluate(double progress, string timingFunction)
    {
        // Handle steps() timing functions.
        var stepsMatch = StepsPattern.Match(timingFunction);
        if (stepsMatch.Success)
        {
            int steps = int.Parse(stepsMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            string position = stepsMatch.Groups[2].Success ? stepsMatch.Groups[2].Value : "end";
            return ApplySteps(progress, steps, position);
        }

        if (timingFunction == "step-start")
            return ApplySteps(progress, 1, "start");
        if (timingFunction == "step-end")
            return ApplySteps(progress, 1, "end");

        // Handle cubic-bezier() timing functions.
        var bezierMatch = CubicBezierPattern.Match(timingFunction);
        if (bezierMatch.Success)
        {
            double x1 = double.Parse(bezierMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            double y1 = double.Parse(bezierMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            double x2 = double.Parse(bezierMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            double y2 = double.Parse(bezierMatch.Groups[4].Value, CultureInfo.InvariantCulture);
            return SolveCubicBezier(progress, x1, y1, x2, y2);
        }

        // Named easing keywords.
        return timingFunction switch
        {
            "ease" => SolveCubicBezier(progress, 0.25, 0.1, 0.25, 1.0),
            "ease-in" => SolveCubicBezier(progress, 0.42, 0.0, 1.0, 1.0),
            "ease-out" => SolveCubicBezier(progress, 0.0, 0.0, 0.58, 1.0),
            "ease-in-out" => SolveCubicBezier(progress, 0.42, 0.0, 0.58, 1.0),
            _ => progress, // linear
        };
    }

    /// <summary>
    /// Evaluates a cubic-bezier timing function: given an x-progress value
    /// in [0,1], finds the corresponding y-output by Newton-Raphson iteration
    /// on the x-coordinate polynomial.
    /// </summary>
    private static double SolveCubicBezier(double x, double x1, double y1, double x2, double y2)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;

        // Solve for t where B_x(t) = x using Newton-Raphson.
        double t = x; // initial guess
        for (int i = 0; i < 20; i++)
        {
            double bx = BezierCoord(t, x1, x2);
            double dx = bx - x;
            if (Math.Abs(dx) < 1e-7) break;
            double dbx = BezierDerivative(t, x1, x2);
            if (Math.Abs(dbx) < 1e-12) break;
            t -= dx / dbx;
            t = Math.Clamp(t, 0, 1);
        }

        return BezierCoord(t, y1, y2);
    }

    // B(t) = 3(1-t)^2 * t * p1 + 3(1-t) * t^2 * p2 + t^3
    private static double BezierCoord(double t, double p1, double p2)
    {
        double omt = 1 - t;
        return 3 * omt * omt * t * p1 + 3 * omt * t * t * p2 + t * t * t;
    }

    // B'(t) = 3(1-t)^2 * p1 + 6(1-t)*t*(p2-p1) + 3t^2*(1-p2)
    private static double BezierDerivative(double t, double p1, double p2)
    {
        double omt = 1 - t;
        return 3 * omt * omt * p1 + 6 * omt * t * (p2 - p1) + 3 * t * t * (1 - p2);
    }

    private static double ApplySteps(double progress, int steps, string position)
    {
        if (steps <= 0) steps = 1;

        // CSS steps() function: divides the animation into N equal intervals.
        double currentStep;
        switch (position)
        {
            case "start":
            case "jump-start":
                currentStep = Math.Ceiling(progress * steps);
                break;
            case "end":
            case "jump-end":
            default:
                currentStep = Math.Floor(progress * steps);
                break;
            case "jump-none":
                currentStep = Math.Floor(progress * steps);
                steps = Math.Max(steps - 1, 1);
                break;
            case "jump-both":
                currentStep = Math.Floor(progress * (steps + 1));
                steps++;
                break;
        }

        return Math.Min(currentStep / steps, 1.0);
    }

    [GeneratedRegex(@"steps\(\s*(\d+)\s*(?:,\s*(start|end|jump-start|jump-end|jump-none|jump-both))?\s*\)", RegexOptions.Compiled)]
    private static partial Regex StepsPatternRegex();
    [GeneratedRegex(@"cubic-bezier\(\s*([0-9.eE+-]+)\s*,\s*([0-9.eE+-]+)\s*,\s*([0-9.eE+-]+)\s*,\s*([0-9.eE+-]+)\s*\)", RegexOptions.Compiled)]
    private static partial Regex CubicBezierPatternRegex();
}
