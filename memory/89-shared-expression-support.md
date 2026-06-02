# Shared Expression Support Package

Date: 2026-06-02

## Decision

Add `FluxFlow.Components.Expressions` as a small supporting package for common
component expression setup.

The engine remains free of concrete expression-language implementations.
Component packages can share registration mechanics without each package
copying the same named/default engine and context-factory resolution code.

## Scope

Prepared `FluxFlow.Components.Expressions` `0.1.0-alpha.1` with:

- `FlowExpressionEngineRegistry`
- `FlowContextFactoryRegistry<TFactory>`
- named expression engine lookup
- default expression engine lookup
- host-provided expression engine resolver support
- exact, assignable, most-specific, and default context-factory resolution

No nodes are included in this package. It is a package-authoring helper for
other component packages.

## First Migration

Prepared `FluxFlow.Components.Mapping` `0.2.0-alpha.1` as the first component
package to use the shared expression support.

Mapping keeps its existing public registration API:

- `UseExpressionEngine`
- `UseExpressionEngineResolver`
- `UseDefaultContextFactory`
- `UseContextFactory`

This keeps current hosts compatible while reducing duplicated package code.

## Verification

- `dotnet test tests\FluxFlow.Components.Expressions.Tests\FluxFlow.Components.Expressions.Tests.csproj -c Release --no-restore`
- `dotnet test tests\FluxFlow.Components.Mapping.Tests\FluxFlow.Components.Mapping.Tests.csproj -c Release --no-restore`
- `dotnet build FluxFlow.sln -c Release --no-restore`
- `dotnet test FluxFlow.sln -c Release --no-restore`
- `dotnet pack src\FluxFlow.Components.Expressions\FluxFlow.Components.Expressions.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`
- `dotnet pack src\FluxFlow.Components.Mapping\FluxFlow.Components.Mapping.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`
- Released `FluxFlow.Components.Expressions` `0.1.0-alpha.1`.
- Verified fresh public-feed restore/build with
  `FluxFlow.Components.Expressions` `0.1.0-alpha.1`.
- Released `FluxFlow.Components.Mapping` `0.2.0-alpha.1`.
- Verified fresh public-feed restore/build with
  `FluxFlow.Components.Mapping` `0.2.0-alpha.1`.

## Release Order

Release `FluxFlow.Components.Expressions` first, then
`FluxFlow.Components.Mapping`, because Mapping depends on Expressions.

## Next

Migrate the same helper pattern into the next expression-based package,
starting with Control or Assertions.
