# Sessions Clock Hardening

Date: 2026-06-02

`FluxFlow.Components.Sessions` now has a host-provided clock boundary.

## Decision

Add a minimal `ISessionClock` contract to the Sessions package and expose it
through `SessionsComponentOptions.UseClock(...)`.

The default remains `SystemSessionClock`, so existing hosts keep the same
runtime behavior. Tests and hosts that need deterministic recording or replay
timing can provide their own clock.

## Scope

- `session.recorder` uses the configured clock for session start timestamps.
- `session.recorder` uses the configured clock for default message timestamps
  when `SessionRecordInput.Timestamp` is not set.
- `session.recorder` uses the configured clock for session end timestamps.
- `session.replay` uses the configured clock for fixed interval, real-time, and
  multiplier replay delays.
- Node ports and session contracts stay unchanged.

## Package

- Package: `FluxFlow.Components.Sessions`
- Version: `0.2.0-alpha.1`
- Public additions:
  - `ISessionClock`
  - `SystemSessionClock`
  - `SessionsComponentOptions.UseClock(...)`

## Verification

- Focused Sessions tests passed: 13 tests.
- Full solution build passed in Release with 0 warnings.
- Full solution tests passed in Release.
- Package pack passed and produced
  `FluxFlow.Components.Sessions.0.2.0-alpha.1.nupkg`.
- Release commit: `5c19701`.
- Release tag: `components-sessions-v0.2.0-alpha.1`.
- Release workflow run: `26833142464`, success.
- Main verification run: `26833129238`, success.
- Fresh public-feed restore/build smoke passed on attempt 4.
