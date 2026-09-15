# Human review: Broiler.CSS

> **Status: PENDING HUMAN REVIEW for the next preview.**
>
> The previous approval (first preview, commit `44e5444`, 2026-07-01) does not cover the
> revision below. Until a human reviewer completes the decision and attestation, the
> current checkout must not be described as approved.

## Review target

- **Component:** Broiler.CSS
- **Scope:** CSS syntax, rules, selectors, declarations, values, diagnostics, parsing,
  serialization, and DOM-facing CSS assemblies (`Broiler.CSS`, `Broiler.CSS.Dom`).
- **Release:** Next preview (`0.1.0-preview.N`; the Publish workflow selects `N`)
- **Commit:** `26724b8ba9e93dbae643d093f6145aa6207570aa`
- **Previously reviewed commit:** `44e5444cc29555e7112c2a2c06e5dcc0c661be5d`
- **Reviewer:** _to be completed by the human reviewer_
- **Reviewer handle:** _to be completed_
- **Review date:** _to be completed_
- **Intended preview use:** Preview Broiler.CSS development and integration use, with no
  compatibility or stability guarantees.

Commits that change only this file do not change the reviewed source. Any other source
change after the commit above requires renewed review.

## Changes since the previously reviewed commit

`git log 44e5444..26724b8` has 171 commits; `git diff --shortstat 44e5444 26724b8`
reports 102 files changed, 16,768 insertions and 4,277 deletions, including tests and
documentation. The themes below come from the recent first-parent history. They are not
a substitute for reviewing the diff.

