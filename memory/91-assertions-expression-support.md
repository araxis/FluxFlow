# Assertions Shared Expression Support

Date: 2026-06-02

## Decision

Migrate `FluxFlow.Components.Assertions` to use the shared expression support
package introduced for component authors.

Assertions keeps its public registration surface stable and gains the same
most-specific context factory resolution behavior as Mapping and Control.

## Scope

Prepared `FluxFlow.Components.Assertions` `0.2.0-alpha.1` with:

- dependency on `FluxFlow.Components.Expressions`
- `AssertionsComponentOptions` using `FlowExpressionEngineRegistry`
- `AssertionsComponentOptions` using
  `FlowContextFactoryRegistry<IAssertionContextFactory>`
- unchanged `UseExpressionEngine`
- unchanged `UseExpressionEngineResolver`
- unchanged `UseDefaultContextFactory`
- unchanged `UseContextFactory`
- unchanged assertion ports and behavior

## Test Coverage

Added an assertion regression test proving that Assertions resolves the
most-specific assignable context factory for a derived input type.

## Verification

- `dotnet test tests\FluxFlow.Components.Assertions.Tests\FluxFlow.Components.Assertions.Tests.csproj -c Release --no-restore`
- `dotnet build FluxFlow.sln -c Release --no-restore`
- `dotnet test FluxFlow.sln -c Release --no-restore`
- `dotnet pack src\FluxFlow.Components.Assertions\FluxFlow.Components.Assertions.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`
- Released `FluxFlow.Components.Assertions` `0.2.0-alpha.1`.
- Verified fresh public-feed restore/build with
  `FluxFlow.Components.Assertions` `0.2.0-alpha.1`.

## Next

Continue shared expression support migration into the next expression-driven
component package, starting with State or Observability.
