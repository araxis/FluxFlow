# Public API Baseline

This directory holds the lightweight source-declaration baseline used by release
tests.

The baseline is intentionally simple:

- it scans package source files listed in `eng/packages.json`
- it records the number of public or protected source declarations per package
- it records a hash of the normalized declaration text
- entries follow manifest order and do not duplicate package names

This is not a full binary compatibility tool. It is a review checkpoint that
catches accidental public declaration drift and forces intentional versioning,
changelog, and documentation decisions.

## Updating

When a public declaration change is intentional:

1. Decide whether the package change is breaking, additive, or patch-compatible.
2. Update the package version and changelog if the package artifact changes.
3. Update docs or README guidance when behavior or usage changes.
4. Accept the new baseline:

```powershell
$env:FLUXFLOW_ACCEPT_PUBLIC_API_BASELINE='1'
dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --filter FullyQualifiedName~PublicApiBaselineTests
Remove-Item Env:\FLUXFLOW_ACCEPT_PUBLIC_API_BASELINE
```

5. Review the baseline diff together with the source diff before committing.
