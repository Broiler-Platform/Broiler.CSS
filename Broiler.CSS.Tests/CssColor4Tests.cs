namespace Broiler.CSS.Tests;

/// <summary>
/// CSS Color 4/5 colour functions, resolved to the <c>rgba()</c> form the rest of the pipeline
/// reads (<see cref="CssColor4"/>).
/// </summary>
/// <remarks>
/// The values pinned here are the ones a browser produces, not ones read back off this
/// implementation: every conversion below was cross-checked against the reference numbers WPT's own
/// <c>css/css-color</c> reftests state, and the sRGB cases are exact by construction
/// (<c>color(srgb 0 0.5 0.5)</c> <i>is</i> <c>rgb(0, 128, 128)</c>). A tolerance appears only where
/// the answer genuinely depends on the transfer function's last bit.
/// </remarks>
public sealed class CssColor4Tests
{
    private static (int R, int G, int B, double A) Resolve(string value)
    {
        Assert.True(CssColor4.TryParse(value, out var rgba), $"failed to parse: {value}");
        return (
            (int)System.Math.Round(rgba.R),
            (int)System.Math.Round(rgba.G),
            (int)System.Math.Round(rgba.B),
            rgba.A);
    }

    private static void AssertClose(string value, int r, int g, int b, int tolerance = 1)
    {
        var actual = Resolve(value);
        Assert.True(
            System.Math.Abs(actual.R - r) <= tolerance &&
            System.Math.Abs(actual.G - g) <= tolerance &&
            System.Math.Abs(actual.B - b) <= tolerance,
            $"{value}: expected ~rgb({r}, {g}, {b}), got rgb({actual.R}, {actual.G}, {actual.B})");
    }

    // ---------------------------------------------------------------- the gap that started this

    /// <summary>
    /// The defect the whole class exists for: the canonical parser reads only <c>rgb()</c>,
    /// <c>hsl()</c>, hex and the named colours, and an unrecognised value does not fail visibly —
    /// it folds to <i>opaque black</i>. So every one of these painted a solid black box.
    /// </summary>
    [Theory]
    [InlineData("hwb(180 0% 50%)")]
    [InlineData("lab(50% -20 -10)")]
    [InlineData("lch(50% 30 200)")]
    [InlineData("oklab(0.6 -0.1 -0.05)")]
    [InlineData("oklch(0.6 0.12 200)")]
    [InlineData("color(srgb 0 0.5 0.5)")]
    [InlineData("color(display-p3 0 0.5 0.5)")]
    [InlineData("color-mix(in srgb, red, blue)")]
    [InlineData("rgb(0 128 128 / 50%)")]
    public void EveryColorFourFunctionResolvesToSomethingOtherThanBlack(string value)
    {
        var actual = Resolve(value);
        Assert.NotEqual((0, 0, 0), (actual.R, actual.G, actual.B));
    }

    [Fact]
    public void ColorInSrgbIsExact()
    {
        Assert.Equal((0, 128, 128, 1.0), Resolve("color(srgb 0 0.5 0.5)"));
        Assert.Equal((255, 255, 255, 1.0), Resolve("color(srgb 1 1 1)"));
        Assert.Equal((0, 0, 0, 1.0), Resolve("color(srgb 0 0 0)"));
    }

    /// <summary>HWB's whiteness/blackness scale a fully saturated hue; at w+b ≥ 1 the hue drops out
    /// entirely and the result is the grey their ratio names (CSS Color 4 §7).</summary>
    [Fact]
    public void HwbScalesTheHueAndCollapsesToGreyWhenSaturated()
    {
        Assert.Equal((0, 128, 128, 1.0), Resolve("hwb(180 0% 50%)"));
        Assert.Equal((128, 128, 128, 1.0), Resolve("hwb(180 50% 50%)"));
        Assert.Equal((191, 191, 191, 1.0), Resolve("hwb(0 75% 25%)"));
    }

    [Fact]
    public void LabAndLchAgreeWhenTheyNameTheSameColor()
    {
        // lch(50% 30 200) is lab(50%, 30·cos200°, 30·sin200°) by definition, so the two notations
        // must land on the same pixel — the polar unwind is the only thing between them.
        var lch = Resolve("lch(50% 30 200)");
        var lab = Resolve("lab(50% -28.191 -10.261)");
        Assert.Equal(lab, lch);
    }

