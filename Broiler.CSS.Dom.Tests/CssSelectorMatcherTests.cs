using Broiler.Dom;

namespace Broiler.CSS.Dom.Tests;

public sealed class CssSelectorMatcherTests
{
    [Fact]
    public void Matches_Compound_Combinator_And_Attribute_Selectors()
    {
        var tree = CreateTree();
        var matcher = new CssSelectorMatcher();

        Assert.True(matcher.Matches(
            tree.First,
            "#host > p.item[data-state='active']:first-child"));
        Assert.True(matcher.Matches(tree.Note, "p.item > span.note"));
        Assert.True(matcher.Matches(tree.Second, "p + p"));
        Assert.True(matcher.Matches(tree.Second, "#host p:last-child"));
        Assert.False(matcher.Matches(tree.Second, "p:first-child"));
    }

    [Theory]
    [InlineData(":first-child")]
    [InlineData(":last-child")]
    [InlineData(":only-child")]
    [InlineData(":nth-child(1)")]
    [InlineData(":nth-last-child(1)")]
    [InlineData(":first-of-type")]
    [InlineData(":last-of-type")]
    [InlineData(":only-of-type")]
    [InlineData(":nth-of-type(1)")]
    [InlineData(":nth-last-of-type(1)")]
    public void Root_Element_Is_Not_A_Child_Of_Its_Document(string selector)
    {
        // Acid3 test 35: the root's parent is the document, and the root matches no
        // child-indexed pseudo-class, as in Chromium and Firefox.
        var document = new DomDocument();
        var html = document.CreateElement("html");
        var body = document.CreateElement("body");
        document.AppendChild(html);
        html.AppendChild(body);
        var matcher = new CssSelectorMatcher();

        Assert.False(matcher.Matches(html, selector));
        Assert.True(matcher.Matches(html, ":root"));
        Assert.True(matcher.Matches(body, selector));
    }

    [Fact]
    public void Element_In_A_Fragment_Keeps_Its_Siblings()
    {
        var document = new DomDocument();
        var fragment = document.CreateDocumentFragment();
        var first = document.CreateElement("p");
        var second = document.CreateElement("p");
        fragment.AppendChild(first);
        fragment.AppendChild(second);
        var matcher = new CssSelectorMatcher();

        Assert.True(matcher.Matches(first, ":first-child"));
        Assert.False(matcher.Matches(second, ":first-child"));
        Assert.True(matcher.Matches(second, ":last-child"));
    }

    [Fact]
    public void Matches_Level_Four_Functional_Pseudo_Classes()
    {
        var tree = CreateTree();
        var matcher = new CssSelectorMatcher();

        Assert.True(matcher.Matches(tree.First, "p:has(> span.note)"));
        Assert.True(matcher.Matches(tree.First, "p:is(.item, #missing)"));
        Assert.True(matcher.Matches(tree.First, "p:where(.item)"));
        Assert.True(matcher.Matches(tree.First, "p:not(.missing)"));
        Assert.True(matcher.Matches(tree.First, "p:nth-child(1 of .item)"));
        Assert.True(matcher.Matches(tree.Second, "p:nth-last-child(1)"));
    }

    [Fact]
    public void Matches_Not_With_Nested_Attribute_Selector()
    {
        // Regression: a nested attribute selector inside :not() (or :is()/:where()) must be
        // evaluated as part of the pseudo, not hoisted to a top-level positive filter.
        // Previously the attribute strip ran before pseudo extraction, so `p:not([data-state])`
        // was mis-parsed as "p AND has [data-state]" — inverting it. This inversion is what hid
        // OPEN <dialog>s under the UA rule `dialog:not([open]){display:none}`.
        var tree = CreateTree();
        var matcher = new CssSelectorMatcher();

        // tree.First HAS data-state; tree.Second does not.
        Assert.False(matcher.Matches(tree.First, "p:not([data-state])"));
        Assert.True(matcher.Matches(tree.Second, "p:not([data-state])"));

        // The positive attribute-presence direction still works.
        Assert.True(matcher.Matches(tree.First, "p[data-state]"));
        Assert.False(matcher.Matches(tree.Second, "p[data-state]"));

        // :is() with a nested attribute selector.
        Assert.True(matcher.Matches(tree.First, "p:is([data-state], .missing)"));
        Assert.False(matcher.Matches(tree.Second, "p:is([data-state], .missing)"));

        // Empty-value boolean attribute (the `<dialog open="">` shape): presence matches, and
        // :not() correctly excludes it.
        var document = new DomDocument();
        var root = document.CreateElement("body");
        document.AppendChild(root);
        var dialog = document.CreateElement("dialog");
        dialog.SetAttribute("open", "");
        root.AppendChild(dialog);
        Assert.True(matcher.Matches(dialog, "dialog[open]"));
        Assert.False(matcher.Matches(dialog, "dialog:not([open])"));

        var closed = document.CreateElement("dialog");
        root.AppendChild(closed);
        Assert.False(matcher.Matches(closed, "dialog[open]"));
        Assert.True(matcher.Matches(closed, "dialog:not([open])"));
    }

