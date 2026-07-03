# Package Binary Compatibility Readiness

Date: 2026-07-02

## Summary

Added a release-tooling helper for package binary compatibility readiness:
`eng/package-binary-compat-preflight.ps1`.

The helper:

- resolves package aliases through `eng/packages.json` and project versions
  through the existing release resolver.
- requires the requested `-Version` to match the project `<Version>`.
- defaults `-BaselineVersion` to the requested version.
- seeds the baseline package into the NuGet global package cache through a
  temporary restore outside the repo.
- runs `dotnet pack --configuration Release --no-build` with .NET SDK package
  validation enabled and baseline package name/version supplied through MSBuild
  properties.
- supports an optional local or URL `-PackageSource` for future local-source
  baseline checks while keeping the public feed available for dependencies.

Release-script tests now cover prepare-only command construction, version
mismatch rejection, package-source validation, missing Release build output, and
the success marker after a pack command.

## Verification

- `dotnet build FluxFlow.sln --configuration Release --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  passed before package validation checks.
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  passed after adding the helper tests: 91 passed, 0 failed, 0 skipped.
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  passed after the helper and documentation changes: 0 warnings, 0 errors.
- `eng/list-package-releases.ps1` enumerated 55 manifest packages.
- `eng/package-binary-compat-preflight.ps1 -Package components-designer -Version 2.16.0`
  restored the published baseline package, packed with SDK package validation,
  and printed `BINARY_COMPAT_OK=FluxFlow.Components.Designer`.

## Blocker

The all-package same-version readiness loop was intentionally stopped after the
first missing published baseline package:

- `eng/package-binary-compat-preflight.ps1 -Package components-configuration -Version 1.5.0`
  failed during baseline restore because `FluxFlow.Components.Configuration`
  `1.5.0` is not visible on the public package feed; NuGet reported nearest
  version `1.0.0`.

This is a package-feed/version availability blocker, not a source/API
implementation change. A separate pass should either publish the missing current
support package versions first or run binary compatibility against explicit
older `-BaselineVersion` values where that is the intended policy.

## Boundaries

No package source APIs, runtime behavior, package versions, release notes,
changelog entries, public API baselines, tags, publishing workflow, or package
source files were changed.
