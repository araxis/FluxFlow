# State Clock Hardening

Date: 2026-06-02

## Decision

Harden `FluxFlow.Components.State` with a host-provided clock for reducer
result timestamps.

State reducer results are often consumed by assertions, dashboards, and
follow-up workflow steps. The package should keep the default system behavior
for existing callers while allowing deterministic timestamps in hosts and tests.

## Package Shape

- Package: `FluxFlow.Components.State`
- Version: `0.3.0-alpha.1`
- Clock contract: `IStateClock`
- Default clock: `SystemStateClock`
- Registration: `RegisterStateComponents(options => options.UseClock(clock))`

## Behavior

- `state.reducer` uses the configured clock for `StateReducerResult.UpdatedAt`.
- Reduce, reset, and clear operations all use the configured clock.
- Existing `RegisterStateComponents()` callers keep the default system clock.

## Verification

Completed local verification:

- `dotnet test tests\FluxFlow.Components.State.Tests\FluxFlow.Components.State.Tests.csproj -c Release --no-restore`
  passed with 12 tests.
- `dotnet build FluxFlow.sln -c Release --no-restore` passed with 0 warnings.
- `dotnet test FluxFlow.sln -c Release --no-restore` passed.
- `dotnet pack src\FluxFlow.Components.State\FluxFlow.Components.State.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`
  created the package and symbol package.
- Commit: `32aa8b0` (`Add deterministic state clock`).
- Tag: `components-state-v0.3.0-alpha.1`.
- Release workflow: `26839708645`, success.
- Main CI workflow: `26839700691`, success.
- Public package restore/build smoke passed on attempt 9 after public-feed
  indexing caught up.
