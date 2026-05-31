# Package Versioning Docs

Date: 2026-05-31

## Summary

Added `docs/11-package-versioning.md` as a short public reference for package
version rules, changelog requirements, release checks, and package versioning.

## Decisions

- Keep the public page compact.
- Describe the project file version as the package version source.
- Release each package independently.
- Do not bump the engine package when only a component package changes.
- Keep external release service details out of the public page.

## Verification Target

The page should stay aligned with:

- `src/FluxFlow.Engine/FluxFlow.Engine.csproj`
- `CHANGELOG.md`
- release workflow
- `eng/get-release-notes.ps1`

## Next Step

Update the public page to describe independent component package versions before
the first component package is published.
