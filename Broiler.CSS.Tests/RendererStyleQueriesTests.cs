namespace Broiler.CSS.Tests;

public sealed class RendererStyleQueriesTests
{
    /// <summary>
    /// CSS Syntax 3 §4.3.7: an escape consumes up to six hex digits and one following white space
    /// character; zero, a surrogate, or a value above U+10FFFF decodes to U+FFFD. A surrogate used
    /// to reach <see cref="char.ConvertFromUtf32"/> and throw, and zero or an out-of-range value
    /// was dropped.
    /// </summary>
    [Theory]
    [InlineData(@"\41 BC", "ABC")]
    [InlineData(@"\1F600", "\U0001F600")]
    [InlineData(@"a\-b", "a-b")]
    [InlineData(@"\D800", "�")]
    [InlineData(@"\DFFF x", "�x")]
    [InlineData(@"\0", "�")]
    [InlineData(@"\110000", "�")]
    [InlineData(@"\FFFFFF", "�")]
    public void UnescapeIdentifier_Decodes_Escapes_And_Replaces_Invalid_Code_Points(string escaped, string expected)
    {
        Assert.Equal(expected, RendererStyleQueries.UnescapeIdentifier(escaped));
    }

    [Fact]
    public void Font_Face_Family_With_A_Surrogate_Escape_Parses_Instead_Of_Throwing()
    {
        var sheet = new CssParser().ParseStyleSheet(@"@font-face { font-family: Brand\D800; src: url(brand.woff2); }");

        var face = Assert.Single(RendererStyleQueries.GetFontFaces(sheet));
        Assert.Contains("�", face.Family);
    }
}
