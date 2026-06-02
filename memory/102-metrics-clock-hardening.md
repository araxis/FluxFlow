# Metrics Clock Hardening

Date: 2026-06-02

## Decision

Harden `FluxFlow.Components.Metrics` with a host-provided clock for fallback
sample timestamps.

Metric samples can already carry an explicit `Timestamp`, and those timestamps
remain authoritative. The package should not call the system clock directly
when a sample omits `Timestamp`; hosts and tests need a deterministic fallback.

## Package Shape

- Package: `FluxFlow.Components.Metrics`
- Version: `0.2.0-alpha.1`
- Clock contract: `IMetricsClock`
- Default clock: `SystemMetricsClock`
- Registration: `RegisterMetricsComponents(options => options.UseClock(clock))`

## Behavior

- `metrics.aggregate` uses `MetricSampleInput.Timestamp` when present.
- When `Timestamp` is missing, `metrics.aggregate` uses the configured
  `IMetricsClock.UtcNow`.
- The fallback timestamp is used for the emitted snapshot, latest sample copy,
  group latest timestamp, current rate window, and average-rate baseline.
- Existing `RegisterMetricsComponents()` callers keep the default system clock.

## Verification

Completed local verification:

- `dotnet test tests\FluxFlow.Components.Metrics.Tests\FluxFlow.Components.Metrics.Tests.csproj -c Release --no-restore`
  passed with 16 tests.
- `dotnet build FluxFlow.sln -c Release --no-restore` passed with 0 warnings.
- `dotnet test FluxFlow.sln -c Release --no-restore` passed.
- `dotnet pack src\FluxFlow.Components.Metrics\FluxFlow.Components.Metrics.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`
  created the package and symbol package.

Release evidence will be added after full verification, tag publication, and
public package smoke testing.
