# FluxFlow.Components.State.Composition

Optional registration and Designer metadata for keyed JSON state reduction.

The configured node uses `JsonStateReducerNode`, with
`StateReducerInput<JsonElement>` Input,
`StateReducerResult<JsonElement>` Output, and Events. Errors share Output and
are routed with normal conditions; there is no Errors port.

Reducer/key expressions, initial state, key limits, diagnostic caps, and
runtime hints remain flat. Expression engine is required; clock and context
resources are optional and host-owned.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit StateComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddState();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`StateComponents.StateReducer` is the typed contract used by both generic `AddComponent` and `AddStateReducer`. Its handle exposes named `Input`, `Output`, and `Events` ports. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor. Normal
code-first hosting therefore calls only `AddFluxFlow(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.
