# Sources Clock Hardening

Date: 2026-06-02

`FluxFlow.Components.Sources` now has a host-provided clock boundary.

## Decision

Add a minimal `ISourceClock` contract to the Sources package and expose it
through `SourcesComponentOptions.UseClock(...)`.

The default remains `SystemSourceClock`, so existing hosts keep the same
runtime behavior. Tests and hosts that need deterministic source timing can
provide their own clock.

## Scope

- `source.generated` uses the configured clock for initial and interval delays.
- `source.sequence` uses the configured clock for initial and interval delays.
- `source.sequence` uses the configured clock for item timestamps.
- The node port shape stays unchanged.
- Generic replay remains in the sessions package unless a separate neutral
  replay source shape is proven.

## Package

- Package: `FluxFlow.Components.Sources`
- Version: `0.2.0-alpha.1`
- Public additions:
  - `ISourceClock`
  - `SystemSourceClock`
  - `SourcesComponentOptions.UseClock(...)`

## Verification

Completed verification:

- `dotnet test tests\FluxFlow.Components.Sources.Tests\FluxFlow.Components.Sources.Tests.csproj -c Release --no-restore`
  passed with 19 tests.
- `dotnet build FluxFlow.sln -c Release --no-restore` passed with 0 warnings.
- `dotnet test FluxFlow.sln -c Release --no-restore` passed.
- `dotnet pack src\FluxFlow.Components.Sources\FluxFlow.Components.Sources.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`
  created the package and symbol package.
- Commit: `a875ddc` (`Add deterministic source clocks`).
- Tag: `components-sources-v0.2.0-alpha.1`.
- Release workflow: `26832008311`, success.
- Branch CI workflow: `26831999212`, success.
- Public package restore/build smoke passed on attempt 11.
