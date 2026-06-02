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

Completed local verification:

- `dotnet test tests\FluxFlow.Components.Validation.Tests\FluxFlow.Components.Validation.Tests.csproj -c Release --no-restore`
  passed with 11 tests.
- `dotnet build FluxFlow.sln -c Release --no-restore` passed with 0 warnings.
- `dotnet test FluxFlow.sln -c Release --no-restore` passed.
- `dotnet pack src\FluxFlow.Components.Validation\FluxFlow.Components.Validation.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`
  created the package and symbol package.
- Commit: `a82a446` (`Add deterministic validation clock`).
- Tag: `components-validation-v0.2.0-alpha.1`.
- Release workflow: `26842569760`, success.
- Main CI workflow: `26842554223`, success.
- Public package restore/build smoke passed on attempt 8 after public-feed
  indexing caught up.
