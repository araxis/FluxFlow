# Package Versioning

Use semantic versions for published packages.

Packages in this repository move independently. Do not infer a package version
from a family name such as `FluxFlow.Components.*` or from another package's
release line.

Prereleases use this shape:

```text
0.6.0-beta.1
```

Release tags use the package tag prefix, `-v`, and the package version:

```text
engine-v1.0.0
components-mqtt-v1.0.0
```

## Source Of Truth

Each packable project owns its own `<Version>`.

`eng/packages.json` lists the shipped packages, their release aliases, tag
prefixes, project paths, and changelog names. The release workflow reads the
selected project version and refuses to publish when the requested version does
not match the project file. This keeps package versions reviewable in source.

## Changelog

Every published package version needs a matching `CHANGELOG.md` section:

```md
## FluxFlow.Components.Mqtt 0.1.0-alpha.1

Short release summary.

- Change one.
- Change two.
```

The release workflow extracts the matching changelog section and uses it as the
release notes.

## Version Rules

While the package is pre-1.0:

- bump the minor number for meaningful public API or behavior changes
- bump the patch number for small fixes that keep the same public shape
- use prerelease suffixes for early package validation
- keep package notes short and tied to user-visible changes

After 1.0:

- major: breaking public API or persisted-definition changes
- minor: additive public API or behavior
- patch: compatible fixes

## Public API Baseline

Release tests include a lightweight public API baseline for source declarations
across the packages listed in `eng/packages.json`. The baseline is stored under
`eng/public-api` and records a declaration count plus a normalized declaration
hash for each package in manifest order.

When the baseline changes, review the source diff before accepting it:

- breaking public API changes require a major version after `1.0`
- additive public API changes require a minor version after `1.0`
- compatible fixes that keep the same public shape can stay patch-level
- documentation-only changes should not update the baseline

Accept an intentional baseline update by setting
`FLUXFLOW_ACCEPT_PUBLIC_API_BASELINE=1` and rerunning
`PublicApiBaselineTests`. Do this only after package version, changelog, and docs
changes are correct for the public API change.

## Binary Compatibility Preflight

The source-declaration baseline is not a binary compatibility tool. For package
release readiness, run `eng/package-binary-compat-preflight.ps1` after a
controlled Release build. The helper resolves the package through
`eng/packages.json` and uses .NET SDK package validation during `dotnet pack` to
compare the current package assembly against a published baseline package
version.

Use the current project version as the baseline version for same-version
release-readiness checks. Use an explicit older baseline only when validating a
new package version against the previous published stable package.

## Release Checklist

Before publishing:

1. Update `CHANGELOG.md`.
2. Confirm the selected project file version matches the intended package
   version.
3. Require the intended package version to be absent from the public feed.
4. Run build and tests locally.
5. Run the sample app when docs, JSON, links, lifecycle, or package authoring
   behavior changed.
6. Run package binary compatibility preflight when the release must be checked
   against a published baseline.
7. Create the release from a clean commit.
8. Verify the package can be restored from the public package feed.

Check one intended version without publishing it:

```powershell
./eng/package-release-availability.ps1 `
  -Package nodes `
  -ExpectedState Missing
```

The availability helper resolves the id and version from the manifest and
project, follows the package source's V3 flat-container resource, and fails on
an invalid response or unexpected state. A network or protocol error is never
treated as evidence that a version is missing.

## Coordinated Release Trains

`eng/packages.json` is inventory order, not an implicit publication order. For
a coordinated change, calculate dependency waves from the package projects'
explicit `ProjectReference` relationships:

```powershell
./eng/package-release-plan.ps1
```

If an exact prerequisite version is already published and intentionally reused,
name its manifest alias explicitly:

```powershell
./eng/package-release-plan.ps1 -AlreadyAvailable mapping
```

The planner performs no network or Git mutation. It rejects unknown aliases,
missing package projects, unlisted referenced package projects, and dependency
cycles. Publish a dependent wave only after every prerequisite wave has passed
the release workflow and public-feed verification.

Never use duplicate skipping to distinguish a reusable prerequisite from a
collision. Audit the exact existing version, record why it is reusable, and
exclude it from the new publication targets. Any unexpected existing version
stops the train.

### Deterministic Release verification

Build the configuration that the test gate will execute, then keep the test
pass restore-free and build-free:

```sh
dotnet build FluxFlow.sln --configuration Release --no-restore --no-incremental --maxcpucount:1
dotnet test FluxFlow.sln --configuration Release --no-restore --no-build --maxcpucount:4
```

The Release verification project executes its sample smoke tests from the
matching prebuilt configuration. It never restores or builds a sample inside a
test, so missing or stale preparation fails visibly at the release boundary.
Test-owned release scripts and sample processes have explicit time limits,
drain standard output and error concurrently, and terminate their owned process
tree on timeout or cancellation.

## Independent Packages

Runtime, composition, component, support, adapter, and metadata packages move
independently. Keep dependency ranges narrow at first, then loosen them only
after real consumers prove compatibility.

Do not bump the engine version when only a component, composition adapter,
support package, or storage/client adapter changes. Do not bump a component
package when only its optional composition adapter changes. When a shared
contract package changes, republish only the packages that consume the changed
contract and need a new package artifact.

## Keep Releases Small

Prefer small releases that prove one public change at a time:

- one engine feature
- one component package template
- one component family
- one migration polish pass

Small releases make package rollback and consumer migration much easier.

When a large accumulated breaking change genuinely requires several packages,
keep each package release independent and use explicit dependency waves. Stop
at the first failed alias; already published versions remain immutable, while
unstarted dependent waves remain untouched.
