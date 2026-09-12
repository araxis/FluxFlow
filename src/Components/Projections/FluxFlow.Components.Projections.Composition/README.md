# FluxFlow.Components.Projections.Composition

Optional runtime registration and authoring for event.project.

Canonical FlowMessage input is materialized as a projection event inside the
component. Output carries snapshots or errors; Events carries diagnostics. Filter,
rate, emission, preview and runtime options remain flat. The optional clock is
host-owned, and ordinary link conditions select output outcomes.

Editor hints are separate: install `FluxFlow.Components.Projections.Designer` and call
`AddProjectionsDesign()` after `AddProjections()`. Runtime registration alone does not
register a presentation catalog, activate components or resolve host resources.

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

`ProjectionsComponents.EventProjection` is the typed contract used by both generic `AddComponent` and `AddEventProjection`. Its handle exposes named `Input`, `Output`, and `Events` ports. See [typed code-first authoring](../../../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor.
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.
