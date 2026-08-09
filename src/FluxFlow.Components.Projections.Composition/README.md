# FluxFlow.Components.Projections.Composition

Optional registration and Designer metadata for `event.project`.

The descriptor exposes `ProjectionEvent` Input,
`EventProjectionSnapshot` Output, and Events. Filter, rate, emission, preview,
diagnostic, and runtime options remain flat. The optional clock is host-owned.
Errors share Output and are selected by normal link conditions.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit ProjectionsComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddProjections();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`ProjectionsComponents.EventProjection` is the typed contract used by both generic `AddComponent` and `AddEventProjection`. Its handle exposes named `Input`, `Output`, and `Events` ports. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor. Normal
code-first hosting therefore calls only `AddFluxFlow(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.
