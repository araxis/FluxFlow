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

## Planned Verification

- Focused Sessions tests.
- Full solution build and test pass.
- Package pack and public package smoke after release.
