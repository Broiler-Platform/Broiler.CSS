using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

/// <summary>
/// The constraint-validation pseudo-classes <c>:valid</c>/<c>:invalid</c>/<c>:required</c>/
/// <c>:optional</c> (HTML §4.10.16, Selectors 4 §11.3).
/// <para>
/// All four used to be listed as recognised-but-unmodelled, so they fell through to the lenient
/// default and matched <em>every</em> element — <c>&lt;html&gt;</c> and <c>&lt;body&gt;</c>
/// included, whose background propagates to the canvas. A bare
/// <c>:invalid { background-color: … }</c>, which is how WPT's form-validation tests write it,
/// therefore painted the whole page instead of the failing control (issue #1552 problem 21,
/// <c>html/semantics/forms/constraints/form-validation-validity-textarea-defaultValue</c>).
/// </para>
/// <para>Expectations here are the reference browser's, measured on the same 40 constructed cases
/// the renderer was calibrated against.</para>
/// </summary>
public sealed class CssSelectorMatcherValidityTests
{
    /// <summary>Builds <c>&lt;html&gt;&lt;body&gt;{markup}</c> and returns the matcher plus the
    /// element carrying <paramref name="id"/>.</summary>
    private static (CssSelectorMatcher Matcher, DomElement Target, DomElement Body) Build(
        Action<DomDocument, DomElement> markup, string id = "x")
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        document.AppendChild(html);
        var body = document.CreateElement("body");
        html.AppendChild(body);
        markup(document, body);

