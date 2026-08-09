# FluxFlow.Components.Payloads.Composition

Optional registration and Designer metadata for `payload.inspect`.

The descriptor declares `FlowContent` Input,
`PayloadInspectionResult` Output, and Events. Limit, preview, detection,
formatting, and runtime options remain flat. The optional clock is host-owned.

There is no codec-catalog resource, cached decoded value, result wrapper, or
Errors port. Parsing needed by downstream nodes is an explicit Serialization
step.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit PayloadsComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddPayloads();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`PayloadsComponents.PayloadInspection` is the typed contract used by both generic `AddComponent` and `AddPayloadInspection`. Its handle exposes named `Input`, `Output`, and `Events` ports. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor. Normal
code-first hosting therefore calls only `AddFluxFlow(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.
