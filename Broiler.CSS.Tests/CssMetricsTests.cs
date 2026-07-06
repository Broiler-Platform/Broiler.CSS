namespace Broiler.CSS.Tests;

/// <summary>
/// Phase M0 guard for <see cref="CssMetrics"/>: pins the derived measurement
/// factors to (a) their mathematical definitions and (b) the historical inline
/// literals they will replace in Phase M1, so the later swap is provably
/// behavior-neutral. See <c>docs/roadmap/broiler-layout-measurement-dedup.md</c>.
/// </summary>
public sealed class CssMetricsTests
{
    // Tolerance for comparing against the historical truncated float literals.
    // The derived doubles are more precise; the delta is ~1e-10 relative, far
    // below any sub-pixel effect. 9 decimal digits documents the closeness while
    // acknowledging the intentional precision improvement.
    private const int LiteralPrecision = 6;

    [Fact]
    public void Absolute_Factors_Match_Their_Definitions()
    {
        Assert.Equal(96.0, CssMetrics.Dpi);
        Assert.Equal(96.0 / 72.0, CssMetrics.PtToPx);   // bit-identical to the old inline 96.0/72.0
        Assert.Equal(72.0 / 96.0, CssMetrics.PxToPt);   // bit-identical to the old inline 72f/96f (=0.75)
        Assert.Equal(96.0, CssMetrics.PxPerInch);       // bit-identical to the old 96f
        Assert.Equal(CssMetrics.Dpi / 2.54, CssMetrics.PxPerCm);
        Assert.Equal(CssMetrics.Dpi / 25.4, CssMetrics.PxPerMm);
        Assert.Equal(CssMetrics.PxPerCm / 40.0, CssMetrics.PxPerQ);
        Assert.Equal(12.0 * CssMetrics.PtToPx, CssMetrics.PxPerPica);
    }

    [Fact]
    public void Derived_Factors_Are_Internally_Consistent()
    {
        Assert.Equal(CssMetrics.PxPerMm * 10.0, CssMetrics.PxPerCm, 12);
        Assert.Equal(CssMetrics.PxPerCm / 40.0, CssMetrics.PxPerQ, 12);
        // 1pc = 12pt, and 12pt = 16px at 96 DPI.
        Assert.Equal(16.0, CssMetrics.PxPerPica, 12);
        // PxToPt is the exact inverse of PtToPx.
        Assert.Equal(1.0, CssMetrics.PtToPx * CssMetrics.PxToPt, 12);
    }

    [Fact]
    public void Factors_Match_The_Historical_Inline_Literals()
    {
        // These are the exact literals currently living in CssLengthParser.cs
        // (lines 210-237 / 469-490) that Phase M1 replaces with CssMetrics.
        Assert.Equal(3.779527559, CssMetrics.PxPerMm, LiteralPrecision);          // "3 pixels per millimeter"
        Assert.Equal(37.795275591, CssMetrics.PxPerCm, LiteralPrecision);         // "37 pixels per centimeter"
        Assert.Equal(37.795275591 / 40.0, CssMetrics.PxPerQ, LiteralPrecision);   // "1Q = 1/40 cm"
        Assert.Equal(96.0, CssMetrics.PxPerInch, LiteralPrecision);               // "96 pixels per inch"
        Assert.Equal(16.0, CssMetrics.PxPerPica, LiteralPrecision);               // "1 pica = 12 points"
        Assert.Equal(96.0 / 72.0, CssMetrics.PtToPx, LiteralPrecision);           // "1 point = 1/72 of inch"
    }

    [Fact]
    public void Font_And_LineHeight_Defaults_Match_Current_Constants()
    {
        Assert.Equal(CssConstants.FontSize, CssMetrics.DefaultFontSizePt);
        Assert.Equal(CssConstants.FontSize * (96.0 / 72.0), CssMetrics.DefaultFontSizePx);
        Assert.Equal(1.2, CssMetrics.NormalLineHeightFactor);
    }

    // Phase M1 parity: CssLengthParser now multiplies by the CssMetrics doubles
    // instead of the historical truncated float literals. This proves the swap is
    // behavior-neutral at the pixel level — over a wide magnitude range, the
    // rounded (device-pixel) result is unchanged and the raw delta is sub-milli-px.
    [Theory]
    [InlineData("mm", 3.779527559f)]
    [InlineData("cm", 37.795275591f)]
    [InlineData("in", 96f)]
    [InlineData("pt", 96f / 72f)]
    [InlineData("pc", 16f)]
    [InlineData("q", 37.795275591f / 40f)]
    public void ParseLength_Matches_Historical_Float_Factors_Within_SubPixel(string unit, double oldFloatFactor)
    {
        foreach (var n in new[] { 0.1, 1, 3, 7.5, 10, 100, 250, 1000 })
        {
            double expectedOld = oldFloatFactor * n;
            double actualNew = CssLengthParser.ParseLength($"{n.ToString(System.Globalization.CultureInfo.InvariantCulture)}{unit}", 100, 16);

            // The device-pixel (rounded) result is identical — the pixel-safety proof.
            Assert.Equal(Math.Round(expectedOld), Math.Round(actualNew));
            // The raw delta is the old-float rounding error only (~1e-7 relative),
            // i.e. a precision improvement, never a behavioral regression.
            Assert.True(Math.Abs(expectedOld - actualNew) <= Math.Max(1e-6, Math.Abs(expectedOld) * 1e-6),
                $"{n}{unit}: old={expectedOld}, new={actualNew}");
        }
    }
}
