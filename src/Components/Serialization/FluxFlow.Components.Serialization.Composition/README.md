# FluxFlow.Components.Serialization.Composition

Optional registrations and Designer metadata for JSON, text, and Base64
conversion nodes.

Descriptors expose the exact `FlowContent`, `JsonElement`, or string port types
listed by the runtime package, one Output, and Events. Encoding, JSON, size, and
runtime options remain flat. The optional clock is host-owned.

Errors share Output. No codec catalog, result wrapper, or universal Errors port
is registered.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit SerializationComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddSerialization();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`SerializationComponents` exposes the six JSON, text, and Base64 typed contracts. The retained `AddX` methods use those same contracts, and every handle exposes `Events`. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from these contracts retains its executable descriptors.
Normal code-first hosting therefore calls only `AddFluxFlow(definition)` and
does not repeat the family registration above. Use that service registration
for JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contracts.
