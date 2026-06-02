# Routing Clock Hardening

Date: 2026-06-02

## Decision

Harden `FluxFlow.Components.Routing` with a host-provided clock for route
timestamps and timeout delays.

Routing already owns several time-sensitive behaviors: switch and merge output
timestamps, count/time windows, join timeouts, and correlation timestamps. The
package should keep the default system behavior for existing callers while
letting hosts and tests supply deterministic time.

## Package Shape

- Package: `FluxFlow.Components.Routing`
- Version: `0.9.0-alpha.1`
- Clock contract: `IRoutingClock`
- Default clock: `SystemRoutingClock`
- Registration: `RegisterRoutingComponents(options => options.UseClock(clock))`

## Behavior

- `flow.switch` uses the configured clock for switch result and route-envelope
  timestamps.
- `flow.merge` uses the configured clock for merged item timestamps.
- `flow.window` uses the configured clock for window start, emitted time, and
  timer delay.
- `flow.join` uses the configured clock for pending item timestamps, join
  elapsed time, timeout timestamps, and timeout delay.
- `flow.correlation` uses the configured clock for request/response timestamps,
  match elapsed time, and completion timeouts.
- Existing `RegisterRoutingComponents()` callers keep the default system clock.

## Verification

Completed local verification:

- `dotnet test tests\FluxFlow.Components.Routing.Tests\FluxFlow.Components.Routing.Tests.csproj -c Release --no-restore`
  passed with 78 tests.
- `dotnet build FluxFlow.sln -c Release --no-restore` passed with 0 warnings.
- `dotnet test FluxFlow.sln -c Release --no-restore` passed.
- `dotnet pack src\FluxFlow.Components.Routing\FluxFlow.Components.Routing.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`
  created the package and symbol package.
