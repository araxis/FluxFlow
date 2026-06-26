# FluxFlow.Components.Expressions

Shared expression registration helpers for FluxFlow component packages.

This package does not include a concrete expression language. Applications and
adapter packages still provide `IFlowExpressionEngine` implementations.

## Helpers

| Type | Purpose |
|------|---------|
| `FlowExpressionEngineRegistry` | Registers named/default expression engines or a host-provided resolver. |
| `FlowContextFactoryRegistry<TFactory>` | Resolves exact, assignable, or default context factories by input type. |
| `ExpressionServiceCollectionExtensions` | Registers host-owned expression engines and typed map context factories as keyed DI resources. |

Use `Use(engine, useAsDefault: false)` for a named-only engine that should be
resolved explicitly by name but not become the fallback engine for empty names.
Engine names are trimmed for registration and lookup. Blank lookup names are
treated as the default engine, including when a custom resolver is configured.
Registry construction requires a non-blank scope name, engine registration
requires a non-null engine with a non-blank name, and custom resolver
registration requires a non-null delegate.
Context factory lookup prefers exact registrations, then a single most-specific
assignable registration, then the default factory. If multiple assignable
registrations match and no single registration is more specific than all others,
lookup fails with a deterministic ambiguity diagnostic that lists the matching
registration types.

The package is intended for component package authors. Application code usually
uses higher-level registration methods from packages such as Mapping, Control,
Assertions, Routing, State, or Observability.

Hosts that wire composition resources through keyed DI can register expression
services directly:

```csharp
services
    .AddFluxFlowExpressionEngine("primary", expressionEngine)
    .AddFluxFlowMapContextFactory<Order>("order-context", contextFactory);
```

These helpers only register already-owned services. They do not select an
expression language, compile expressions, scan assemblies, or create node
factories.

## Composition

This package does not expose standalone nodes or `FluxFlow.Composition`
factories. Composition adapters that need expressions resolve host-owned
`IFlowExpressionEngine` or context factory resources directly; these registries
are optional helper infrastructure for hosts and adapter packages.