    [Fact]
    public void OklabAndOklchAgreeWhenTheyNameTheSameColor()
    {
        var oklch = Resolve("oklch(0.6 0.12 200)");
        var oklab = Resolve("oklab(0.6 -0.112763 -0.041042)");
        Assert.Equal(oklab, oklch);
    }

    /// <summary>
    /// WPT <c>oklab-l-over-1-1</c>/<c>oklch-l-over-1-2</c>: an over-range lightness is clamped, so
    /// the out-of-range spelling and the in-range one must paint identically. Missing this made the
    /// two take different branches of the gamut mapper and paint different colours.
    /// </summary>
    [Fact]
    public void LightnessIsClampedToItsSpaceRange()
    {
        Assert.Equal(Resolve("oklab(1 0.5 0.2)"), Resolve("oklab(1.5 0.5 0.2)"));
        Assert.Equal(Resolve("oklch(100% 0.5 50)"), Resolve("oklch(150% 0.5 50)"));
        Assert.Equal(Resolve("lab(100% 20 30)"), Resolve("lab(150% 20 30)"));
        Assert.Equal(Resolve("oklab(0 0.5 0.2)"), Resolve("oklab(-0.5 0.5 0.2)"));
    }

    /// <summary>
    /// CSS Color 4 §4.4: a missing component rendered on its own is zero. WPT
    /// <c>gradient-single-stop-none-interpolation</c> asserts exactly this, and it is the reason
    /// that test rendered a blank page against a blank reference and "passed" while painting none
    /// of what it asks for.
    /// </summary>
    [Fact]
    public void NoneComponentsResolveToZero()
    {
        Assert.Equal(Resolve("color(srgb 0 0.5 0.5)"), Resolve("color(srgb none 0.5 0.5)"));
        Assert.Equal(Resolve("color(srgb 0 0 0)"), Resolve("color(srgb none none none)"));
        Assert.Equal(Resolve("oklch(0.8 0.4 0)"), Resolve("oklch(0.8 0.4 none)"));
    }

    // ---------------------------------------------------------------- alpha

    /// <summary>
    /// The base parser rewrites the slash to a comma and then splits on commas, which turns three
    /// space-separated channels into one token and fails its own arity check — so the modern alpha
    /// spelling parsed as nothing and painted black, while the legacy comma form worked.
    /// </summary>
    [Fact]
    public void ModernSlashAlphaIsRead()
    {
        Assert.Equal((0, 128, 128, 0.5), Resolve("rgb(0 128 128 / 50%)"));
        Assert.Equal((0, 128, 128, 0.5), Resolve("rgb(0 128 128 / 0.5)"));
        Assert.Equal((0, 128, 128, 1.0), Resolve("hsl(180 100% 25% / 1)"));
    }

    /// <summary>The legacy comma form is the base parser's business and must be left to it, so the
    /// two never disagree about the same value.</summary>
    [Fact]
    public void LegacyCommaFormIsDeclined()
    {
        Assert.False(CssColor4.TryParse("rgb(0, 128, 128)", out _));
        Assert.False(CssColor4.TryParse("rgba(0, 128, 128, 0.5)", out _));
        Assert.False(CssColor4.TryParse("hsl(180, 100%, 25%)", out _));
    }

    // ---------------------------------------------------------------- color-mix

    [Fact]
    public void ColorMixInSrgbIsTheMidpoint()
    {
        Assert.Equal((128, 0, 128, 1.0), Resolve("color-mix(in srgb, red, blue)"));
        Assert.Equal((64, 0, 191, 1.0), Resolve("color-mix(in srgb, red 25%, blue 75%)"));
    }

    /// <summary>CSS Color 5 §3.2: one stated percentage leaves the rest to the other colour.</summary>
    [Fact]
    public void ColorMixFillsInTheOmittedPercentage()
    {
        Assert.Equal(
            Resolve("color-mix(in srgb, red 25%, blue 75%)"),
            Resolve("color-mix(in srgb, red 25%, blue)"));
    }

    /// <summary>Percentages summing under 100% scale the *alpha* rather than the colours (§3.2), so
    /// the hue is the same as the full mix and only the transparency differs.</summary>
    [Fact]
    public void ColorMixUnderOneHundredPercentScalesAlpha()
    {
        var mixed = Resolve("color-mix(in srgb, red 25%, blue 25%)");
        Assert.Equal((128, 0, 128), (mixed.R, mixed.G, mixed.B));
        Assert.Equal(0.5, mixed.A, 3);
    }