    [Fact]
    public void Unknown_PseudoClass_Invalidates_Selector_But_Recognized_Ones_Stay_Lenient()
    {
        var tree = CreateTree();
        var matcher = new CssSelectorMatcher();

        // An unrecognized pseudo-class is an invalid selector; per the Selectors
        // spec the whole rule must be ignored, so it must not match anything.
        // (Previously it matched every element — the WPT "invalid selector is
        // ignored" idiom `:bogus { background: red }` painted red everywhere.)
        Assert.False(matcher.Matches(tree.First, ":unknownpseudo"));
        Assert.False(matcher.Matches(tree.First, "p:unknownpseudo"));
        Assert.False(matcher.Matches(tree.First, "p.item:totally-made-up"));

        // Recognized-but-unmodeled pseudo-classes stay lenient (match as before),
        // so this is a strict narrowing that only rejects genuinely invalid names.
        Assert.True(matcher.Matches(tree.First, "p:defined"));
        Assert.True(matcher.Matches(tree.First, "p:read-only"));

        // Vendor-prefixed pseudo-classes are extensions, not typos — kept lenient.
        Assert.True(matcher.Matches(tree.First, "p:-webkit-anything"));
    }

    [Fact]
    public void Matches_Root_Scope_Empty_Language_And_Form_State()
    {
        var document = new DomDocument();
        var html = document.CreateElement("html");
        html.SetAttribute("lang", "en-US");
        var body = document.CreateElement("body");
        var empty = document.CreateElement("div");
        var checkbox = document.CreateElement("input");
        checkbox.SetAttribute("type", "checkbox");
        document.AppendChild(html);
        html.AppendChild(body);
        body.AppendChild(empty);
        body.AppendChild(checkbox);

        var matcher = new CssSelectorMatcher(new CheckedStateProvider(checkbox));

        Assert.True(matcher.Matches(html, ":root"));
        Assert.False(matcher.Matches(body, ":root"));
        Assert.True(matcher.Matches(body, ":not(:root)"));
        Assert.True(matcher.Matches(empty, ":scope:empty:lang(en)", empty));
        Assert.True(matcher.Matches(empty, ":lang(en-*-US)"));
        Assert.True(matcher.Matches(checkbox, "input:enabled:checked"));
    }

    // The user-action pseudo-classes match what the state provider reports of the element itself, and
    // nothing at all without one: a still render has nothing hovered, pressed or focused.
    [Fact]
    public void Matches_User_Action_Pseudo_Classes_From_The_State_Provider()
    {
        var tree = CreateTree();
        var host = tree.First.ParentElement!;
        var matcher = new CssSelectorMatcher(new UserActionStateProvider(new Dictionary<DomElement, CssUserActionState>
        {
            [host] = CssUserActionState.Hover | CssUserActionState.FocusWithin,
            [tree.First] = CssUserActionState.Hover | CssUserActionState.Active | CssUserActionState.FocusWithin,
            [tree.Note] = CssUserActionState.Hover | CssUserActionState.Active | CssUserActionState.Focus |
                          CssUserActionState.FocusVisible | CssUserActionState.FocusWithin,
        }));

        Assert.True(matcher.Matches(tree.Note, "#host:hover .note:focus"));
        Assert.True(matcher.Matches(tree.Note, "span:focus-visible"));
        Assert.True(matcher.Matches(tree.First, "p:active:focus-within"));
        Assert.False(matcher.Matches(tree.First, ":focus"));
        Assert.False(matcher.Matches(tree.First, ":focus-visible"));
        Assert.False(matcher.Matches(tree.Second, ":hover"));
        Assert.True(matcher.Matches(tree.Second, "p:not(:hover)"));
        Assert.True(matcher.TryMatch(tree.Note, ":hover", out var hovered) && hovered);
        Assert.True(matcher.TryMatch(tree.Second, ":active", out var active) && !active);

        var still = new CssSelectorMatcher();
        Assert.False(still.Matches(tree.Note, ":hover"));
        Assert.False(still.Matches(tree.Note, ":focus-within"));
        Assert.False(new CssSelectorMatcher(new CheckedStateProvider(tree.Note)).Matches(tree.Note, ":focus"));
    }

