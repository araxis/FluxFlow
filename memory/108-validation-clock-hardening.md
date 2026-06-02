# Validation Clock Hardening

Date: 2026-06-02

## Decision

Harden `FluxFlow.Components.Validation` with a host-provided clock for JSON
schema validation result timestamps.

Validation results are often consumed by assertions, dashboards, logs, and
scenario checks. The package should keep the default system behavior for
existing callers while allowing deterministic result timestamps in hosts and
tests.

## Package Shape

- Package: `FluxFlow.Components.Validation`
- Version: `0.2.0-alpha.1`
- Clock contract: `IValidationClock`
- Default clock: `SystemValidationClock`
- Registration:
  `RegisterValidationComponents(options => options.UseClock(clock))`

## Behavior

- `json.schema-validator` uses the configured clock for
  `JsonSchemaValidationResult<TInput>.Timestamp`.
- Existing `RegisterValidationComponents()` callers keep the default system
  clock.
- Type registration and value selector behavior are unchanged.

## Verification

Initial local verification:

- `dotnet test tests\FluxFlow.Components.Validation.Tests\FluxFlow.Components.Validation.Tests.csproj -c Release --no-restore`
  passed with 11 tests.
