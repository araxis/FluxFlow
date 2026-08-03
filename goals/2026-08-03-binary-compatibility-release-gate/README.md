# Goal: Enforce Package Binary Compatibility During Release

## Status

- State: complete
- Date: 2026-08-03
- Repository: `C:\Projects\FluxFlow`
- Accepted base branch: `main`
- Accepted base commit: `069e3bcae1c4fc440d72e1a914e020977378d6fb`
- Working branch: `work/binary-compat-release-gate`
- Runtime feature scope: none
- Publication scope: none

## Objective

Turn the existing .NET SDK package-validation helper into a mandatory,
fail-closed part of the normal package release path. A release must compare its
candidate assembly with the explicitly declared previously published package
version before any archive can be published. A genuine first release must be
declared explicitly and reported as such; absence or ambiguity must never be
treated as permission to skip compatibility validation.

The completed round must preserve the current lightweight release architecture:

1. keep `eng/packages.json` as the explicit package inventory and release-policy
   source;
2. reuse `eng/package-binary-compat-preflight.ps1` and .NET SDK package
   validation rather than adding another compatibility framework;
3. package once in the release workflow, using the compatibility preflight to
   create the artifact consumed by every later release step;
4. retain archive inspection, package-only consumer smoke testing, release-note
   preparation, artifact upload, collision detection, publication, public-feed
   verification, and repository-release creation in their safe order; and
5. leave runtime behavior, public APIs, package versions, package bytes,
   schemas, and published state unchanged in this implementation round.

## Current Evidence

- The canonical 59-package inventory is publicly available at each current
  project version, as recorded by the completed coordinated release train.
- `eng/package-binary-compat-preflight.ps1` already restores a baseline package
  and runs `dotnet pack` with .NET SDK package validation.
- `tests/FluxFlow.Release.Tests/PackageBinaryCompatPreflightScriptTests.cs`
  already proves the helper's core preparation and execution behavior.
- `.github/workflows/publish-nuget.yml` currently performs an ordinary
  `dotnet pack` and does not invoke binary compatibility validation.
- The preceding concurrency-hardening goal explicitly deferred this independent
  release-hardening opportunity until concurrency behavior was trustworthy.

## Compatibility Policy

Every entry in `eng/packages.json` must contain the
`binaryCompatibilityBaseline` property.

- A semantic-version string identifies the exact previously published package
  version used by .NET SDK package validation.
- JSON `null` means a genuine initial package release with no earlier binary
  contract. The release must still use the shared preflight packaging path and
  must print an explicit initial-release decision.
- A missing property, an empty string, an unsupported JSON value, or an invalid
  semantic version is a configuration error and must fail before packaging.
- Existing packages must declare a string baseline. Because all current
  versions are published, this round initializes each baseline to its current
  project version without changing that project version.
- When preparing a later release, advance the project version but retain the
  prior published version as `binaryCompatibilityBaseline`. Only after that new
  package is published can it become a future release's baseline.
- Do not discover the baseline implicitly from network ordering. The reviewed
  manifest value is the release contract.

## Implementation Plan

### 1. Manifest and resolver

1. Add `binaryCompatibilityBaseline` to all manifest entries.
2. Extend the release resolver to require the property, validate string values
   with the existing semantic-version rule, distinguish explicit `null`, and
   expose:
   - `PACKAGE_BINARY_COMPATIBILITY_BASELINE`; and
   - `PACKAGE_IS_INITIAL_RELEASE`.
3. Preserve all existing package, tag, project-version, and prerelease
   resolution behavior.
4. Keep the implementation direct PowerShell with no reflection, network
   inference, hidden default, or new dependency.

### 2. Shared compatibility packaging

1. Make the preflight consume the resolved manifest policy when no deliberate
   command-line baseline override is supplied.
2. Preserve an explicit `-BaselineVersion` override for bounded local checks,
   while still requiring a valid manifest policy.
3. Add an explicit output-directory parameter so the release workflow can
   place its sole candidate artifact in `artifacts/packages` while existing
   local behavior retains `artifacts/binary-compat` by default.