    // A matcher with no provider -- the renderer's, handed a page as markup -- reads the state the
    // markup carries; one with a provider asks the provider alone, so a page cannot claim a state.
    [Fact]
    public void Matches_User_Action_State_Carried_In_Markup_Only_Without_A_Provider()
    {
        var tree = CreateTree();
        tree.First.SetAttribute(CssUserActionStateMarkup.AttributeName, "hover  focus-within\tbogus");
        tree.Note.SetAttribute(CssUserActionStateMarkup.AttributeName, CssUserActionStateMarkup.Format(
            CssUserActionState.Hover | CssUserActionState.Focus | CssUserActionState.FocusVisible | CssUserActionState.FocusWithin)!);

        var renderer = new CssSelectorMatcher();
        Assert.True(renderer.Matches(tree.First, "p:hover:focus-within"));
        Assert.False(renderer.Matches(tree.First, ":focus"));
        Assert.True(renderer.Matches(tree.Note, ".note:focus:focus-visible"));
        Assert.False(renderer.Matches(tree.Second, ":hover"));
        Assert.Equal("hover focus focus-visible focus-within", tree.Note.GetAttribute(CssUserActionStateMarkup.AttributeName));
        Assert.Null(CssUserActionStateMarkup.Format(CssUserActionState.None));

        var live = new CssSelectorMatcher(new UserActionStateProvider([]));
        Assert.False(live.Matches(tree.First, ":hover"));
        Assert.False(live.Matches(tree.Note, ":focus"));
    }

    [Fact]
    public void Has_Matches_Nth_Child_And_Nested_Functions()
    {
        var document = new DomDocument();
        var target = document.CreateElement("div");
        target.Id = "target";
        document.AppendChild(target);
        for (var index = 0; index < 3; index++)
        {
            var item = document.CreateElement("div");
            item.ClassName = "item";
            target.AppendChild(item);
        }

        var matcher = new CssSelectorMatcher();

        Assert.True(matcher.Matches(target, "#target:has(.item:nth-child(3))"));
        Assert.True(matcher.Matches(target, "#target:has(:is(.item + .item + .item))"));
    }

