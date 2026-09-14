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

`Broiler.CSS.Dom` consumes `Broiler.Dom` as a package from GitHub Packages, so a fresh
restore needs feed credentials (see below).

```bash
dotnet build Broiler.CSS.slnx -c Release
pwsh -File eng/run-tests.ps1 -Configuration Release
node --test eng/resolve-preview-version.test.mjs
pwsh -File eng/pack.ps1
```

### Consuming Broiler packages from GitHub Packages

`NuGet.config` pins two sources — nuget.org and the Broiler-Platform GitHub Packages
feed — and clears whatever the machine has configured. Package source mapping sends
`Broiler.*` to GitHub Packages and everything else to nuget.org. Versions are pinned
in `Directory.Packages.props`.

GitHub Packages requires authentication **even for public packages**. Create a personal
access token with the `read:packages` scope and put it in your **user-level** config,
never in the committed one:

```bash
dotnet nuget update source broiler-github --username <github-user> --password <pat> --store-password-in-clear-text --configfile "$APPDATA/NuGet/NuGet.Config"
```

In GitHub Actions the workflows supply `secrets.GITHUB_TOKEN` through
`NuGetPackageSourceCredentials_broiler-github`. `Broiler.Dom` must grant this repository
Actions read access; `packages: read` alone does not grant access to packages owned by
another repository.

## Continuous integration and publishing

CI builds and tests `Release` on Ubuntu and Windows, then packs and verifies both
packages on Ubuntu and attaches them as `nuget-packages`. **Publish** (manual, or a
`v0.1.0-preview.N` tag for NuGet.org) resolves the next unused preview version, reruns
CI with it, verifies a fresh consumer restore from the destination feed, and pushes the
validated packages. `dry-run=true` is the default. The workflow and `eng/` scripts are
shared with Broiler.DOM and Broiler.Documents.

Publishing to NuGet.org also requires `Broiler.Dom` to be available there first.

## Documentation

- [Current roadmap](docs/roadmap.md) — the small amount of work still open
- [Human-review record](HUMAN_REVIEW.md) — revision-scoped preview decision

## License

Broiler.CSS is licensed under the [Apache License 2.0](LICENSE). Third-party material, if
present, retains the license identified with that material. The license provides the
software on an “AS IS” basis, without warranties or conditions.
