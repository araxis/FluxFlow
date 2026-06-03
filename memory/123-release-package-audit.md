# Release Package Audit

## 2026-06-03

Added a release-audit test project for package metadata drift.

## Decision

Stabilization work should include guardrails around release metadata, not only
runtime behavior. The package manifest is now checked against project metadata
and release notes during normal solution tests.

This keeps independent package releases safer as the package list grows and
reduces the chance of a tag resolving to the wrong project, a project shipping
without a packed readme, or a release missing its changelog heading.

## Scope

- Added `FluxFlow.Release.Tests`.
- Added `PackageManifestTests`.
- Verifies package manifest uniqueness for aliases, tag prefixes, package ids,
  and project paths.
- Verifies each manifest project path is relative and under `src/`.
- Verifies each project exists and has matching `PackageId`.
- Verifies each project has `Version`, `PackageReadmeFile`, and
  `PackageReleaseNotes`.
- Verifies the configured readme file is packed and exists on disk.
- Verifies `CHANGELOG.md` contains a heading for each manifest package using
  `notesName` and the project version.

## Verification

- Focused release-audit test passed.
- Full solution build passed.
- Full solution tests passed.

## Next

Continue package maturity work by adding narrow guardrails or consumer-driven
fixes without changing the stable engine boundary casually.
