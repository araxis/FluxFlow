# Release Automation

Date: 2026-05-31

## Decision

Use one release workflow that selects one package per run.

The workflow is `.github/workflows/publish-nuget.yml`.

## Triggers

- Pushing a package-scoped tag like `engine-v0.5.0-alpha.1` or
  `components-mqtt-v0.1.0-alpha.1`.
- Manual workflow dispatch with an explicit package alias and optional version.

## Versioning

- Each packable project owns its own version.
- Package versions use semantic versioning.
- Tags use the package tag prefix, `-v`, and the package version.
- Prerelease versions contain a hyphen, for example `0.1.0-alpha.1`.
- The workflow resolves `PACKAGE_PROJECT`, `PACKAGE_ID`, `PACKAGE_VERSION`, and
  `RELEASE_TAG` from `eng/packages.json`, the tag, and manual input.
- A tag or manual version must match the selected project version.
- Adding a project can change the solution and package manifest without forcing
  existing packages to be republished.

## Release Notes

`eng/get-release-notes.ps1` extracts the matching package/version section from
`CHANGELOG.md` and writes `artifacts/release-notes.md`.

The release record uses those extracted notes.

## Workflow Steps

1. Restore.
2. Build.
3. Test.
4. Pack the selected package project.
5. Extract release notes.
6. Upload workflow artifacts.
7. Create or update the release record.
8. Attach package files to the release.
9. Publish the selected `.nupkg`.

Release record creation runs before package deployment so package assets are
still attached to the release if the package feed credential is invalid or
expired.

Reruns update release notes, replace package assets, and retarget the release
metadata to the commit that triggered the workflow.

## Package Manifest

`eng/packages.json` maps package aliases and tag prefixes to project files. The
current entries are:

- `engine` -> `FluxFlow.Engine`
- `components-mqtt` -> `FluxFlow.Components.Mqtt`

Future component packages should add one manifest entry. The release workflow
does not need a new pack loop for each component family.

## Secrets

Package deployment uses the configured repository secret.

Current status: the earlier single-package release workflow reached restore,
build, test, pack, artifact upload, release asset upload, and package
deployment successfully for `0.1.0-alpha.1`, `0.2.0-alpha.1`,
`0.3.0-alpha.1`, `0.4.0-alpha.1`, and `0.5.0-alpha.1`.

The project README shows package version and download badges from the public
package feed so the repository front page reflects the latest published
package state automatically.

The package deployment step pushes `.nupkg` files only; the matching `.snupkg`
symbols package is pushed automatically by the package tooling.

Release `0.3.0-alpha.1` completed in run `26713377988`; branch CI for the
same commit completed in run `26713375042`.

Release `0.4.0-alpha.1` completed in run `26715368105`; branch CI for the
same commit completed in run `26715365405`.

Release `0.5.0-alpha.1` completed in run `26718498700`; branch CI for the
same commit completed in run `26718474621`.