    [Fact]
    public void Nth_Child_Of_Selector_Requires_The_Element_To_Match_The_Filter()
    {
        // Regression for the WPT test css/selectors/nth-last-child-of-tagname.html.
        // `:nth-child(An+B of S)` only matches elements that themselves match S. The
        // element's position was looked up in the *filtered* sibling list without
        // checking that the lookup succeeded, so a non-matching element got index -1;
        // the from-the-end branch then computed `count - (-1)` = count + 1, a positive
        // position that could satisfy the An+B test. With `odd` that made every
        // non-matching element with an even filtered-sibling count match — including
        // <html>, whose lime background propagated to the canvas and painted the whole
        // page (0.1% pixel match against the reference).
        var document = new DomDocument();
        var html = document.CreateElement("html");
        document.AppendChild(html);
        var body = document.CreateElement("body");
        html.AppendChild(body);

        DomElement Append(string name)
        {
            var element = document.CreateElement(name);
            body.AppendChild(element);
            return element;
        }

        // The reftest's body, in order: p p webkit p webkit webkit p p fast p p.
        var intro = Append("p");
        Append("p");
        var firstWebkit = Append("webkit");
        Append("p");
        var greenWebkit = Append("webkit");
        var lastWebkit = Append("webkit");
        Append("p");
        Append("p");
        var greenFast = Append("fast");
        Append("p");
        var lastParagraph = Append("p");

        var matcher = new CssSelectorMatcher();
        const string Selector = ":nth-last-child(odd of webkit, fast)";

        // Only the 1st and 3rd elements counting back through {webkit, fast} match.
        Assert.True(matcher.Matches(greenFast, Selector));
        Assert.True(matcher.Matches(greenWebkit, Selector));
        Assert.False(matcher.Matches(lastWebkit, Selector));
        Assert.False(matcher.Matches(firstWebkit, Selector));

        // Elements outside the `of` selector list never match, at any depth.
        Assert.False(matcher.Matches(html, Selector));
        Assert.False(matcher.Matches(body, Selector));
        Assert.False(matcher.Matches(intro, Selector));
        Assert.False(matcher.Matches(lastParagraph, Selector));

        // The forward-counting form keeps the same rule.
        Assert.True(matcher.Matches(firstWebkit, ":nth-child(1 of webkit, fast)"));
        Assert.True(matcher.Matches(greenWebkit, ":nth-child(2 of webkit, fast)"));
        Assert.False(matcher.Matches(html, ":nth-child(1 of webkit, fast)"));
        Assert.False(matcher.Matches(intro, ":nth-child(1 of webkit, fast)"));
    }

    [Fact]
    public void Specificity_Is_Owned_By_The_Css_Kernel()
    {
        Assert.Equal(
            new CssSpecificity(1, 1, 1),
            CssSelectorParser.CalculateSpecificity("p:nth-child(2 of #featured, .card)"));
    }

    private static TestTree CreateTree()
    {
        var document = new DomDocument();
        var host = document.CreateElement("div");
        host.Id = "host";
        var first = document.CreateElement("p");
        first.Id = "featured";
        first.ClassName = "item card";
        first.SetAttribute("data-state", "active");
        var note = document.CreateElement("span");
        note.ClassName = "note";
        var second = document.CreateElement("p");
        second.ClassName = "item";

        document.AppendChild(host);
        host.AppendChild(first);
        first.AppendChild(note);
        host.AppendChild(second);
        return new TestTree(first, note, second);
    }

    private sealed record TestTree(DomElement First, DomElement Note, DomElement Second);

    private sealed class CheckedStateProvider(DomElement checkedElement) : ICssSelectorStateProvider
    {
        public bool? IsChecked(DomElement element) =>
            ReferenceEquals(element, checkedElement);
    }

    // :target is the element the provider, or without one the markup, reports as its document's target.
    [Fact]
    public void Matches_Target_From_The_State_Provider_Or_The_Markup()
    {
        var tree = CreateTree();
        var live = new CssSelectorMatcher(new ElementStateProvider { States = { [tree.Note] = CssElementState.Target } });
        Assert.True(live.Matches(tree.Note, "span:target"));
        Assert.False(live.Matches(tree.First, ":target"));
        Assert.False(live.Matches(tree.Note, ":target-within"));

        tree.Second.SetAttribute(CssElementStateMarkup.AttributeName, "target");
        Assert.True(new CssSelectorMatcher().Matches(tree.Second, ":target"));
        Assert.False(live.Matches(tree.Second, ":target"));
        Assert.False(new CssSelectorMatcher().Matches(tree.First, ":target"));
    }

