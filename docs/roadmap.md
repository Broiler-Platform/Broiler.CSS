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

### Preview review

The human-review record applies to a specific revision. Before a new preview claim,
review changes since that revision and update `HUMAN_REVIEW.md` with the exact commit and
scope.

## Scope

Layout, painting, CSSOM JavaScript wrappers, and browser timelines are owned by their
respective components. New work belongs here only when it changes CSS syntax, selectors,
cascade, computed values, or the DOM-facing style service.
