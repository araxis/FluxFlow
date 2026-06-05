# Package Versioning

Use semantic versions for published packages.

Current stable engine and component release line:

```text
FluxFlow.Engine              1.0.0
FluxFlow.Components.*        1.0.0
```

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

The release workflow reads the selected project version and refuses to publish
when the requested version does not match the project file. This keeps package
versions reviewable in source.

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

## Component Packages

Component packages move independently from the engine package. Keep their
dependency range narrow at first, then loosen it only after real consumers prove
compatibility.

When an engine prerelease moves public node-authoring types, republish every
published component package that references the engine, even if the component
behavior did not change. Use a patch prerelease bump and describe it as an
engine compatibility rebuild.

Current stable pattern:

```text
FluxFlow.Engine              1.0.0
FluxFlow.Components.Example  1.0.0
```

Do not bump the engine version when only a component package changes.

## Keep Releases Small

Prefer small releases that prove one public change at a time:

- one engine feature
- one component package template
- one component family
- one migration polish pass

Small releases make package rollback and consumer migration much easier.
