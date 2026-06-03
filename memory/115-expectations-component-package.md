# Expectations Component Package

Date: 2026-06-03

## Package

- Package: `FluxFlow.Components.Expectations`
- Version: `0.1.0-alpha.1`
- Tag: `components-expectations-v0.1.0-alpha.1`

## Goal

Add reusable event expectation nodes that let workflows wait for or guard
against neutral runtime events without requiring a separate scenario runner.

## Decisions

- The package owns two node types: `event.expect` and `event.guard`.
- Both nodes consume engine-owned `FlowEvent` values through `Input`.
- Both nodes emit one `EventExpectationResult` through `Result`.
- The package reuses the neutral `EventFilter` and `EventSummary` contracts
  from `FluxFlow.Components.Projections` so event matching stays consistent
  across projection and expectation packages.
- `event.expect` is satisfied when a matching event is observed.
- `event.guard` is not satisfied when a matching event is observed.
- Timeout behavior is optional and uses `IExpectationClock` for deterministic
  delay and result timestamps.
- Input completion produces a final result when no earlier match or timeout
  resolved the node.
- Nodes continue to fit normal FluxFlow port, completion, error, and diagnostic
  behavior.

## Verification

- `dotnet test tests\FluxFlow.Components.Expectations.Tests\FluxFlow.Components.Expectations.Tests.csproj -c Release --no-restore /nr:false`
- `dotnet build FluxFlow.sln -c Release --no-restore /nr:false`
- `dotnet test FluxFlow.sln -c Release --no-restore /nr:false`
- Direct clock scan confirmed only `SystemExpectationClock` reads current time
  directly in the package.
- Local package artifact was created in `artifacts\packages`.
- Mainline workflow passed after push.
- Release workflow passed for `components-expectations-v0.1.0-alpha.1`.
- Public-feed restore smoke installed `FluxFlow.Components.Expectations`
  `0.1.0-alpha.1` into a clean temporary console project and ran a neutral
  contract sample successfully.

## Next

The next general-purpose package candidate is designer metadata: neutral
component and option metadata contracts that hosts can use to generate editors
without hardcoding every package in host-specific UI code.
