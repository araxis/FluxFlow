# Designer Metadata Hint Release Readiness

Date: 2026-07-01

## Summary

Ran the local release-readiness pass for the completed Designer metadata hint
train. Broad verification and release preflight metadata checks are green, and
the Designer package dry-run is green. Composition package dry-runs are blocked
by unpublished current dependency packages that are not present in the isolated
temp package source.

## Package Set

- `components-designer` `2.16.0` / `components-designer-v2.16.0`.
- Composition metadata hint packages:
  - `components-mapping-composition`, `components-control-composition`,
    `components-assertions-composition`, `components-state-composition`,
    `components-observability-composition`, `components-validation-composition`,
    `components-routing-composition`, `components-serialization-composition`,
    `components-payloads-composition`, `components-projections-composition`,
    `components-metrics-composition`, `components-expectations-composition`,
    and `components-http-composition` at `1.3.0`.
  - `components-sources-composition`, `components-filesystem-composition`,
    `components-storage-composition`, and `components-mqtt-composition` at
    `1.4.0`.
  - `components-timers-composition` and `components-sessions-composition` at
    `1.5.0`.

## Verification

- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  - Passed: 85
- `dotnet test tests\FluxFlow.Components.Designer.Tests\FluxFlow.Components.Designer.Tests.csproj --no-restore -v minimal`
  - Passed: 93
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  - Passed with 0 warnings and 0 errors.
- `dotnet build FluxFlow.sln --configuration Release --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  - Passed with 0 warnings and 0 errors after the first fast dry-run showed
    Release pack artifacts were required.
- `eng/list-package-releases.ps1`
  - Confirmed impacted aliases, versions, and release tags.
- `eng/package-release-preflight.ps1`
  - Passed for all 20 impacted aliases; changelog sections resolved.
- `eng/package-release-dry-run.ps1 -Package components-designer -Version 2.16.0 -SkipSolutionBuild`
  - Passed using a temp package source.

## Finding

- `components-mapping-composition` `1.3.0` packed successfully, but its
  consumer smoke restore failed because the temp package source did not contain
  current unpublished dependencies:
  - `FluxFlow.Components.Mapping` `3.0.1`
  - `FluxFlow.Mapping` `1.0.2`
  - `FluxFlow.Composition` `1.0.9`
  - `FluxFlow.Composition.Hosting` `1.0.5`
- The local `artifacts/packages` folder only has older versions for those
  dependencies, and public NuGet also does not have every current dependency
  version needed by the branch. This is a release sequencing/source-seeding
  issue, not a metadata implementation issue.

## Next

Plan a separate release-dependency readiness pass before publishing the
composition metadata packages. That pass should decide whether to seed a temp
source with current dependency packages, release dependency packages first, or
adjust the release dry-run workflow to make train-level package verification
explicit.