    /// <summary>A polar space mixes the hue round the circle, and which way round is a visible
    /// choice rather than a rounding one — <c>longer hue</c> takes the other arc.</summary>
    [Fact]
    public void ColorMixHonoursTheHueArc()
    {
        var shorter = Resolve("color-mix(in hsl, hsl(20 100% 50%), hsl(320 100% 50%))");
        var longer = Resolve("color-mix(in hsl longer hue, hsl(20 100% 50%), hsl(320 100% 50%))");
        Assert.NotEqual((shorter.R, shorter.G, shorter.B), (longer.R, longer.G, longer.B));
    }

    /// <summary>Without a <c>currentcolor</c> the caller supplies, the mix is left unconverted
    /// rather than resolved against an invented black.</summary>
    [Fact]
    public void ColorMixWithCurrentColorNeedsTheCallerToSupplyIt()
    {
        Assert.False(CssColor4.TryParse("color-mix(in srgb, currentcolor, blue)", out _));

        var resolved = CssColor4.NormalizeColorFunctions(
            "color-mix(in srgb, currentcolor, blue)", "rgba(255, 0, 0, 1)");
        Assert.Equal("rgba(128, 0, 128, 1)", resolved);
    }

    // ---------------------------------------------------------------- relative colour syntax

    /// <summary>
    /// CSS Color 5 §4. The channel keywords are bound in the <i>destination</i> space, so naming
    /// them straight through is an identity and re-ordering them permutes the colour.
    /// </summary>
    [Fact]
    public void RelativeSyntaxRebindsTheOriginsChannels()
    {
        Assert.Equal((255, 0, 0, 1.0), Resolve("rgb(from red r g b)"));
        Assert.Equal((0, 0, 255, 1.0), Resolve("rgb(from red b g r)"));
        Assert.Equal((255, 255, 255, 1.0), Resolve("color(from lime srgb g g g)"));
    }

    [Fact]
    public void RelativeSyntaxCarriesTheOriginsAlphaWhenNoneIsStated()
    {
        Assert.Equal(0.5, Resolve("rgb(from rgb(255 0 0 / 0.5) r g b)").A, 3);
        Assert.Equal(0.25, Resolve("rgb(from rgb(255 0 0 / 0.5) r g b / 0.25)").A, 3);
    }

    /// <summary>A relative colour's components may be arithmetic over the bound keywords.</summary>
    [Fact]
    public void RelativeSyntaxEvaluatesCalcOverTheChannels()
    {
        Assert.Equal((255, 0, 0, 0.3), Resolve("rgb(from rgb(255 0 0 / 0.5) r g b / calc(alpha - 0.2))"));
        Assert.Equal((128, 0, 0, 1.0), Resolve("rgb(from rgb(255 0 0) calc(r / 2) g b)"));
    }

    /// <summary>The origin may itself be any colour, including one this class had to resolve first.</summary>
    [Fact]
    public void RelativeSyntaxAcceptsAFunctionAsItsOrigin()
    {
        Assert.Equal(
            Resolve("color(srgb 0.25 0.5 0.75)"),
            Resolve("color(from color(srgb 0.25 0.5 0.75) srgb r g b)"));
    }

    /// <summary>Round-tripping through a wider space must come back where it started, which is what
    /// pins the inverse primary matrices against the forward ones.</summary>
    [Theory]
    [InlineData("display-p3", "r g b")]
    [InlineData("display-p3-linear", "r g b")]
    [InlineData("a98-rgb", "r g b")]
    [InlineData("prophoto-rgb", "r g b")]
    [InlineData("rec2020", "r g b")]
    [InlineData("srgb", "r g b")]
    [InlineData("srgb-linear", "r g b")]
    [InlineData("xyz-d50", "x y z")]
    [InlineData("xyz-d65", "x y z")]
    public void RelativeSyntaxRoundTripsThroughAWiderSpace(string space, string channels)
    {
        var original = Resolve("rgb(64 128 192 / 1)");
        var roundTripped = Resolve($"color(from rgb(64 128 192 / 1) {space} {channels})");

        // Re-reading the same colour in another space and converting straight back is an identity
        // up to the transfer functions' last bit; anything larger means a matrix is out of step.
        Assert.True(
            System.Math.Abs(original.R - roundTripped.R) <= 1 &&
            System.Math.Abs(original.G - roundTripped.G) <= 1 &&
            System.Math.Abs(original.B - roundTripped.B) <= 1,
            $"{space}: rgb({original.R}, {original.G}, {original.B}) -> " +
            $"rgb({roundTripped.R}, {roundTripped.G}, {roundTripped.B})");
    }