4. For a normal baseline policy:
   - require the controlled Release build outputs;
   - restore the exact baseline package without reusing a machine-global cache;
   - keep the restored archive in a fresh temporary package directory and pass
     its exact path to SDK validation;
   - run one package operation with `EnablePackageValidation=true`, the resolved
     package id, and the effective baseline version; and
   - fail on restore, compatibility, or packaging errors.
5. For explicit initial release:
   - require the controlled Release build outputs;
   - skip only the impossible prior-package comparison;
   - run the same shared packaging helper without SDK baseline arguments; and
   - print a clear initial-release result.
6. Preserve cleanup of temporary restore projects and stale package artifacts.
7. Never let a same-id/same-version locally cached package substitute for the
   declared package-source baseline.

### 3. Release workflow

1. Keep restore, Release build, solution tests, and both real-provider release
   suites before package creation.
2. Replace the ordinary `Pack` step with a named binary-compatibility package
   gate invoking the existing helper.
3. Pass the resolved package alias, version, manifest baseline, public package
   source, and `artifacts/packages` output directory explicitly.
4. Do not keep a second `dotnet pack` path in the workflow.
5. Require the gate before archive inspection and every publication-related
   step.
6. Preserve all downstream integrity checks and their ordering.

### 4. Focused verification

Add or strengthen release tests that prove:

- all real manifest entries contain an explicit, valid compatibility policy;
- every current published package uses a non-null baseline in this repository
  state;
- resolver output contains the exact baseline and initial-release flag;
- a missing property fails closed;
- empty, non-string, and invalid-version policies fail closed;
- explicit `null` resolves as an initial release;
- preflight uses the manifest baseline by default;
- an explicit local baseline override remains supported;
- the output directory is resolved safely and passed to the sole pack command;
- explicit initial release packages without SDK baseline arguments;
- normal packages retain SDK package-validation arguments;
- the release workflow invokes the helper after Release build/tests and before
  archive inspection/publication;
- the workflow has no duplicate ordinary pack command; and
- archive inspection, consumer smoke, collision checking, publication,
  feed verification, and release creation remain ordered.

Use the repository's existing xUnit and Shouldly conventions. Keep fixtures
local and deterministic; structural workflow tests must not publish packages or
require a public service.

### 5. Documentation and memory

Update:

- `docs/11-package-versioning.md` with the manifest baseline lifecycle, initial
  release representation, local override behavior, and automatic release gate;
- `docs/38-release-validation.md` with the exact release ordering and
  fail-closed compatibility boundary;
- `memory/00-index.md` and `memory/01-current-state.md`;
- one new numbered memory record; and
- this goal with exact completion evidence.

No changelog entry or package README update is required because no package
consumer behavior or package version changes.

## Non-Negotiable Principles

1. KISS, SRP, explicit data flow, and small release boundaries.
2. No reflection, assembly scanning, hidden network selection, service locator,
   new framework, or speculative abstraction.
3. No dependency addition.
4. No runtime, DSL, registration, persistence, provider, or public API changes.
5. No package version, release-note, public API baseline, tag, release, or
   publication changes.
6. No compatibility suppression or warning downgrade to force a pass.
7. Missing or malformed policy fails closed.
8. Initial release is explicit and observable, never inferred from restore
   failure.
9. Package only once in the release workflow.
10. Preserve unrelated user work and avoid broad formatting or cleanup.
11. Update goal, documentation, documentation-site content, and memory.
12. Use the normal branch, commit, pull-request, checks, review, and merge path.

## Explicit Non-Goals

- no new workflow-engine feature or component;
- no runtime performance or concurrency change;
- no automatic latest-version lookup from the public feed;
- no manifest schema framework or generalized policy engine;
- no replacement for .NET SDK package validation;
- no all-package publication or tag movement;
- no package-version bump;
- no unrelated refactoring.

## Validation Sequence

1. Run focused release tests during implementation.
2. Run resolver and preflight preparation checks for every manifest entry.
3. Run at least one real same-version compatibility pack against the public
   baseline without publishing.
