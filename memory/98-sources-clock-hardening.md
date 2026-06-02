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

## Planned Verification

- Focused Sources tests.
- Full solution build and test pass.
- Package pack and public package smoke after release.
