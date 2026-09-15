using System.Globalization;
using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

public sealed class CssSelectorMatcherCultureTests
{
    /// <summary>
    /// An <c>An+B</c> coefficient is CSS syntax, not a localized number. The matcher parsed
    /// <c>A</c> and <c>B</c> with the current culture, so under a culture whose minus sign is not
    /// <c>-</c> the <c>-2</c> in <c>-2n+3</c> failed to parse, fell back to 0, and the selector
    /// matched only the third child.
    /// </summary>
    [Fact]
    public void Nth_Child_Reads_A_Negative_Coefficient_Under_A_Culture_With_Another_Minus_Sign()
    {
        var document = new DomDocument();
        var list = document.CreateElement("ul");
        document.AppendChild(list);
        var items = new DomElement[4];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = document.CreateElement("li");
            list.AppendChild(items[i]);
        }

        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "~";
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            var matcher = new CssSelectorMatcher();

            // -2n+3 selects the third and first children.
            Assert.Equal(
                new[] { true, false, true, false },
                items.Select(item => matcher.Matches(item, "li:nth-child(-2n+3)")).ToArray());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
