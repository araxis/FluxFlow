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

Added a package archive inspector that checks packed archive contents before the
consumer smoke harness runs.

Added a post-publish package feed verifier that restores, builds, runs, and
loads the exact package version from a configured package source using an
isolated package cache.

Added a local release dry-run script that resolves a package, optionally runs
solution restore/build/test, packs the selected project, inspects the package
archives, runs local consumer smoke, and runs local feed-style verification.

Added a guarded release tag helper that resolves the package, requires a clean
working tree, refuses existing tags, runs the local dry run, and creates the tag
only after the dry run passes.

Added an operator note that documents the local dry-run and guarded tag command
path for package releases.

Added a read-only package listing helper for checking package aliases, current
versions, release tags, package ids, and project paths before a release command.

## Decision

Stabilization work should include guardrails around release metadata, not only
runtime behavior. The package manifest is now checked against project metadata
and release notes during normal solution tests.

This keeps independent package releases safer as the package list grows and
reduces the chance of a tag resolving to the wrong project, a project shipping
without a packed readme, or a release missing its changelog heading.

The release helper scripts are also covered directly so automation behavior
does not drift away from the manifest contract.

Release operators should be able to inspect package aliases and versions without
opening the manifest and project files by hand.

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
- Added `eng/package-archive-inspect.ps1`.
- Added release script tests that validate expected archive contents and missing
  symbol entries.
- Wired the release workflow to inspect package archives after pack and before
  consumer smoke.
- Added `eng/package-feed-verify.ps1`.
- Added release script tests that validate generated feed verification projects,
  retry input validation, and local package source validation.
- Wired the release workflow to verify the package from the configured package
  source after publish.
- Added `eng/package-release-dry-run.ps1`.
- Added release script tests that validate dry-run package resolution, package
  source preparation, and invalid input rejection.
- Added `eng/package-release-tag.ps1`.
- Added release script tests that validate tag resolution, custom tag messages,
  and invalid remote rejection.
- Added `memory/124-release-operator-note.md`.
- Added a release test that keeps the operator note linked from the memory index
  and checks the guarded command examples.
- Added `eng/list-package-releases.ps1`.
- Added release script tests for full package listing, single-package filtering,
  and missing manifest rejection.
- Added `memory/125-release-package-list-helper.md`.

## Verification

- Focused release-audit tests passed.
- Local package archive inspection passed against an existing configuration
  package artifact.
- Local consumer smoke harness passed against an existing configuration package
  artifact.
- Local package feed verification passed against existing configuration package
  artifacts.
- Local release dry run passed for the configuration package using existing
  build outputs.
- Local release tag helper preparation passed for the configuration package.
- Operator note guard test passed.
- Package list helper tests passed.
- Full solution build passed.
- Full solution tests passed.

## Next

Continue package maturity work by adding narrow guardrails or consumer-driven
fixes without changing the stable engine boundary casually.
