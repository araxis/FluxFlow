# State Shared Expression Support

Date: 2026-06-02

## Decision

Migrate `FluxFlow.Components.State` to use the shared expression support package
introduced for component authors.

State only needs expression engine registration, so this slice uses
`FlowExpressionEngineRegistry` without introducing context-factory helpers.

## Scope

Prepared `FluxFlow.Components.State` `0.2.0-alpha.1` with:

- dependency on `FluxFlow.Components.Expressions`
- `StateComponentOptions` using `FlowExpressionEngineRegistry`
- unchanged `UseExpressionEngine`
- unchanged `UseExpressionEngineResolver`
- unchanged state reducer ports and behavior

## Test Coverage

Added a reducer regression test proving that the public expression engine
resolver path still builds and runs a reducer node.

## Verification

- `dotnet test tests\FluxFlow.Components.State.Tests\FluxFlow.Components.State.Tests.csproj -c Release --no-restore`
- `dotnet build FluxFlow.sln -c Release --no-restore`
- `dotnet test FluxFlow.sln -c Release --no-restore`
- `dotnet pack src\FluxFlow.Components.State\FluxFlow.Components.State.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`

## Next

Release `FluxFlow.Components.State` `0.2.0-alpha.1` and verify a fresh
public-feed restore/build.
