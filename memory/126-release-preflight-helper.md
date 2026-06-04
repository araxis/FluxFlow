# Release Preflight Helper

## 2026-06-04

Added a read-only release preflight helper:

```powershell
./eng/package-release-preflight.ps1 -Package components-configuration
```

It resolves the package alias, current version, release tag, package id, project
path, and changelog name. It verifies the selected package has a changelog
section for the resolved version before printing the exact dry-run and guarded
tag commands.

## Decision

Before running a dry run or creating a guarded tag, operators should have one
command that shows the selected package and the next commands exactly as they
should be executed.

The helper is intentionally read-only. It delegates package resolution to the
existing resolver and changelog extractor. It does not pack, tag, publish, or
mutate the working tree.

## Verification

- Added script tests for command output, version mismatch handling, and missing
  changelog section handling.
- Focused release tests passed.
