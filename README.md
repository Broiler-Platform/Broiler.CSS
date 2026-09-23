# Broiler.CSS

A dependency-free CSS component for Broiler targeting .NET 10.

It contains:

- `Broiler.CSS`: CSS syntax, rules, selectors, declarations, values, diagnostics,
  parsing, and serialization.
- `Broiler.CSS.Dom`: selector matching, cascade, computed values, and DOM-facing
  style services over `Broiler.DOM`.

## Install

```bash
dotnet add package Broiler.CSS --prerelease
dotnet add package Broiler.CSS.Dom --prerelease
```

`Broiler.CSS` has no dependencies. `Broiler.CSS.Dom` depends on `Broiler.CSS` and
`Broiler.Dom`, both from nuget.org.

## Preview status

This is preview software. Its API and behavior may change without compatibility
guarantees. Substantial implementation work was AI-assisted. Human-review approval is
revision-scoped; consult [HUMAN_REVIEW.md](https://github.com/Broiler-Platform/Broiler.CSS/blob/main/HUMAN_REVIEW.md)
for the reviewed revision and conditions before describing the current checkout as
approved.

Broiler.CSS is an independent Broiler component. It is used by Broiler.HTML, whose
rendering lineage comes from HTML Renderer, but it must not be represented as an official
HTML Renderer component or as endorsed by that project's contributors.

## Build and test

Every dependency restores from nuget.org; `NuGet.config` clears machine-level sources so
a restore resolves the same way everywhere. Versions are pinned in
`Directory.Packages.props`.

```bash
dotnet build Broiler.CSS.slnx -c Release
pwsh -File eng/run-tests.ps1 -Configuration Release
node --test eng/resolve-preview-version.test.mjs
pwsh -File eng/pack.ps1
```

## Continuous integration and publishing

CI builds and tests `Release` on Ubuntu and Windows, then packs and verifies both
packages on Ubuntu and attaches them as `nuget-packages`. **Publish** (manual, or a
`v0.1.0-preview.N` tag) resolves the next preview version, reruns CI with it, verifies a
fresh consumer restore against nuget.org, and pushes the validated packages and their
symbols to nuget.org with the `NUGET_API_KEY` secret. `dry-run=true` is the default for
manual runs.

The preview number is cumulative: it is one past the highest preview either package has
ever had on nuget.org, and never below the `VersionSuffix` floor in
`Directory.Build.props`. The floor records numbers spent on the retired GitHub Packages
feed (preview.1 to preview.6), so nuget.org never reuses one.

## Documentation

- [Current roadmap](https://github.com/Broiler-Platform/Broiler.CSS/blob/main/docs/roadmap.md) — the small amount of work still open
- [Human-review record](https://github.com/Broiler-Platform/Broiler.CSS/blob/main/HUMAN_REVIEW.md) — revision-scoped preview decision

## License

Broiler.CSS is licensed under the [Apache License 2.0](https://github.com/Broiler-Platform/Broiler.CSS/blob/main/LICENSE). Third-party material, if
present, retains the license identified with that material. The license provides the
software on an “AS IS” basis, without warranties or conditions.
