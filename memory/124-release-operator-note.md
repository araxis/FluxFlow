# Package Release Operator Note

Date: 2026-06-04

This note records the package release command path for one package at a time.
Use package aliases from `eng/packages.json`.

## Local Dry Run

Run this first when checking a package locally:

```powershell
./eng/package-release-dry-run.ps1 -Package components-configuration
```

For a faster pass after a recent solution build:

```powershell
./eng/package-release-dry-run.ps1 -Package components-configuration -SkipSolutionBuild
```

The dry run resolves the package, packs it, inspects package archives, runs a
local consumer smoke check, and runs a local feed-style verification against the
newly packed artifacts.

## Guarded Tag Creation

Create the release tag only through the guarded helper:

```powershell
./eng/package-release-tag.ps1 -Package components-configuration
```

The helper resolves the package, checks that the working tree is clean, refuses
an existing tag, runs the local dry run, and only then creates the local tag.

To create and push the tag in one guarded pass:

```powershell
./eng/package-release-tag.ps1 -Package components-configuration -Push
```

## Specific Version

When a version is supplied, it must match the selected project version:

```powershell
./eng/package-release-tag.ps1 -Package components-configuration -Version 0.1.0-alpha.1
```

## Safety Rules

- Do not create release tags directly.
- Do not bypass the local dry run.
- Keep the working tree clean before tag creation.
- Use one package alias per release command.
- If the dry run fails, fix the package or release metadata before trying again.
