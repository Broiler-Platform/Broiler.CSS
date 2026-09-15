using System.Globalization;

namespace Broiler.CSS.Tests;

/// <summary>
/// CSS numbers use a period as the decimal separator whatever culture the host process runs under.
/// Each test switches the current culture to one whose number format would change the answer if the
/// code consulted it, and restores the previous culture afterwards.
/// </summary>
public sealed class CssCultureInvarianceTests
{
    /// <summary>
    /// The unit-suffixed fallback parsed the number with the current culture, so the culture's own
    /// decimal separator made <c>1·5px</c> a valid length. A separator that collides with nothing in
    /// the invariant format is used on purpose: the fallback's number style also admits the
    /// invariant thousands separator, so <c>1,5px</c> reads as <c>15px</c> in every culture, which is
    /// a separate question from culture.
    /// </summary>
    [Fact]
    public void IsValidLength_Ignores_The_Current_Cultures_Decimal_Separator()
    {
        WithCulture(NumberCulture(decimalSeparator: "·", groupSeparator: " "), () =>
        {
            Assert.True(CssLengthParser.IsValidLength("1.5px"));
            Assert.False(CssLengthParser.IsValidLength("1·5px"));
        });
    }

    [Fact]
    public void Percentage_Length_Serializes_With_A_Period_Under_A_Comma_Culture()
    {
        WithCulture(NumberCulture(decimalSeparator: ",", groupSeparator: "."), () =>
        {
            var length = new CssLength("50.5%");

            Assert.Equal(length.Number.ToString(CultureInfo.InvariantCulture) + "%", length.ToString());
            Assert.DoesNotContain(",", length.ToString());
        });
    }

    /// <summary>
    /// The serializer used to format with the current culture and then replace a comma with a
    /// period, which repaired only the cultures whose decimal separator happens to be a comma.
    /// </summary>
    [Fact]
    public void Unit_Length_Serializes_With_A_Period_Whatever_The_Decimal_Separator()
    {
        WithCulture(NumberCulture(decimalSeparator: "·", groupSeparator: " "), () =>
            Assert.Equal("1.5px", new CssLength("1.5px").ToString()));
    }

    private static CultureInfo NumberCulture(string decimalSeparator, string groupSeparator)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = decimalSeparator;
        culture.NumberFormat.NumberGroupSeparator = groupSeparator;
        return culture;
    }

    private static void WithCulture(CultureInfo culture, Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
