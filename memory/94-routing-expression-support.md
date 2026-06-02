# Routing Shared Expression Support

Date: 2026-06-02

## Decision

Migrate `FluxFlow.Components.Routing` to use the shared expression support
package introduced for component authors.

Routing owns expression engine resolution plus typed context factories across
switch, correlation, and join nodes, so this slice uses both
`FlowExpressionEngineRegistry` and `FlowContextFactoryRegistry`.

## Scope

Prepared `FluxFlow.Components.Routing` `0.8.0-alpha.1` with:

- dependency on `FluxFlow.Components.Expressions`
- `RoutingComponentOptions` using shared expression engine registration
- `RoutingComponentOptions` using shared context factory registration
- unchanged public `UseExpressionEngine`
- unchanged public `UseExpressionEngineResolver`
- unchanged public context factory registration methods
- unchanged routing node ports and behavior

## Test Coverage

Added a switch regression test proving assignable context factories resolve to
the most specific registered factory for the runtime input type.

## Verification

- `dotnet test tests\FluxFlow.Components.Routing.Tests\FluxFlow.Components.Routing.Tests.csproj -c Release --no-restore`
- `dotnet build FluxFlow.sln -c Release --no-restore`
- `dotnet test FluxFlow.sln -c Release --no-restore`
- `dotnet pack src\FluxFlow.Components.Routing\FluxFlow.Components.Routing.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`

## Next

Run package review and verification, then release
`FluxFlow.Components.Routing` `0.8.0-alpha.1`.