    // :popover-open and :modal are states only a script or the user puts an element in, as the provider,
    // or without one the markup, reports them. Both matched every element, so a closed popover was never
    // [popover]:not(:popover-open) -- the rule that hides it -- and every element was :modal.
    [Fact]
    public void Matches_Popover_Open_And_Modal_From_The_State_Provider_Or_The_Markup()
    {
        var document = new DomDocument();
        var body = document.CreateElement("body");
        var shown = document.CreateElement("div");
        shown.SetAttribute("popover", "");
        var closed = document.CreateElement("div");
        closed.SetAttribute("popover", "");
        var modal = document.CreateElement("dialog");
        modal.SetAttribute("open", "");
        var open = document.CreateElement("dialog");
        open.SetAttribute("open", "");
        document.AppendChild(body);
        body.AppendChild(shown);
        body.AppendChild(closed);
        body.AppendChild(modal);
        body.AppendChild(open);

        var live = new CssSelectorMatcher(new ElementStateProvider
        {
            States = { [shown] = CssElementState.PopoverOpen, [modal] = CssElementState.Modal },
        });
        Assert.True(live.Matches(shown, "[popover]:popover-open"));
        Assert.True(live.Matches(closed, "[popover]:not(:popover-open)"));
        Assert.False(live.Matches(body, ":popover-open"));
        Assert.True(live.Matches(modal, "dialog:modal"));
        Assert.False(live.Matches(open, ":modal"));
        Assert.False(live.Matches(body, ":modal"));

        closed.SetAttribute(CssElementStateMarkup.AttributeName, "popover-open");
        open.SetAttribute(CssElementStateMarkup.AttributeName, "modal");
        var still = new CssSelectorMatcher();
        Assert.True(still.Matches(closed, ":popover-open"));
        Assert.True(still.Matches(open, ":modal"));
        Assert.False(still.Matches(shown, ":popover-open"));
        Assert.False(still.Matches(body, ":modal"));

        Assert.Equal("popover-open modal", CssElementStateMarkup.Format(CssElementState.PopoverOpen | CssElementState.Modal));
        Assert.Equal(CssElementState.PopoverOpen | CssElementState.Modal, CssElementStateMarkup.Parse("modal popover-open"));
    }

    // :valid and :invalid judge the value the provider reports -- what the user typed or a script set
    // -- and its checkedness, rather than the markup's.
    [Fact]
    public void Judges_Validity_On_The_Live_Value()
    {
        var (document, form) = NewForm();
        var email = Control(document, form, "input", ("type", "email"), ("value", "a@b.c"));
        var notes = Control(document, form, "textarea", ("required", ""));
        var choice = Control(document, form, "select", ("required", ""));
        var empty = document.CreateElement("option");
        empty.SetAttribute("value", "");
        empty.SetAttribute("selected", "");
        choice.AppendChild(empty);
        var agree = Control(document, form, "input", ("type", "checkbox"), ("required", ""));

        var still = new CssSelectorMatcher();
        Assert.True(still.Matches(email, ":valid"));
        Assert.True(still.Matches(notes, ":invalid"));
        Assert.True(still.Matches(choice, ":invalid"));
        Assert.True(still.Matches(agree, ":invalid"));

        var live = new CssSelectorMatcher(new ElementStateProvider
        {
            Values = { [email] = "nope", [notes] = "typed", [choice] = "x" },
            Checked = { [agree] = true },
        });
        Assert.True(live.Matches(email, ":invalid"));
        Assert.True(live.Matches(notes, ":valid"));
        Assert.True(live.Matches(choice, ":valid"));
        Assert.True(live.Matches(agree, ":valid"));
        Assert.True(live.Matches(form, ":invalid"));
    }

    // minlength and maxlength judge only a value the user edited, as HTML's "too short" and "too long" do.
    [Fact]
    public void Judges_Length_Only_For_A_Value_The_User_Edited()
    {
        var (document, form) = NewForm();
        var shortField = Control(document, form, "input", ("minlength", "5"));
        var longField = Control(document, form, "input", ("type", "search"), ("maxlength", "3"));
        var notes = Control(document, form, "textarea", ("minlength", "4"));
        var number = Control(document, form, "input", ("type", "number"), ("minlength", "5"));

        var provider = new ElementStateProvider
        {
            Values = { [shortField] = "abc", [longField] = "abcd", [notes] = "ab", [number] = "12" },
        };
        var live = new CssSelectorMatcher(provider);
        Assert.True(live.Matches(shortField, ":valid"));
        Assert.True(live.Matches(longField, ":valid"));
        Assert.True(live.Matches(notes, ":valid"));

        foreach (var field in new[] { shortField, longField, notes, number })
            provider.States[field] = CssElementState.UserEdited;

        Assert.True(live.Matches(shortField, ":invalid"));
        Assert.True(live.Matches(longField, ":invalid"));
        Assert.True(live.Matches(notes, ":invalid"));
        Assert.True(live.Matches(number, ":valid"));

        provider.Values[shortField] = string.Empty;
        Assert.True(live.Matches(shortField, ":valid"));
    }

