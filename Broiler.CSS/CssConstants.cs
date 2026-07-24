namespace Broiler.CSS;

/// <summary>
/// CSS keyword and unit string constants used during layout / used-value
/// resolution. Promoted into the <c>Broiler.CSS</c> kernel so consumers
/// (notably the layout engine extracted into <c>Broiler.Layout</c>) can share
/// them without depending on the renderer's <c>Broiler.HTML.Utils</c> copy.
/// Values are byte-for-byte identical to that copy; the renderer keeps its own
/// until the Phase 7 CSS cleanup dedups them.
/// </summary>
public static class CssConstants
{
    public const string Absolute = "absolute";
    public const string Auto = "auto";
    public const string Avoid = "avoid";
    public const string Baseline = "baseline";
    public const string Blink = "blink";
    public const string Block = "block";
    public const string InlineBlock = "inline-block";
    public const string Bold = "bold";
    public const string Bolder = "bolder";
    public const string Bottom = "bottom";
    public const string BreakAll = "break-all";
    public const string KeepAll = "keep-all";
    public const string Center = "center";
    public const string Collapse = "collapse";
    public const string Cursive = "cursive";
    public const string Circle = "circle";
    public const string Decimal = "decimal";
    public const string DecimalLeadingZero = "decimal-leading-zero";
    public const string Disc = "disc";
    public const string Fantasy = "fantasy";
    public const string Fixed = "fixed";
    public const string Relative = "relative";
    public const string Hide = "hide";
    public const string Inherit = "inherit";
    public const string Inline = "inline";
    public const string InlineTable = "inline-table";
    public const string Inset = "inset";
    public const string Italic = "italic";
    public const string Justify = "justify";
    public const string Large = "large";
    public const string Larger = "larger";
    public const string Left = "left";
    public const string Lighter = "lighter";
    public const string LineThrough = "line-through";
    public const string ListItem = "list-item";
    public const string Ltr = "ltr";
    public const string LowerAlpha = "lower-alpha";
    public const string LowerLatin = "lower-latin";
    public const string LowerRoman = "lower-roman";
    public const string LowerGreek = "lower-greek";
    public const string Armenian = "armenian";
    public const string Georgian = "georgian";
    public const string Hebrew = "hebrew";
    public const string Hiragana = "hiragana";
    public const string HiraganaIroha = "hiragana-iroha";
    public const string Katakana = "katakana";
    public const string KatakanaIroha = "katakana-iroha";
    public const string Medium = "medium";
    public const string Middle = "middle";
    public const string Monospace = "monospace";
    public const string None = "none";
    public const string Normal = "normal";
    public const string NoWrap = "nowrap";
    public const string Oblique = "oblique";
    public const string Outset = "outset";
    public const string Overline = "overline";
    public const string Pre = "pre";
    public const string PreWrap = "pre-wrap";
    public const string PreLine = "pre-line";
    public const string Right = "right";
    public const string Rtl = "rtl";
    public const string SansSerif = "sans-serif";
    public const string Serif = "serif";
    public const string Show = "show";
    public const string Small = "small";
    public const string Smaller = "smaller";
    public const string Solid = "solid";
    public const string Sub = "sub";
    public const string Super = "super";
    public const string Square = "square";
    public const string Table = "table";
    public const string TableRow = "table-row";
    public const string TableRowGroup = "table-row-group";
    public const string TableHeaderGroup = "table-header-group";
    public const string TableFooterGroup = "table-footer-group";
    public const string TableColumn = "table-column";
    public const string TableColumnGroup = "table-column-group";
    public const string TableCell = "table-cell";
    public const string TableCaption = "table-caption";
    public const string TextBottom = "text-bottom";
    public const string TextTop = "text-top";
    public const string Thin = "thin";
    public const string Thick = "thick";
    public const string Top = "top";
    public const string Underline = "underline";
    public const string UpperAlpha = "upper-alpha";
    public const string UpperLatin = "upper-latin";
    public const string UpperRoman = "upper-roman";
    public const string XLarge = "x-large";
    public const string XSmall = "x-small";
    public const string XXLarge = "xx-large";
    public const string XXSmall = "xx-small";
    public const string Visible = "visible";
    public const string Hidden = "hidden";
    public const string Scroll = "scroll";
    public const string Dotted = "dotted";
    public const string Dashed = "dashed";
    public const string Double = "double";
    public const string Groove = "groove";
    public const string Ridge = "ridge";

    public const string Cm = "cm";
    public const string Mm = "mm";
    public const string Px = "px";
    public const string In = "in";
    public const string Em = "em";
    public const string Ex = "ex";
    public const string Ch = "ch";
    public const string Ic = "ic";
    public const string Lh = "lh";
    public const string Pt = "pt";
    public const string Pc = "pc";
    public const string Rem = "rem";
    public const string Rlh = "rlh";

    /// <summary>Viewport-height unit: 1vh = 1% of viewport height.</summary>
    public const string Vh = "vh";

    /// <summary>Viewport-width unit: 1vw = 1% of viewport width.</summary>
    public const string Vw = "vw";

    /// <summary>Viewport-min unit: 1vmin = 1% of the smaller viewport dimension.</summary>
    public const string Vmin = "vmin";

    /// <summary>Viewport-max unit: 1vmax = 1% of the larger viewport dimension.</summary>
    public const string Vmax = "vmax";

    /// <summary>Quarter-millimeter: 1Q = 1/40 cm ≈ 0.945 px at 96 dpi.</summary>
    public const string Q = "q";
    /// <summary>
    /// Initial (root) font size in points. Single-sourced from
    /// <see cref="CssMetrics.DefaultFontSizePt"/>.
    /// </summary>
    public const double FontSize = CssMetrics.DefaultFontSizePt;
    public const string DefaultFont = "serif";
}