- **DOM dependency:** the nested Broiler.DOM git submodule was replaced by the
  `Broiler.Dom` package from GitHub Packages (`e0cb9b6`), now pinned to
  `0.1.0-preview.2` (#33). Local copies of `textContent` and element traversal were
  removed in favour of the package's API.
- **CI/CD and packaging:**
  - the shared reusable CI, Publish workflow, and `eng/` pack, feed-verification and
    version-resolver scripts (#28, #33)
  - central package management and the vendored packaging props
  - Source Link taken from the SDK (#31)
- **Selector matching:**
  - `:is()` aliases no longer match every element (`0bf7be6`)
  - `:link`/`:visited`, including SVG links (`a6da33d`, `6531f51`)
  - `:dir()` (`f960f94`)
  - the constraint-validation pseudo-classes (`e1c5f5f`)
  - `:nth-child(An+B of S)` (`076ed5d`)
- **Cascade and values:**
  - CSS Nesting (`fb9bda7`)
  - `contrast-color()` and `style()` container queries (`719ce82`)
  - system colours and logical viewport units (`abdc6e0`)
  - media range syntax and `@custom-media` (`56eea09`)
  - math functions wherever a length is expected (`de940e2`)
  - paged media (`c9d4f92`)
  - the `@supports` evaluator for `CSS.supports()` (`8be7a65`)
  - escape and invalid-declaration handling (`e851c08`, `3829101`)
  - `font-weight: bolder`/`lighter` resolved by the CSS Fonts 4 relative-weight table
    (#36)
  - an escape for zero, a surrogate, or a value above U+10FFFF decoding to U+FFFD instead
    of throwing, in `@font-face` family names and in selectors (#40)
  - CSS numbers parsed and formatted with the invariant culture, and unit suffixes
    matched ordinally, whatever the host's locale (#42)
  - lengths accepting exactly the CSS `<number>` forms: an exponent such as `1e2px` is
    read, and a thousands separator, trailing sign or dot, `NaN` and `Infinity` are
    rejected (#44)
- **Concurrency and performance:** sharded style-engine memo caches and a memoized
  cascade projection (`c3d5a73`), the cascade rule index (#27), and every memo store
  publishing through the cache-generation guard, so a result computed across an
  invalidation is no longer cached (#38).
- **Public API:**
  - read-only `@position-try` and `@font-feature-values` maps (#29, #30)
  - `CssStyleRule.Range` and `CssAtRule.Range` (#33)
  - `GetSparseComputedStyle` returning a fresh, caller-owned map as documented, instead of
    the engine's cached sparse map or a shared empty map (#46)
- **Build hygiene:** all compiler warnings fixed (#33, #34), and xUnit `Timeout`
  removed from synchronous tests after the test-package update (#33).

## Evidence

The evidence below was assembled with AI assistance on 2026-09-15. It is input to the
review, not its result; the reviewer confirms it by ticking the checklist.

- [ ] Build and automated tests completed.
- [ ] Security-sensitive behavior was considered for the reviewed scope.
- [ ] AI-assisted or generated code was treated as requiring human source-level review.
- [ ] Public API and preview compatibility risks were assessed at preview level.
- [ ] Known limitations and residual risks are listed below.

### Assembled evidence

- **CI:** `CI` run 34944333174 on the target commit passed on `ubuntu-latest` and
  `windows-latest`. It covered the Release build, `eng/run-tests.ps1`, and package pack
  and verification, restoring the published `Broiler.Dom 0.1.0-preview.2` from GitHub
  Packages.
- **Local build and tests:** run on 2026-09-15 on Windows with .NET SDK 10.0.400. The
  source was identical to the target commit (the tested tree differs from it only in this
  file), built against `Broiler.Dom 0.1.0-preview.2` packed from the identical Broiler.DOM
  source.
  - `dotnet build Broiler.CSS.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
  - `Broiler.CSS.Tests`: 399 passed, 0 failed, 1 skipped (a corpus that is not in this
    repository).
  - `Broiler.CSS.Dom.Tests`: 454 passed, 0 failed, 0 skipped.
- **Runtime dependencies:** `Broiler.CSS` has no project or package references.
  `Broiler.CSS.Dom` references `Broiler.CSS` and the `Broiler.Dom` package only.
- **Security-sensitive API sweep (production code):** no file-system, process,
  native-interop, reflection, dynamic-loading, or network-client usage was found. Present:
  - `System.Net.WebUtility.HtmlDecode` (`CssStyleEngine.Computed.cs`)
  - one bounded `stackalloc char[64]` (`SupportsConditionSyntax.cs`)
  - `Environment.CurrentManagedThreadId` in a diagnostic message (`CssLengthParser.cs`)

  The only regular expression built from document input is the HTML `pattern` attribute
  in `CssSelectorMatcher.MatchesPatternAttribute`. It runs with a 250 ms match timeout,
  and an invalid or timed-out pattern counts as no violation.

### Commands

```powershell
dotnet build .\Broiler.CSS.slnx -c Release
.\eng\run-tests.ps1 -Configuration Release
```

## Findings and residual risks

### Carried from the first-preview review

- **Dead code:** a large amount of dead, inactive, or transitional code remains. It was
  accepted for the first preview while the global refactoring continues.
- **Security:** no directly security-critical code paths were identified in the
  first-preview scope.
- **Preview stability:** APIs and behavior may still change.

### Open items from an AI-assisted code review (2026-09-14), not yet confirmed by the reviewer

- **Per-thread state in shared caches:** the per-thread quirks-mode and paged-media
  state influences caches shared by all threads.
- **Duplication and dead code:** duplicated top-level scanners and unit tables, and
  public types with no references in this repository.

## Decision

- [ ] **APPROVED FOR PREVIEW** within the intended-use scope above.
- [ ] **APPROVED WITH CONDITIONS** listed below.
- [ ] **NOT APPROVED** for preview use.

**Conditions:** _to be completed by the reviewer._

## Human attestation

I confirm that I am a human developer, that I personally reviewed the revision and
evidence identified above, and that the decision is my own. I understand that this
attestation is a scoped engineering review, not a warranty or a claim that the component
is free of defects or vulnerabilities.

- **Name:** _to be completed_
- **Signature or attributable identity:** _to be completed_
- **Date:** _to be completed_

AI tools may help assemble evidence, but the reviewer identity, decision, and attestation
remain the responsibility of the human reviewer.

## Previous review

- **First preview: APPROVED FOR FIRST PREVIEW.**
  - Reviewer: Maik Ratzmer (`MaiRat`).
  - Commit: `44e5444cc29555e7112c2a2c06e5dcc0c661be5d`.
  - Date: 2026-07-01.
  - Tests: 80 passed (`Broiler.CSS.Tests` 22, `Broiler.CSS.Dom.Tests` 58).
  - Conditions: none; dead code accepted as a preview limitation.
- **Full signed record:** `git show aca404f:HUMAN_REVIEW.md`.