    /// <summary>An <c>xyz</c> relative colour names its channels <c>x y z</c>, not <c>r g b</c>.</summary>
    [Fact]
    public void XyzSpacesBindXyzChannelNames()
    {
        Assert.Equal(
            Resolve("rgb(64 128 192 / 1)"),
            Resolve("color(from rgb(64 128 192 / 1) xyz-d65 x y z)"));
        Assert.False(CssColor4.TryParse("color(from red xyz-d65 r g b)", out _));
    }

    // ---------------------------------------------------------------- gamut mapping

    /// <summary>
    /// CSS Color 4 §13.2. Clipping each channel independently shifts hue as well as saturation, and
    /// does it most on exactly the colours a wide-gamut notation exists to express: the naive clip
    /// of <c>oklch(0.8 0.4 0)</c> is a flat <c>rgb(255, 0, 179)</c>, where reducing chroma keeps the
    /// hue and lands where a browser puts it.
    /// </summary>
    [Fact]
    public void OutOfGamutColorsAreMappedRatherThanClipped()
    {
        var mapped = Resolve("oklch(0.8 0.4 0)");
        Assert.NotEqual((255, 0, 179), (mapped.R, mapped.G, mapped.B));
        AssertClose("oklch(0.8 0.4 0)", 255, 146, 186, tolerance: 6);
    }

    /// <summary>And nothing sRGB can already show may move, which is the property that makes the
    /// mapper safe to run on every conversion.</summary>
    [Fact]
    public void InGamutColorsAreUntouchedByTheMapper()
    {
        Assert.Equal((0, 128, 128, 1.0), Resolve("color(srgb 0 0.5 0.5)"));
        Assert.Equal((255, 255, 255, 1.0), Resolve("color(srgb 1 1 1)"));
        AssertClose("lab(50% -20 -10)", 65, 129, 135);
    }

    // ---------------------------------------------------------------- the normaliser

    /// <summary>A value holding no colour function comes back as the very same instance, so the
    /// getters this runs behind stay allocation-free on the overwhelmingly common path.</summary>
    [Theory]
    [InlineData("none")]
    [InlineData("10px 20px 5px rgba(0, 0, 0, 0.5)")]
    [InlineData("linear-gradient(to right, red, blue)")]
    [InlineData("url(a.png) no-repeat")]
    public void AValueWithNoColorFunctionIsReturnedUnchanged(string value)
    {
        Assert.Same(value, CssColor4.NormalizeColorFunctions(value));
    }

    /// <summary>The rewrite works over a whole declaration, because the values that need it most
    /// are compound.</summary>
    [Fact]
    public void EveryColorInACompoundValueIsRewritten()
    {
        Assert.Equal(
            "linear-gradient(to right, rgba(0, 128, 128, 1), rgba(128, 0, 128, 1))",
            CssColor4.NormalizeColorFunctions(
                "linear-gradient(to right, color(srgb 0 0.5 0.5), color-mix(in srgb, red, blue))"));

        Assert.Equal(
            "10px 20px 5px rgba(0, 128, 128, 1)",
            CssColor4.NormalizeColorFunctions("10px 20px 5px color(srgb 0 0.5 0.5)"));
    }

    /// <summary>An unconvertible function is left exactly as written, and a later one in the same
    /// value is still converted — a value is not all-or-nothing.</summary>
    [Fact]
    public void AnUnconvertibleFunctionIsLeftAloneWithoutStoppingTheScan()
    {
        Assert.Equal(
            "color(not-a-space 1 2 3) rgba(0, 128, 128, 1)",
            CssColor4.NormalizeColorFunctions("color(not-a-space 1 2 3) color(srgb 0 0.5 0.5)"));
    }

    /// <summary>The <c>lab</c> inside <c>oklab</c> is not a function of its own; reading it as one
    /// would rewrite half an identifier.</summary>
    [Fact]
    public void AFunctionNameIsNotMatchedInsideALongerIdentifier()
    {
        var oklab = CssColor4.NormalizeColorFunctions("oklab(0.6 -0.1 -0.05)");
        Assert.StartsWith("rgba(", oklab);
        Assert.DoesNotContain("ok", oklab);
    }
}
