# FluxFlow.Components.Expressions

Shared expression registration helpers for FluxFlow component packages.

This package does not include a concrete expression language. Applications and
adapter packages provide `IFlowExpressionEngine` implementations and register
them through the host dependency-injection container.

## Registration

`ExpressionServiceCollectionExtensions` exposes two keyed-DI helpers:

- `AddFluxFlowExpressionEngine(name, engine)` (or its factory overload);
- `AddFluxFlowMapContextFactory<TInput>(name, factory)` (or its instance overload).

Names are trimmed and must be non-blank. Registrations are exact: there is no
package-global registry, implicit default, assignable-type fallback, or custom
resolver layer. Consumers resolve the requested keyed service from the host.

```csharp
services.AddFluxFlowExpressionEngine("rules", expressionEngine);
services.AddFluxFlowMapContextFactory<Order>("rules", orderContextFactory);
```

## Ownership

Keep the expression implementation and its context factories in the host or in
an explicit adapter package. Component packages depend only on the shared
mapping contracts and request the exact configured key they need.

## Composition

This support package does not register executable components or depend on
`FluxFlow.Composition`, and it does not expose Composition factories. A
component-family Composition adapter may call these helpers, but the host
remains responsible for the concrete expression services.
