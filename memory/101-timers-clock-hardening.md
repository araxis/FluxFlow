# Timers Clock Hardening

Date: 2026-06-02

`FluxFlow.Components.Timers` now has a host-provided clock boundary.

## Decision

Add a minimal `ITimerClock` contract to the Timers package and expose it through
`TimerComponentOptions.UseClock(...)`.

The default remains `SystemTimerClock`, so existing hosts keep normal runtime
behavior. Tests and hosts that need deterministic timing can provide their own
clock.

## Scope

- `timer.interval` uses the configured clock for start time, due-time delays,
  tick timestamps, elapsed time, and drift.
- `timer.schedule` uses the configured clock for schedule lookup, due-time
  delays, tick timestamps, and drift.
- `timer.delay` uses the configured clock for per-message delays.
- `timer.throttle` uses the configured clock for throttle windows and delays.
- `timer.debounce` uses the configured clock for quiet-period delays.
- `timer.interval` and `timer.schedule` emit started diagnostics before their
  background work begins.
- Node ports and timer contracts stay unchanged.

## Package

- Package: `FluxFlow.Components.Timers`
- Version: `0.5.0-alpha.1`
- Public additions:
  - `ITimerClock`
  - `SystemTimerClock`
  - `TimerComponentOptions.UseClock(...)`

## Verification

- Focused Timers tests passed: 62 tests.
- Full solution build passed in Release with 0 warnings.
- Full solution tests passed in Release.
- Package pack passed and produced
  `FluxFlow.Components.Timers.0.5.0-alpha.1.nupkg`.
- Release commit: `4fc5f7e`.
- Release tag: `components-timers-v0.5.0-alpha.1`.
- Release workflow run: `26835522422`, success.
- Main verification run: `26835512082`, success.
- Fresh public-feed restore/build smoke passed on attempt 6.
