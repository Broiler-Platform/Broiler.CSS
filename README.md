# Broiler.CSS

A dependency-free CSS component for Broiler targeting .NET 10.

It contains:

- `Broiler.CSS`: CSS syntax, rules, selectors, declarations, values, diagnostics,
  parsing, and serialization.
- `Broiler.CSS.Dom`: selector matching, cascade, computed values, and DOM-facing
  style services over `Broiler.DOM`.

## Preview status

This is first-preview software. Its API and behavior may change without compatibility
guarantees. Substantial implementation work was AI-assisted. Human-review approval is
revision-scoped; consult [HUMAN_REVIEW.md](HUMAN_REVIEW.md) for the reviewed revision
and conditions before describing the current checkout as approved.

Broiler.CSS is an independent Broiler component. It is used by Broiler.HTML, whose
rendering lineage comes from HTML Renderer, but it must not be represented as an official
HTML Renderer component or as endorsed by that project's contributors.

## Build and test

Initialize the nested DOM submodule, then build or test the solution:

```bash
git submodule update --init --recursive
dotnet build Broiler.CSS.slnx
dotnet test Broiler.CSS.slnx
```

## Documentation

- [Current roadmap](docs/roadmap.md) — the small amount of work still open
- [Human-review record](HUMAN_REVIEW.md) — revision-scoped preview decision

## License

Broiler.CSS is licensed under the [Apache License 2.0](LICENSE). Third-party material, if
present, retains the license identified with that material. The license provides the
software on an “AS IS” basis, without warranties or conditions.
