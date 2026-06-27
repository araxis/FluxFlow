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

## Release Checklist

Before publishing:

1. Update `CHANGELOG.md`.
2. Confirm the selected project file version matches the intended package
   version.
3. Run build and tests locally.
4. Run the sample app when docs, JSON, links, lifecycle, or package authoring
   behavior changed.
5. Create the release from a clean commit.
6. Verify the package can be restored from the public package feed.

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
