# Projections Designer Metadata Hints

Date: 2026-06-30

## Summary

Completed the Projections composition Designer metadata hint pass.
`event.projection` metadata now carries richer option hints plus a host-owned
clock resource key pattern. The change is descriptive metadata only.

## Changes

- Added option section, importance, and editor hints to
  `ProjectionsComponentDesignMetadataProvider`:
  - Diagnostics hint for `name`.
  - Filtering hint for `filter`.
  - Rate hint for `rateWindowSeconds`.
  - Emission hints for `emitEveryMatch` and `emitFinalSnapshot`.
  - Preview hint for `maxPreviewChars`.
  - Runtime hint for `boundedCapacity`.
  - Boolean emission options omit editor hints because Designer has no boolean
    editor attribute value.
- Added the host-owned `clock` resource key pattern `clock:{name}` while
  preserving the existing clock picker kind and optional resource shape.
- Preserved event filtering, snapshot folding, rolling-rate behavior, final
  snapshot lifecycle behavior, ports, clock use, configuration binding, runtime
  behavior, resource ownership, renderer behavior, hot reload behavior, and
  engine dependency boundaries.
- Bumped `FluxFlow.Components.Projections.Composition` from `1.2.1` to
  `1.3.0`.
- Updated the package README, package release notes, top-level changelog, and
  focused metadata tests.

## Verification

- `dotnet test tests\FluxFlow.Components.Projections.Composition.Tests\FluxFlow.Components.Projections.Composition.Tests.csproj --no-restore -v minimal`
  - Passed: 11
- `dotnet test tests\FluxFlow.Components.Designer.Tests\FluxFlow.Components.Designer.Tests.csproj --no-restore -v minimal`
  - Passed: 93
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  - Passed: 84
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  - Passed with 0 warnings and 0 errors.
- Local graph output was refreshed with `graphify update . --force` during
  closeout and remains excluded from git.

## Next

Keep any further package-family Designer metadata hint pass separately planned,
locally scoped, and locally committed.
