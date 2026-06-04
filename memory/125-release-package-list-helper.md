# Release Package List Helper

## 2026-06-04

Added a read-only package listing helper:

```powershell
./eng/list-package-releases.ps1
```

It prints each manifest alias with the current project version, release tag,
package id, and project path.

To inspect one package:

```powershell
./eng/list-package-releases.ps1 -Package components-configuration
```

To return structured output:

```powershell
./eng/list-package-releases.ps1 -AsJson
```

## Decision

Operators should not need to open the package manifest or project files by hand
to find the correct alias and current version before a dry run or guarded tag
command.

The helper is intentionally read-only. It does not pack, tag, publish, or mutate
the working tree.

## Verification

- Added script tests for full listing, single-package filtering, and missing
  manifest rejection.
- Focused release tests passed.
