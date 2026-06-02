# Observability Shared Expression Support

Date: 2026-06-02

## Decision

Migrate `FluxFlow.Components.Observability` to use the shared expression support
package introduced for component authors.

Observability owns predicate evaluation and typed context factories for
observer nodes, so this slice uses both `FlowExpressionEngineRegistry` and
`FlowContextFactoryRegistry`.

## Scope

Prepared `FluxFlow.Components.Observability` `0.2.0-alpha.1` with:

- dependency on `FluxFlow.Components.Expressions`
- `ObservabilityComponentOptions` using shared expression engine registration
- `ObservabilityComponentOptions` using shared context factory registration
- unchanged public `UseExpressionEngine`
- unchanged public `UseExpressionEngineResolver`
- unchanged public context factory registration methods
- unchanged observer node ports and behavior

## Test Coverage

Added a counter regression test proving assignable context factories resolve to
the most specific registered factory for the runtime input type.

## Verification

- `dotnet test tests\FluxFlow.Components.Observability.Tests\FluxFlow.Components.Observability.Tests.csproj -c Release --no-restore`
- `dotnet build FluxFlow.sln -c Release --no-restore`
- `dotnet test FluxFlow.sln -c Release --no-restore`
- `dotnet pack src\FluxFlow.Components.Observability\FluxFlow.Components.Observability.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`

## Next

Run package review and verification, then release
`FluxFlow.Components.Observability` `0.2.0-alpha.1`.
