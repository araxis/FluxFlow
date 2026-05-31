# Package Versioning Docs

Date: 2026-05-31

## Summary

Added `docs/11-package-versioning.md` as a short public reference for package
version rules, changelog requirements, release checks, and early component
package version alignment.

## Decisions

- Keep the public page compact.
- Describe the project file version as a local default.
- Describe the release workflow version as the published package version.
- Recommend matching engine and component package prerelease versions until the
  extension surface is stable.
- Keep external release service details out of the public page.

## Verification Target

The page should stay aligned with:

- `src/FluxFlow.Engine/FluxFlow.Engine.csproj`
- `CHANGELOG.md`
- release workflow
- `eng/get-release-notes.ps1`

## Next Step

Use the package versioning guidance when creating the first component package
template and any prerelease that ships it.
