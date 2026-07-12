namespace Broiler.CSS.Tests;

/// <summary>
/// Behavior-parity guard for <see cref="CssPriority"/>. This is the canonical home
/// for the CSSOM string-level <c>!important</c> handling the HtmlBridge
/// <c>CSSStyleDeclaration</c> surface previously owned privately (DOM/CSS promotion
/// Phase 1 slice 2). Cases pin the pre-refactor lenient-suffix contract.
/// </summary>
public sealed class CssPriorityTests
{
    [Theory]
    [InlineData("10px !important", "10px")]
    [InlineData("10px", "10px")]
    [InlineData("red ! important", "red")]        // lenient whitespace around '!'
    [InlineData("red !IMPORTANT", "red")]         // case-insensitive
    [InlineData("  blue  !important  ", "blue")]  // trims result
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Strip_Removes_Trailing_Important(string? value, string expected)
        => Assert.Equal(expected, CssPriority.Strip(value));

    [Theory]
    [InlineData("10px !important", "important")]
    [InlineData("red ! important", "important")]
    [InlineData("red !IMPORTANT", "important")]
    [InlineData("10px", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Parse_Reports_Important_Priority(string? value, string expected)
        => Assert.Equal(expected, CssPriority.Parse(value));

    [Theory]
    [InlineData("10px", "important", "10px !important")]
    [InlineData("10px !important", "important", "10px !important")] // re-apply is idempotent
    [InlineData("10px !important", "", "10px")]                     // clearing priority strips it
    [InlineData("red", "IMPORTANT", "red !important")]              // priority is case-insensitive
    [InlineData("red", " important ", "red !important")]            // priority is trimmed
    [InlineData("red", null, "red")]
    public void Apply_Sets_Or_Clears_Important(string value, string? priority, string expected)
        => Assert.Equal(expected, CssPriority.Apply(value, priority));
}
