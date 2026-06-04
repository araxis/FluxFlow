# Release Preflight Helper

## 2026-06-04

Added a read-only release preflight helper:

```powershell
./eng/package-release-preflight.ps1 -Package components-configuration
```

It resolves the package alias, current version, release tag, package id, and
project path. It also prints the exact dry-run and guarded tag commands with the
resolved version included.

## Decision

Before running a dry run or creating a guarded tag, operators should have one
command that shows the selected package and the next commands exactly as they
should be executed.

The helper is intentionally read-only. It delegates package resolution to the
existing resolver and does not pack, tag, publish, or mutate the working tree.

## Verification

- Added script tests for command output and version mismatch handling.
- Focused release tests passed.