        var target = Find(body, id) ?? throw new InvalidOperationException($"no element with id '{id}'");
        return (new CssSelectorMatcher(), target, body);
    }

    private static DomElement? Find(DomElement root, string id) =>
        root.Descendants().OfType<DomElement>().FirstOrDefault(e => e.Id == id);

    /// <summary>Appends one element with the given attributes (and optional text) to
    /// <paramref name="parent"/>.</summary>
    private static DomElement Add(DomDocument document, DomElement parent, string tag,
        string? id = null, string? text = null, (string Name, string Value)[]? attributes = null)
    {
        var element = document.CreateElement(tag);
        if (id is not null)
            element.SetAttribute("id", id);
        foreach (var (name, value) in attributes ?? [])
            element.SetAttribute(name, value);
        if (text is not null)
            element.AppendChild(document.CreateTextNode(text));
        parent.AppendChild(element);
        return element;
    }

    /// <summary>Renders which of the four pseudo-classes match, as a compact "VIRO" string, so a
    /// failure prints the whole state rather than one boolean.</summary>
    private static string State(CssSelectorMatcher matcher, DomElement element) =>
        (matcher.Matches(element, ":valid") ? "V" : "-")
        + (matcher.Matches(element, ":invalid") ? "I" : "-")
        + (matcher.Matches(element, ":required") ? "R" : "-")
        + (matcher.Matches(element, ":optional") ? "O" : "-");

    /// <summary>
    /// The bug itself: an element that takes no part in constraint validation matches neither
    /// <c>:valid</c> nor <c>:invalid</c>. <c>&lt;body&gt;</c> is the one that mattered — its
    /// background propagates to the canvas, so matching it painted the whole page.
    /// </summary>
    [Theory]
    [InlineData("div")]
    [InlineData("p")]
    [InlineData("span")]
    [InlineData("output")]
    public void NonFormElements_MatchNeitherValidNorInvalid(string tag)
    {
        var (matcher, target, body) = Build((d, b) => Add(d, b, tag, id: "x"));

        Assert.Equal("----", State(matcher, target));
        Assert.Equal("----", State(matcher, body));
    }

    /// <summary>A control with an unsatisfied <c>required</c> is <c>:invalid</c>; satisfying it
    /// makes it <c>:valid</c>. Either way it stays <c>:required</c>.</summary>
    [Theory]
    // textarea's value is its text content.
    [InlineData("textarea", null, "-IR-")]
    [InlineData("textarea", "a", "V-R-")]
    public void RequiredTextarea_TracksItsTextContent(string tag, string? text, string expected)
    {
        var (matcher, target, _) = Build((d, b) =>
            Add(d, b, tag, id: "x", text: text, attributes: [("required", "")]));

        Assert.Equal(expected, State(matcher, target));
    }

    /// <summary>
    /// <c>minlength</c> never invalidates. HTML makes "suffering from being too short" conditional
    /// on the value having been edited by the user, and nothing in a static render has been — the
    /// exact expectation <c>form-validation-validity-textarea-defaultValue</c> encodes when it
    /// requires <c>&lt;textarea minlength=5 required&gt;a&lt;/textarea&gt;</c> to be valid.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void MinLength_DoesNotInvalidate_BecauseNothingWasUserEdited()
    {
        var (matcher, target, _) = Build((d, b) =>
            Add(d, b, "textarea", id: "x", text: "a", attributes: [("minlength", "5"), ("required", "")]));

        Assert.Equal("V-R-", State(matcher, target));
    }

    /// <summary>An element barred from constraint validation matches neither half — it is not
    /// "valid by default", it is out of the partition entirely.</summary>
    [Theory]
    [InlineData("disabled")]
    [InlineData("readonly")]
    public void BarredControl_MatchesNeitherHalf(string attribute)
    {
        var (matcher, target, _) = Build((d, b) =>
            Add(d, b, "textarea", id: "x", attributes: [("required", ""), (attribute, "")]));

        Assert.Equal("--R-", State(matcher, target));
    }

    /// <summary>A control inside a disabled <c>&lt;fieldset&gt;</c> is disabled too, so it is
    /// barred even though it carries no <c>disabled</c> attribute of its own.</summary>
    [Fact(Timeout = 600000)]
    public void ControlInsideDisabledFieldset_IsBarred()
    {
        var (matcher, target, _) = Build((d, b) =>
        {
            var fieldset = Add(d, b, "fieldset", attributes: [("disabled", "")]);
            Add(d, fieldset, "input", id: "x", attributes: [("required", "")]);
        });

        Assert.Equal("--R-", State(matcher, target));
    }

    /// <summary>The constraint-free input types carry no validity state, and ignore
    /// <c>required</c> so they stay <c>:optional</c>.</summary>
    [Theory]
    [InlineData("hidden")]
    [InlineData("reset")]
    [InlineData("button")]
    [InlineData("image")]
    public void ConstraintFreeInputTypes_AreOutOfThePartition(string type)
    {
        var (matcher, target, _) = Build((d, b) =>
            Add(d, b, "input", id: "x", attributes: [("type", type), ("required", "")]));

        Assert.Equal("---O", State(matcher, target));
    }

    /// <summary>A submit button and a <c>&lt;button&gt;</c> are still validation candidates — the
    /// reference browser reports both <c>:valid</c> — but neither honours <c>required</c>.
    /// </summary>
    [Theory]
    [InlineData("input", "submit")]
    [InlineData("button", null)]
    public void SubmitControls_AreValidAndOptional(string tag, string? type)
    {
        var (matcher, target, _) = Build((d, b) => Add(d, b, tag, id: "x",
            attributes: type is null
                ? [("required", "")]
                : [("type", type), ("required", "")]));

        Assert.Equal("V--O", State(matcher, target));
    }

    /// <summary>A required checkbox is missing a value until it is checked. A radio is never
    /// reported missing: its state belongs to the group, and the reference browser leaves a lone
    /// unchecked required radio valid.</summary>
    [Theory]
    [InlineData("checkbox", false, "-IR-")]
    [InlineData("checkbox", true, "V-R-")]
    [InlineData("radio", false, "V-R-")]
    public void CheckableControls_UseTheirCheckedState(string type, bool @checked, string expected)
    {
        var (matcher, target, _) = Build((d, b) => Add(d, b, "input", id: "x",
            attributes: @checked
                ? [("type", type), ("required", ""), ("checked", "")]
                : [("type", type), ("required", "")]));

        Assert.Equal(expected, State(matcher, target));
    }

    /// <summary>A required select is missing a value until an option with a non-empty value is
    /// selected.</summary>
    [Theory]
    [InlineData(false, "-IR-")]
    [InlineData(true, "V-R-")]
    public void RequiredSelect_NeedsASelectedNonEmptyOption(bool selected, string expected)
    {
        var (matcher, target, _) = Build((d, b) =>
        {
            var select = Add(d, b, "select", id: "x", attributes: [("required", "")]);
            Add(d, select, "option", text: "choose", attributes: [("value", "")]);
            var option = Add(d, select, "option", text: "a", attributes: [("value", "a")]);
            if (selected)
                option.SetAttribute("selected", string.Empty);
        });

        Assert.Equal(expected, State(matcher, target));
    }

    /// <summary>The value-shaped violations. An empty value is only ever a <c>required</c>
    /// violation, so each of these is valid when blank.</summary>
    [Theory]
    [InlineData("email", "notanemail", null, "-I-O")]
    [InlineData("email", "a@b.com", null, "V--O")]
    [InlineData("email", "", null, "V--O")]
    [InlineData("url", "nope", null, "-I-O")]
    [InlineData("url", "https://example.com/", null, "V--O")]
    [InlineData("text", "abc", "[0-9]+", "-I-O")]
    [InlineData("text", "123", "[0-9]+", "V--O")]
    [InlineData("text", "", "[0-9]+", "V--O")]
    public void ValueShapedViolations(string type, string value, string? pattern, string expected)
    {
        var (matcher, target, _) = Build((d, b) =>
        {
            var input = Add(d, b, "input", id: "x", attributes: [("type", type), ("value", value)]);
            if (pattern is not null)
                input.SetAttribute("pattern", pattern);
        });

        Assert.Equal(expected, State(matcher, target));
    }

    /// <summary>A <c>pattern</c> is anchored at both ends: a value that merely contains a match
    /// is still a mismatch.</summary>
    [Fact(Timeout = 600000)]
    public void Pattern_IsAnchoredAtBothEnds()
    {
        var (matcher, target, _) = Build((d, b) => Add(d, b, "input", id: "x",
            attributes: [("pattern", "[0-9]+"), ("value", "12a34")]));

        Assert.Equal("-I-O", State(matcher, target));
    }

    /// <summary><c>min</c>/<c>max</c> bound a numeric input; <c>maxlength</c> does not invalidate,
    /// for the same user-edit reason as <c>minlength</c>.</summary>
    [Theory]
    [InlineData("number", "1", "5", null, "-I-O")]
    [InlineData("number", "7", "5", null, "V--O")]
    [InlineData("number", "7", null, "5", "-I-O")]
    [InlineData("number", "3", null, "5", "V--O")]
    public void NumericRangeViolations(string type, string value, string? min, string? max, string expected)
    {
        var (matcher, target, _) = Build((d, b) =>
        {
            var input = Add(d, b, "input", id: "x", attributes: [("type", type), ("value", value)]);
            if (min is not null) input.SetAttribute("min", min);
            if (max is not null) input.SetAttribute("max", max);
        });

        Assert.Equal(expected, State(matcher, target));
    }

    [Fact(Timeout = 600000)]
    public void MaxLength_DoesNotInvalidate()
    {
        var (matcher, target, _) = Build((d, b) =>
            Add(d, b, "input", id: "x", attributes: [("maxlength", "2"), ("value", "abcd")]));

        Assert.Equal("V--O", State(matcher, target));
    }

    /// <summary>A <c>&lt;form&gt;</c> and a <c>&lt;fieldset&gt;</c> take their state from the
    /// controls they contain, and an empty one is valid. They are never <c>:required</c> or
    /// <c>:optional</c> — that partition is the controls' alone.</summary>
    [Theory]
    [InlineData("form", false, "V---")]
    [InlineData("form", true, "-I--")]
    [InlineData("fieldset", false, "V---")]
    [InlineData("fieldset", true, "-I--")]
    public void FormAndFieldset_TakeTheirStateFromTheirControls(string tag, bool containsInvalid, string expected)
    {
        var (matcher, target, _) = Build((d, b) =>
        {
            var container = Add(d, b, tag, id: "x");
            var input = Add(d, container, "input", attributes: [("required", "")]);
            if (!containsInvalid)
                input.SetAttribute("value", "a");
        });

        Assert.Equal(expected, State(matcher, target));
    }

    [Fact(Timeout = 600000)]
    public void EmptyForm_IsValid()
    {
        var (matcher, target, _) = Build((d, b) => Add(d, b, "form", id: "x"));

        Assert.Equal("V---", State(matcher, target));
    }
}