    // :user-valid and :user-invalid are :valid and :invalid for a control the user has interacted with,
    // and for nothing else.
    [Fact]
    public void Matches_User_Validity_Only_Once_The_User_Interacted()
    {
        var (document, form) = NewForm();
        var email = Control(document, form, "input", ("type", "email"), ("required", ""));
        var other = Control(document, form, "input", ("required", ""));
        var button = Control(document, form, "button");
        var provider = new ElementStateProvider { Values = { [email] = "x" } };
        var live = new CssSelectorMatcher(provider);

        Assert.True(live.Matches(email, ":invalid"));
        Assert.False(live.Matches(email, ":user-invalid"));
        Assert.False(live.Matches(email, ":user-valid"));

        provider.States[email] = CssElementState.UserInteracted;
        provider.States[form] = CssElementState.UserInteracted;
        provider.States[button] = CssElementState.UserInteracted;
        Assert.True(live.Matches(email, "input:user-invalid"));
        Assert.False(live.Matches(email, ":user-valid"));
        Assert.False(live.Matches(other, ":user-invalid"));
        Assert.False(live.Matches(form, ":user-invalid"));
        Assert.False(live.Matches(button, ":user-valid"));

        provider.Values[email] = "a@b.c";
        Assert.True(live.Matches(email, ":user-valid"));

        other.SetAttribute(CssElementStateMarkup.AttributeName, CssElementStateMarkup.Format(CssElementState.UserInteracted)!);
        Assert.True(new CssSelectorMatcher().Matches(other, ":user-invalid"));
        Assert.False(live.Matches(other, ":user-invalid"));
    }

    // :placeholder-shown is an empty field of a type that shows a placeholder, with one to show.
    [Fact]
    public void Matches_Placeholder_Shown_For_An_Empty_Field_With_A_Placeholder()
    {
        var (document, form) = NewForm();
        var text = Control(document, form, "input", ("placeholder", "Name"));
        var number = Control(document, form, "input", ("type", "number"), ("placeholder", "0"));
        var notes = Control(document, form, "textarea", ("placeholder", "Notes"));
        var blank = Control(document, form, "input", ("placeholder", ""));
        var box = Control(document, form, "input", ("type", "checkbox"), ("placeholder", "x"));
        var filled = Control(document, form, "input", ("placeholder", "Name"), ("value", "Ada"));

        var still = new CssSelectorMatcher();
        Assert.True(still.Matches(text, ":placeholder-shown"));
        Assert.True(still.Matches(number, ":placeholder-shown"));
        Assert.True(still.Matches(notes, "textarea:placeholder-shown"));
        Assert.False(still.Matches(blank, ":placeholder-shown"));
        Assert.False(still.Matches(box, ":placeholder-shown"));
        Assert.False(still.Matches(filled, ":placeholder-shown"));

        var live = new CssSelectorMatcher(new ElementStateProvider { Values = { [text] = "typed", [filled] = "" } });
        Assert.False(live.Matches(text, ":placeholder-shown"));
        Assert.True(live.Matches(filled, ":placeholder-shown"));
    }

    // :visited matches only in a visited link's visited style, where :link stops matching it, and only
    // for a link a provider reports visited: never from markup, and never for an ordinary query.
    [Fact]
    public void Matches_Visited_Only_In_A_Visited_Style_And_Only_From_A_Provider()
    {
        var document = new DomDocument();
        var visited = document.CreateElement("a");
        visited.SetAttribute("href", "/seen");
        var fresh = document.CreateElement("a");
        fresh.SetAttribute("href", "/new");
        document.AppendChild(visited);
        visited.AppendChild(fresh);

        var matcher = new CssSelectorMatcher(new ElementStateProvider { States = { [visited] = CssElementState.Visited } });
        Assert.False(matcher.Matches(visited, ":visited"));
        Assert.True(matcher.Matches(visited, ":link"));

        using (CssVisitedLinkMatching.Enter())
        {
            Assert.True(matcher.Matches(visited, "a:visited"));
            Assert.False(matcher.Matches(visited, ":link"));
            Assert.True(matcher.Matches(fresh, ":link"));
            Assert.False(matcher.Matches(fresh, ":visited"));
            Assert.True(matcher.Matches(visited, ":any-link"));

            fresh.SetAttribute(CssElementStateMarkup.AttributeName, "visited");
            Assert.False(new CssSelectorMatcher().Matches(fresh, ":visited"));
        }

        Assert.False(CssVisitedLinkMatching.Active);
        Assert.Null(CssElementStateMarkup.Format(CssElementState.Visited));
        Assert.Equal(CssElementState.Target, CssElementStateMarkup.Parse("visited  target\tbogus"));
        Assert.Equal("target user-interacted user-edited",
            CssElementStateMarkup.Format(CssElementState.Target | CssElementState.UserInteracted | CssElementState.UserEdited | CssElementState.Visited));
    }

