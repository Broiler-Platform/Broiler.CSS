using System.Text.RegularExpressions;

namespace Broiler.CSS;

/// <summary>
/// Shared compiled CSS grammar regexes and small parsing helpers used by the
/// CSS and layout components.
/// </summary>
public static partial class RegexParserUtils
{
    public const string CssMediaTypes = @"@media[^\{\}]*\{";
    /// WARNING: Blocks will include blocks inside at-rules.
    public const string CssBlocks = @"[^\{\}]*\{[^\{\}]*\}";
    public const string CssNumber = @"([0-9]+|[0-9]*\.[0-9]+)";
    public const string CssPercentage = @"([0-9]+|[0-9]*\.[0-9]+)\%";
    public const string CssLength = @"([0-9]+|[0-9]*\.[0-9]+)(em|ex|ch|ic|px|in|cm|mm|pt|pc)";
    public const string CssLineHeight = "(normal|" + CssNumber + "|" + CssLength + "|" + CssPercentage + ")";
    public const string CssFontFamily = "(\"[^\"]*\"|'[^']*'|\\S+\\s*)(\\s*\\,\\s*(\"[^\"]*\"|'[^']*'|\\S+))*";
    public const string CssFontStyle = "(normal|italic|oblique)";
    public const string CssFontVariant = "(normal|small-caps)";
    public const string CssFontWeight = "(normal|bold|bolder|lighter|100|200|300|400|500|600|700|800|900)";
    // CSS 2.1 §4.3.2: The unit identifier may be omitted for zero values.
    // Accept bare "0" so that shorthand "font: 0/0 …" (used by Acid3) is
    // correctly split into font-size:0 and line-height:0.
    public const string CssFontSize = "(" + CssLength + "|" + CssPercentage + "|0|xx-small|x-small|small|medium|large|x-large|xx-large|larger|smaller)";
    public const string CssFontSizeAndLineHeight = CssFontSize + @"(\/" + CssLineHeight + @")?(\s|$)";

    [GeneratedRegex(CssMediaTypes, RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex CssMediaTypesRegex();

    [GeneratedRegex(CssBlocks, RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex CssBlocksRegex();

    [GeneratedRegex(CssNumber, RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex CssNumberRegex();

    [GeneratedRegex(CssPercentage, RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex CssPercentageRegex();

    [GeneratedRegex(CssLength, RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex CssLengthRegex();

    [GeneratedRegex(CssLineHeight, RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex CssLineHeightRegex();

    [GeneratedRegex(CssFontFamily, RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex CssFontFamilyRegex();

    [GeneratedRegex(CssFontStyle, RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex CssFontStyleRegex();

    [GeneratedRegex(CssFontVariant, RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex CssFontVariantRegex();

    [GeneratedRegex(CssFontWeight, RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex CssFontWeightRegex();

    [GeneratedRegex(CssFontSize, RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex CssFontSizeRegex();

    [GeneratedRegex(CssFontSizeAndLineHeight, RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    public static partial Regex CssFontSizeAndLineHeightRegex();

    public static MatchCollection Match(Regex regex, string source) => regex.Matches(source);

    public static string Search(Regex regex, string source) => Search(regex, source, out _);

    public static string Search(Regex regex, string source, out int position)
    {
        MatchCollection matches = Match(regex, source);

        if (matches.Count > 0)
        {
            position = matches[0].Index;
            return matches[0].Value;
        }
        else
        {
            position = -1;
        }

        return null;
    }
}
