# Control Shared Expression Support

Date: 2026-06-02

## Decision

Migrate `FluxFlow.Components.Control` to use the shared expression support
package introduced for component authors.

This keeps the public Control registration API stable while reducing duplicate
registration logic in another expression-driven package.

## Scope

Prepared `FluxFlow.Components.Control` `0.3.0-alpha.1` with:

- dependency on `FluxFlow.Components.Expressions`
- `ControlComponentOptions` using `FlowExpressionEngineRegistry`
- `ControlComponentOptions` using `FlowContextFactoryRegistry<IControlContextFactory>`
- unchanged `UseExpressionEngine`
- unchanged `UseExpressionEngineResolver`
- unchanged `UseDefaultContextFactory`
- unchanged `UseContextFactory`
- unchanged filter/when ports and behavior

## Test Coverage

Added a filter regression test proving that Control resolves the most-specific
assignable context factory for a derived input type.

## Verification

- `dotnet test tests\FluxFlow.Components.Control.Tests\FluxFlow.Components.Control.Tests.csproj -c Release --no-restore`
- `dotnet build FluxFlow.sln -c Release --no-restore`
- `dotnet test FluxFlow.sln -c Release --no-restore`
- `dotnet pack src\FluxFlow.Components.Control\FluxFlow.Components.Control.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`

## Next

Release `FluxFlow.Components.Control` `0.3.0-alpha.1` and verify a fresh
public-feed restore/build.
