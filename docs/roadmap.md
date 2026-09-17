# Broiler.CSS roadmap

This roadmap contains only work that is still open. The CSS extraction and the
renderer/bridge cutover are complete; their implementation history belongs in Git.

## Current work

### Device-pixel and high-DPI length handling

`Broiler.CSS/CssLengthParser.cs` still marks the `px` conversion path for high-DPI
follow-up. Define which layer owns CSS-pixel-to-device-pixel conversion, remove the
placeholder adjustment, and add tests covering normal and high-DPI environments without
making parsing platform-dependent.

Exit gate:

- CSS parsing remains deterministic and platform-neutral.
- Computed lengths are covered at more than one device scale.
- Broiler.HTML consumes the result without applying the scale twice.

### User-agent display defaults that depart from the HTML stylesheet

`Broiler.CSS.Dom/CssUserAgentDefaults.cs` `DisplayValues` maps each tag name to one
`display` value, and departs from the HTML Standard's user-agent stylesheet (Rendering,
Hidden elements and Flow content) in three places:

- `dialog` is `none`. The stylesheet has `dialog { display: block }` and
  `dialog:not([open]) { display: none }`, which a one-value-per-tag table cannot express,
  so an open dialog's display currently has to come from the host.
- `basefont` has no entry. Broiler.Dom.Html's `HtmlElementNames.VoidElements` lacks
  `basefont`, `bgsound` and `keygen`, so a `<basefont>` contains its following siblings
  and hiding it would hide them too. Hide it once Broiler.Dom.Html parses it as a void
  element.
- `rp` has no entry. Broiler.HTML has no ruby layout and paints the `rp` parentheses as
  the only separator between an `rt` and its base text. Hide it once ruby layout lands.

Broiler.HTML's `CssDefaults` makes the same `basefont` and `rp` choices; change both
sheets together.

Exit gate:

- An open `dialog` computes to `block` and a closed one to `none` for every consumer of
  the shared defaults.
- `basefont` and `rp` are `none` in `DisplayValues` and in Broiler.HTML's default
  stylesheet, and the tests that pin their absence are replaced.

### Preview review

The human-review record applies to a specific revision. Before a new preview claim,
review changes since that revision and update `HUMAN_REVIEW.md` with the exact commit and
scope.

## Scope

Layout, painting, CSSOM JavaScript wrappers, and browser timelines are owned by their
respective components. New work belongs here only when it changes CSS syntax, selectors,
cascade, computed values, or the DOM-facing style service.
