namespace Broiler.CSS.Tests;

/// <summary>
/// Behavior-parity guard for <see cref="CssPropertyNames"/>. The kebab↔camel
/// mapping is the single canonical home for the conversions the HtmlBridge CSSOM
/// wrappers previously each carried privately (DOM/CSS promotion Phase 1 slice 2);
/// these cases pin the exact behavior those copies had, including the
/// vendor-prefix leading-hyphen round-trip.
/// </summary>
public sealed class CssPropertyNamesTests
{
    [Theory]
    [InlineData("background-color", "backgroundColor")]
    [InlineData("z-index", "zIndex")]
    [InlineData("color", "color")]
    [InlineData("border-top-left-radius", "borderTopLeftRadius")]
    [InlineData("-webkit-transform", "WebkitTransform")]
    [InlineData("", "")]
    public void ToDomPropertyName_Converts_Kebab_To_Camel(string css, string expected)
        => Assert.Equal(expected, CssPropertyNames.ToDomPropertyName(css));

    [Theory]
    [InlineData("backgroundColor", "background-color")]
    [InlineData("zIndex", "z-index")]
    [InlineData("color", "color")]
    [InlineData("borderTopLeftRadius", "border-top-left-radius")]
    [InlineData("WebkitTransform", "-webkit-transform")]
    [InlineData("", "")]
    public void ToCssPropertyName_Converts_Camel_To_Kebab(string dom, string expected)
        => Assert.Equal(expected, CssPropertyNames.ToCssPropertyName(dom));

    [Theory]
    [InlineData("background-color")]
    [InlineData("z-index")]
    [InlineData("border-top-left-radius")]
    [InlineData("-webkit-transform")]
    [InlineData("color")]
    public void CamelThenKebab_RoundTrips_For_Standard_And_Prefixed_Names(string css)
        => Assert.Equal(css, CssPropertyNames.ToCssPropertyName(CssPropertyNames.ToDomPropertyName(css)));

    [Theory]
    [InlineData("-webkit-transform", "transform")]
    [InlineData("-moz-box-shadow", "box-shadow")]
    [InlineData("-ms-flex", "flex")]
    [InlineData("-o-transition", "transition")]
    [InlineData("-WEBKIT-Transform", "Transform")] // prefix match is case-insensitive; remainder verbatim
    [InlineData("transform", "transform")] // no prefix → unchanged
    [InlineData("--custom-prop", "--custom-prop")] // custom property is not a vendor prefix
    [InlineData("color", "color")]
    [InlineData("", "")]
    public void StripVendorPrefix_Removes_Known_Prefixes_Only(string property, string expected)
        => Assert.Equal(expected, CssPropertyNames.StripVendorPrefix(property));
}