    /// <summary>
    /// :disabled is HTML's "actually disabled", as Chromium matches it (measured): a disabled fieldset
    /// disables the controls and fieldsets in it except in its first legend, and a disabled optgroup its
    /// options; :enabled is the rest of the elements that can be disabled, and nothing else. A control in
    /// the first legend is a validation candidate again.
    /// </summary>
    [Fact]
    public void DisabledIsActuallyDisabled()
    {
        var document = new DomDocument();
        var body = document.CreateElement("body");
        document.AppendChild(body);

        DomElement Add(DomElement parent, string tag, string id, params string[] attributes)
        {
            var element = document.CreateElement(tag);
            element.SetAttribute("id", id);
            foreach (var attribute in attributes)
                element.SetAttribute(attribute, attribute == "disabled" || attribute == "required" ? string.Empty : attribute);
            parent.AppendChild(element);
            return element;
        }

        var select = Add(body, "select", "sel");
        var group = Add(select, "optgroup", "og", "disabled");
        Add(group, "option", "o1");
        Add(select, "option", "o2");
        var outer = Add(body, "fieldset", "outer", "disabled");
        var inner = Add(outer, "fieldset", "inner");
        Add(inner, "input", "deep");
        var legend = Add(outer, "legend", "first-legend");
        var inLegend = Add(legend, "fieldset", "inlegend");
        Add(inLegend, "input", "leg", "required");
        var second = Add(outer, "legend", "second-legend");
        Add(second, "input", "second");
        Add(body, "input", "plain");
        Add(body, "div", "div");

        var matcher = new CssSelectorMatcher();
        string Ids(string selector) =>
            string.Join(",", body.Descendants().OfType<DomElement>().Where(e => matcher.Matches(e, selector)).Select(e => e.GetAttribute("id")));

        Assert.Equal("og,o1,outer,inner,deep,second", Ids(":disabled"));
        Assert.Equal("sel,o2,inlegend,leg,plain", Ids(":enabled"));
        Assert.Equal("leg", Ids("input:invalid"));
    }

    private static (DomDocument Document, DomElement Form) NewForm()
    {
        var document = new DomDocument();
        var form = document.CreateElement("form");
        document.AppendChild(form);
        return (document, form);
    }

    private static DomElement Control(DomDocument document, DomElement form, string tag, params (string Name, string Value)[] attributes)
    {
        var control = document.CreateElement(tag);
        foreach (var (name, value) in attributes)
            control.SetAttribute(name, value);
        form.AppendChild(control);
        return control;
    }

    private sealed class ElementStateProvider : ICssSelectorStateProvider
    {
        public Dictionary<DomElement, CssElementState> States { get; } = new(ReferenceEqualityComparer.Instance);

        public Dictionary<DomElement, string> Values { get; } = new(ReferenceEqualityComparer.Instance);

        public Dictionary<DomElement, bool> Checked { get; } = new(ReferenceEqualityComparer.Instance);

        public bool? IsChecked(DomElement element) => Checked.TryGetValue(element, out var value) ? value : null;

        public CssElementState GetElementState(DomElement element) =>
            States.TryGetValue(element, out var state) ? state : CssElementState.None;

        public string? GetValue(DomElement element) => Values.TryGetValue(element, out var value) ? value : null;
    }

    private sealed class UserActionStateProvider(Dictionary<DomElement, CssUserActionState> states) : ICssSelectorStateProvider
    {
        public CssUserActionState GetUserActionState(DomElement element) =>
            states.TryGetValue(element, out var state) ? state : CssUserActionState.None;
    }
}
