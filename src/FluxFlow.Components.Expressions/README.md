# FluxFlow.Components.Expressions

Shared expression registration helpers for FluxFlow component packages.

This package does not include a concrete expression language. Applications and
adapter packages still provide `IFlowExpressionEngine` implementations.

## Helpers

| Type | Purpose |
|------|---------|
| `FlowExpressionEngineRegistry` | Registers named/default expression engines or a host-provided resolver. |
| `FlowContextFactoryRegistry<TFactory>` | Resolves exact, assignable, or default context factories by input type. |

Use `Use(engine, useAsDefault: false)` for a named-only engine that should be
resolved explicitly by name but not become the fallback engine for empty names.
Engine names are trimmed for registration and lookup. Blank lookup names are
treated as the default engine, including when a custom resolver is configured.
Context factory lookup prefers exact registrations, then a single most-specific
assignable registration, then the default factory. If multiple assignable
registrations match and no single registration is more specific than all others,
lookup fails with a deterministic ambiguity diagnostic that lists the matching
registration types.

The package is intended for component package authors. Application code usually
uses higher-level registration methods from packages such as Mapping, Control,
Assertions, Routing, State, or Observability.

## Composition

This package does not expose standalone nodes or `FluxFlow.Composition`
factories. Composition adapters that need expressions resolve host-owned
`IFlowExpressionEngine` or context factory resources directly; these registries
are optional helper infrastructure for hosts and adapter packages.
