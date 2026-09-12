# FluxFlow.Components.Payloads.Composition

Optional runtime registration and authoring for payload.inspect.

Canonical FlowMessage ports carry exact content and inspection outcomes. Limits,
previews, detection, formatting and runtime options remain flat; the optional clock
is host-owned. There is no codec-catalog resource, cached decoded value or Errors
port. Downstream parsing remains an explicit serialization step.

Editor hints are separate: install `FluxFlow.Components.Payloads.Designer` and call
`AddPayloadsDesign()` after `AddPayloads()`. Runtime registration alone does not
register a presentation catalog, activate components or resolve host resources.

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

`PayloadsComponents.PayloadInspection` is the typed contract used by both generic `AddComponent` and `AddPayloadInspection`. Its handle exposes named `Input`, `Output`, and `Events` ports. See [typed code-first authoring](../../../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor.
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.
