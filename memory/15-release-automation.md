# Release Automation

Date: 2026-05-31

## Decision

Use one release workflow for GitHub Releases and NuGet deployment.

The workflow is `.github/workflows/publish-nuget.yml`.

## Triggers

- Pushing a tag like `v0.1.0-alpha.1`.
- Manual workflow dispatch with an explicit package version.

## Versioning

- Package versions use semantic versioning.
- Tags use `v` plus the package version.
- Prerelease versions contain a hyphen, for example `0.1.0-alpha.1`.
- The workflow resolves `PACKAGE_VERSION` and `RELEASE_TAG` from the tag or
  manual input.

## Release Notes

`eng/get-release-notes.ps1` extracts the matching version section from
`CHANGELOG.md` and writes `artifacts/release-notes.md`.

The GitHub Release uses those extracted notes.

## Workflow Steps

1. Restore.
2. Build.
3. Test.
4. Pack.
5. Extract release notes.
6. Upload workflow artifacts.
7. Create or update the GitHub Release.
8. Attach package files to the GitHub Release.
9. Publish `.nupkg` and `.snupkg` to NuGet.

GitHub Release creation runs before NuGet deployment so package assets are
still attached to the release if an external package feed credential is invalid
or expired.

Reruns update release notes, replace package assets, and retarget the release
metadata to the commit that triggered the workflow.

## Secrets

NuGet publish uses the repository secret `NUGET_API_KEY`.

Current status: the release workflow reached restore, build, test, pack,
artifact upload, GitHub Release asset upload, and NuGet deployment
successfully for `0.1.0-alpha.1`, `0.2.0-alpha.1`,
`0.3.0-alpha.1`, and `0.4.0-alpha.1`.

The project README shows package version and download badges from the public
package feed so the repository front page reflects the latest published
package state automatically.

The package publish step pushes `.nupkg` files only; the matching `.snupkg`
symbols package is pushed automatically by the package tooling.

Release `0.3.0-alpha.1` completed in run `26713377988`; branch CI for the
same commit completed in run `26713375042`.

Release `0.4.0-alpha.1` completed in run `26715368105`; branch CI for the
same commit completed in run `26715365405`.