4. Run the full `FluxFlow.Release.Tests` project.
5. Run the complete solution test suite in Release configuration.
6. Run the complete solution Release build with continuous-integration flags
   and zero warnings.
7. Run repository formatting and dependency/governance checks applicable to
   changed files.
8. Inspect the final diff and scan new project-visible names and text for
   neutrality.
9. Confirm no package project, project version, changelog section, public API
   baseline, tag, release, or public package state changed.

## Review and Closeout

1. Commit only goal-owned files with a neutral subject.
2. Push the branch and open a ready pull request against `main`.
3. Require successful remote checks on the exact head.
4. Resolve every actionable review finding without bypassing policy.
5. Merge normally using the repository's established merge strategy.
6. Synchronize local `main` with `origin/main` and require a clean worktree.
7. Mark this goal complete only after implementation, verification,
   documentation, memory, review, merge, cleanup, and synchronization finish.

## Acceptance Criteria

This goal is complete only when:

- all 59 package entries carry an explicit compatibility policy;
- the resolver fails closed on absent or invalid policies;
- initial release is represented and reported explicitly;
- the normal release workflow packages through the compatibility preflight;
- a baseline-bearing release cannot reach archive inspection or publication
  without successful SDK binary validation;
- the workflow contains only one package-creation path;
- existing downstream release integrity gates remain intact and ordered;
- focused and full validation pass with recorded exact evidence;
- runtime source, public APIs, package versions, and public state are unchanged;
- goal, docs, memory index/current state, and the new memory record are updated;
  and
- local `main` is clean and synchronized after normal review and merge.

## Completion Evidence

### Implemented boundary

- All 59 entries in `eng/packages.json` now declare
  `binaryCompatibilityBaseline`; every current published package points to its
  current project version.
- `eng/resolve-package-release.ps1` exports the declared baseline and explicit
  initial-release decision and rejects a missing, empty, non-string, or invalid
  policy.
- `eng/package-binary-compat-preflight.ps1` is the single package-creation
  boundary used by the release workflow. A normal release restores the exact
  baseline into a fresh cache-disabled package root and passes that archive to
  SDK validation. An explicit initial release uses the same package boundary
  without baseline arguments.
- `.github/workflows/publish-nuget.yml` runs the compatibility gate after the
  controlled build, solution tests, and real-provider suites and before every
  archive, smoke, collision, publication, feed, and repository-release step.
- Documentation, documentation-site content, release tests, and memory records
  describe and enforce the same policy.

### Local verification

- Manifest/resolver/preflight preparation: all 59 package entries prepared
  successfully (`BINARY_COMPAT_PREPARED_COUNT=59`).
- Real public-baseline proof: `FluxFlow.Nodes` 4.0.0 restored from the public
  feed with `--no-cache` and an isolated package root, packed once, and reported
  `BINARY_COMPAT_OK=FluxFlow.Nodes`.
- Focused release suite: 151/151 tests passed with zero warnings. The test audit
  found no surviving high-risk requirement mutation.
- Complete solution Release build: 134 projects succeeded with zero warnings
  and zero errors under continuous-integration build settings.
- Complete solution Release tests: 2,519 tests passed across 66 test projects
  with zero warnings.
- Complete solution formatting verification passed without changes.
- Transitive vulnerable-package audit reported no vulnerable packages for any
  solution project.
- `git diff --check` passed, the changed-file scope contains no runtime source,
  package project, public API baseline, or changelog change, and the generated
  candidate package and symbol package were removed after validation.
- No package version, tag, release, or public package state was changed, and no
  package was published.

### Review and merge

- Implementation commit `6732d301d5ce86ff6d1b0602a33c6aeeeb91e465`
  was reviewed through pull request 73.
- The required `build-test` check completed successfully on that exact head.
- Repository policy rejected self-approval, as expected; no approval or branch
  protection was bypassed.
- The pull request merged normally as
  `cd24239f0f2835a6a1eba82774a3b4c7e4cc7450` on 2026-08-03.
- Local `main` was synchronized with `origin/main` and verified clean after the
  merge. This evidence-only closeout records completion without changing the
  implemented release boundary.
