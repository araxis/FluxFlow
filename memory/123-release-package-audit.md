# Release Package Audit

## 2026-06-03

Added a release-audit test project for package metadata drift.

Extended it with script-level checks for the release resolver and release-notes
extractor.

Extended it again with manifest coverage and failure-path checks for helper
scripts.

Added package project convention checks so releasable projects stay aligned on
target frameworks, package metadata, symbol settings, and package reference
shape.

Added a package consumer smoke harness that generates a throwaway console
consumer, restores from a package source, builds, runs, and loads package types
before release artifacts move forward.

## Decision

Stabilization work should include guardrails around release metadata, not only
runtime behavior. The package manifest is now checked against project metadata
and release notes during normal solution tests.

This keeps independent package releases safer as the package list grows and
reduces the chance of a tag resolving to the wrong project, a project shipping
without a packed readme, or a release missing its changelog heading.

The release helper scripts are also covered directly so automation behavior
does not drift away from the manifest contract.

## Scope

- Added `FluxFlow.Release.Tests`.
- Added `PackageManifestTests`.
- Verifies package manifest uniqueness for aliases, tag prefixes, package ids,
  and project paths.
- Verifies each manifest project path is relative and under `src/`.
- Verifies each project exists and has matching `PackageId`.
- Verifies each project has `Version`, `PackageReadmeFile`, and
  `PackageReleaseNotes`.
- Verifies every source package project with a `PackageId` is listed in the
  package manifest.
- Verifies the configured readme file is packed and exists on disk.
- Verifies `CHANGELOG.md` contains a heading for each manifest package using
  `notesName` and the project version.
- Added shared release-test helpers for repository root discovery, manifest
  loading, and script-host lookup.
- Added release helper script tests for alias resolution, tag-name resolution,
  environment-file output, and release-note extraction.
- Added release helper script failure-path tests for mismatched package/tag
  inputs, version mismatches, and missing release-note sections.
- Added package project convention tests for target frameworks, assembly names,
  root namespaces, authors, descriptions, tags, license expression, readme
  metadata, symbols metadata, release notes, and manifested project references.
- Added `eng/package-consumer-smoke.ps1`.
- Added release script tests that validate smoke project generation and missing
  package-file failure behavior.
- Wired the release workflow to run the consumer smoke harness after pack and
  before release artifact publication.

## Verification

- Focused release-audit tests passed.
- Local consumer smoke harness passed against an existing configuration package
  artifact.
- Full solution build passed.
- Full solution tests passed.

## Next

Continue package maturity work by adding narrow guardrails or consumer-driven
fixes without changing the stable engine boundary casually.
