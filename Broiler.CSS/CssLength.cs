using System;
using System.Globalization;

namespace Broiler.CSS;

/// <summary>
/// Parses a single CSS length token (e.g. <c>"12px"</c>, <c>"1.5em"</c>,
/// <c>"50%"</c>) into its numeric value and unit. This is the shared
/// <c>Broiler.CSS</c> representation used by layout, with units projected onto
/// <see cref="CssUnit"/>.
/// </summary>
public sealed class CssLength
{
    private readonly double _number;

    public CssLength(string length)
    {
        Length = length;
        _number = 0f;
        Unit = CssUnit.None;
        IsPercentage = false;

        //Return zero if no length specified, zero specified
        if (string.IsNullOrEmpty(length) || length == "0")
            return;

        //If percentage, use ParseNumber
        if (length.EndsWith('%'))
        {
            _number = CssLengthParser.ParseNumber(length, 1);
            IsPercentage = true;
            return;
        }

        //If no units, has error
        if (length.Length < 3)
        {
            _ = double.TryParse(length, out _number);
            HasError = true;
            return;
        }

        // Detect the trailing unit via the shared CssLengthParser scanner, then
        // project the token onto CssUnit. Units the legacy
        // CssLength never recognized (lh/rlh/Q) map to an error, preserving behavior.
        string unit = CssLengthParser.GetUnit(length, null, out bool hasUnit, out int unitLength);
        if (!hasUnit || !TryMapUnit(unit, out CssUnit cssUnit, out bool isRelative))
        {
            HasError = true;
            return;
        }

        Unit = cssUnit;
        IsRelative = isRelative;

        // Trim by the unit AS WRITTEN, not by the canonical spelling: the
        // small/large/dynamic viewport variants canonicalise to something shorter
        // (svmin → vmin), so unit.Length would leave the prefix on the number.
        string number = length[..^unitLength];
        if (!double.TryParse(number, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out _number))
            HasError = true;
    }

    /// <summary>
    /// Projects a unit token from <see cref="CssLengthParser.GetUnit"/> onto the
    /// <see cref="CssUnit"/> enum plus its font/viewport-relative flag. Only the
    /// units the legacy <see cref="CssLength"/> parser recognized are accepted;
    /// <c>lh</c>/<c>rlh</c>/<c>Q</c> (and anything else) return <c>false</c>.
    /// </summary>
    private static bool TryMapUnit(string unit, out CssUnit cssUnit, out bool isRelative)
    {
        (cssUnit, isRelative) = unit switch
        {
            CssConstants.Em => (CssUnit.Em, true),
            CssConstants.Ex => (CssUnit.Ex, true),
            CssConstants.Ch => (CssUnit.Ch, true),
            CssConstants.Ic => (CssUnit.Ic, true),
            CssConstants.Px => (CssUnit.Px, true),
            CssConstants.Rem => (CssUnit.Rem, true),
            CssConstants.Vh => (CssUnit.Vh, true),
            CssConstants.Vw => (CssUnit.Vw, true),
            CssConstants.Vmin => (CssUnit.Vmin, true),
            CssConstants.Vmax => (CssUnit.Vmax, true),
            // CSS Values 4 §6.1.4: the logical viewport units. GetUnit reports the
            // sv*/lv*/dv* variants under these canonical spellings.
            CssConstants.Vi => (CssUnit.Vi, true),
            CssConstants.Vb => (CssUnit.Vb, true),
            CssConstants.Mm => (CssUnit.Mm, false),
            CssConstants.Cm => (CssUnit.Cm, false),
            CssConstants.In => (CssUnit.In, false),
            CssConstants.Pt => (CssUnit.Pt, false),
            CssConstants.Pc => (CssUnit.Pc, false),
            _ => (CssUnit.None, false),
        };
        return cssUnit != CssUnit.None;
    }


    public double Number => _number;
    public bool HasError { get; }
    public bool IsPercentage { get; }
    public bool IsRelative { get; }
    public CssUnit Unit { get; }
    public string Length { get; }

    public CssLength ConvertEmToPoints(double emSize)
    {
        if (HasError)
            throw new InvalidOperationException("Invalid length");

        if (Unit != CssUnit.Em)
            throw new InvalidOperationException("Length is not in ems");

        return new CssLength($"{Convert.ToSingle(Number * emSize).ToString("0.0", NumberFormatInfo.InvariantInfo)}pt");
    }

    public CssLength ConvertEmToPixels(double pixelFactor)
    {
        if (HasError)
            throw new InvalidOperationException("Invalid length");

        if (Unit != CssUnit.Em)
            throw new InvalidOperationException("Length is not in ems");

        return new CssLength($"{Convert.ToSingle(Number * pixelFactor).ToString("0.0", NumberFormatInfo.InvariantInfo)}px");
    }

    public override string ToString()
    {
        if (HasError)
        {
            return string.Empty;
        }
        else if (IsPercentage)
        {
            return $"{Number}%";
        }
        else
        {
            string u = string.Empty;

            switch (Unit)
            {
                case CssUnit.None:
                    break;
                case CssUnit.Em:
                    u = "em";
                    break;
                case CssUnit.Px:
                    u = "px";
                    break;
                case CssUnit.Ex:
                    u = "ex";
                    break;
                case CssUnit.Ch:
                    u = "ch";
                    break;
                case CssUnit.Ic:
                    u = "ic";
                    break;
                case CssUnit.In:
                    u = "in";
                    break;
                case CssUnit.Cm:
                    u = "cm";
                    break;
                case CssUnit.Mm:
                    u = "mm";
                    break;
                case CssUnit.Pt:
                    u = "pt";
                    break;
                case CssUnit.Pc:
                    u = "pc";
                    break;
                case CssUnit.Rem:
                    u = "rem";
                    break;
                case CssUnit.Vh:
                    u = "vh";
                    break;
                case CssUnit.Vw:
                    u = "vw";
                    break;
                case CssUnit.Vmin:
                    u = "vmin";
                    break;
                case CssUnit.Vmax:
                    u = "vmax";
                    break;
                case CssUnit.Vi:
                    u = "vi";
                    break;
                case CssUnit.Vb:
                    u = "vb";
                    break;
            }

            return $"{Number:0.0}{u}".Replace(',', '.');
        }
    }
}
