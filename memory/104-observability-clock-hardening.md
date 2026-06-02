# Observability Clock Hardening

Date: 2026-06-02

## Decision

Harden `FluxFlow.Components.Observability` with a host-provided clock for
observer timestamps and rate calculations.

Observer output is often used by dashboards, tests, logs, and runtime status
views. The package should keep the default system behavior for existing callers
while letting hosts and tests provide deterministic timestamps.

## Package Shape

- Package: `FluxFlow.Components.Observability`
- Version: `0.3.0-alpha.1`
- Clock contract: `IObservabilityClock`
- Default clock: `SystemObservabilityClock`
- Registration:
  `RegisterObservabilityComponents(options => options.UseClock(clock))`

## Behavior

- `flow.logger` uses the configured clock for `FlowLogEntry.Timestamp`.
- `flow.counter` uses the configured clock for `FlowCounterSnapshot.Timestamp`
  and `LastObservedAt`.
- `flow.metrics` uses the configured clock for `FlowMetricSnapshot.Timestamp`,
  `LastObservedAt`, current rate, and average rate.
- Existing `RegisterObservabilityComponents()` callers keep the default system
  clock.

## Verification

Completed local verification:

- `dotnet test tests\FluxFlow.Components.Observability.Tests\FluxFlow.Components.Observability.Tests.csproj -c Release --no-restore`
  passed with 15 tests.
- `dotnet build FluxFlow.sln -c Release --no-restore` passed with 0 warnings.
- `dotnet test FluxFlow.sln -c Release --no-restore` passed.
- `dotnet pack src\FluxFlow.Components.Observability\FluxFlow.Components.Observability.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`
  created the package and symbol package.
- Commit: `a591f80` (`Add deterministic observability clock`).
- Tag: `components-observability-v0.3.0-alpha.1`.
- Release workflow: `26838896223`, success.
- Main CI workflow: `26838884366`, success.
- Public package restore/build smoke passed on attempt 9 after public-feed
  indexing caught up.
