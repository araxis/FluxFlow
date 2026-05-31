# Package Versioning

Use semantic versions for published packages.

Current prereleases use this shape:

```text
0.5.0-alpha.1
```

Release tags use `v` plus the package version:

```text
v0.5.0-alpha.1
```

## Source Of Truth

The project file keeps a default `<Version>` so local pack commands produce a
valid prerelease package.

The release workflow sets the published package version with
`PackageVersion`. That means a release can be produced from the same committed
source after selecting the exact version.

## Changelog

Every published version needs a matching `CHANGELOG.md` section:

```md
## 0.6.0-alpha.1

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
2. Confirm the project file default version matches the next local prerelease
   target or is intentionally behind the release override.
3. Run build and tests locally.
4. Run the sample app when docs, JSON, links, lifecycle, or package authoring
   behavior changed.
5. Create the release from a clean commit.
6. Verify the package can be restored from the public package feed.

## Component Packages

Component packages should start on the same prerelease train as the engine while
the extension surface is still settling. Keep their dependency range narrow at
first, then loosen it only after real consumers prove compatibility.

Recommended early pattern:

```text
FluxFlow.Engine              0.6.0-alpha.1
FluxFlow.Components.Example  0.6.0-alpha.1
```

Once the engine reaches a stable 1.0 line, component packages can move at their
own pace as long as their supported engine range is tested.

## Keep Releases Small

Prefer small prereleases that prove one public change at a time:

- one engine feature
- one component package template
- one component family
- one migration polish pass

Small releases make package rollback and consumer migration much easier.
