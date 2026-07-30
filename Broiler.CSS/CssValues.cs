using System.Globalization;

namespace Broiler.CSS;

public enum CssValueKind
{
    Empty,
    Keyword,
    Number,
    Percentage,
    Dimension,
    Color,
    String,
    Url,
    Function,
    Custom,
}

public enum CssUnit
{
    None,
    Percent,
    Px,
    Em,
    Rem,
    Ex,
    Ch,
    Ic,
    Lh,
    Rlh,
    Vw,
    Vh,
    Vmin,
    Vmax,
    // CSS Values 4 §6.1.4: the logical viewport units, resolved against the
    // root element's writing mode (vi = inline axis, vb = block axis).
    Vi,
    Vb,
    Cm,
    Mm,
    Q,
    In,
    Pt,
    Pc,
    Deg,
    Grad,
    Rad,
    Turn,
    S,
    Ms,
    Fr,
    Unknown,
}

public readonly record struct CssNumericValue(double Number, CssUnit Unit)
{
    public bool IsPercentage => Unit == CssUnit.Percent;
}

public readonly record struct CssColor(byte Red, byte Green, byte Blue, byte Alpha = 255)
{
    public string ToCssString() =>
        Alpha == 255
            ? $"rgb({Red}, {Green}, {Blue})"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"rgba({Red}, {Green}, {Blue}, {Alpha / 255d:0.###})");
}

public sealed class CssValue(string text, CssNumericValue? numeric = null)
{
    public string Text { get; } = text;

    public CssNumericValue? Numeric { get; } = numeric;
}
