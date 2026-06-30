# Sources Designer Metadata Hints

Date: 2026-06-30

## Summary

Completed the Sources composition Designer metadata hint pass. Generated Source
and Sequence Source metadata now carry richer option hints plus a host-owned
clock resource key pattern. The change is descriptive metadata only.

## Changes

- Added option section, importance, and editor hints to
  `SourcesComponentDesignMetadataProvider`:
  - Generated Source uses Diagnostics, Type Metadata, Items, Emission, Timing,
    and Runtime sections.
  - Sequence Source uses Diagnostics, Sequence, Timing, and Runtime sections.
  - Boolean `loop` omits an editor hint because Designer has no boolean editor
    attribute value.
- Added the host-owned `clock` resource key pattern `clock:{name}` while
  preserving the existing clock picker kind and optional resource shape.
- Preserved existing ports, option kinds, defaults, minimum values, generated
  item binding, sequence generation, clock use, source lifecycle, runtime
  behavior, resource ownership, renderer behavior, hot reload behavior, and
  engine dependency boundaries.
- Bumped `FluxFlow.Components.Sources.Composition` from `1.3.2` to `1.4.0`.
- Updated the package README, package release notes, top-level changelog, and
  focused metadata tests.

## Verification

- `dotnet test tests\FluxFlow.Components.Sources.Composition.Tests\FluxFlow.Components.Sources.Composition.Tests.csproj --no-restore -v minimal`
  - Passed: 22
- `dotnet test tests\FluxFlow.Components.Designer.Tests\FluxFlow.Components.Designer.Tests.csproj --no-restore -v minimal`
  - Passed: 93
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  - Passed: 84
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  - Passed with 0 warnings and 0 errors after `dotnet build-server shutdown`
    and rerun. No FluxFlow-owned build parent processes remained after the
    initial timed-out attempt.
- Local graph output was refreshed with `graphify update . --force` during
  closeout and remains excluded from git.

## Next

Keep any further package-family Designer metadata hint pass separately planned,
locally scoped, and locally committed.
