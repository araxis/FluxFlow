# FluxFlow.Components.Observability.Composition

Optional runtime registration and authoring for Counter, Logger and Metrics.

Canonical FlowMessage ports carry values, snapshots, log entries and in-band errors.
JSON materialization and expression evaluation stay local to each component.
Expression engines, context factories, selectors and clocks remain host-owned
resources. Filtering, logging, metric and attribute settings stay flat.

Editor hints are separate: install `FluxFlow.Components.Observability.Designer` and call
`AddObservabilityDesign()` after `AddObservability()`. Runtime registration alone does not
register a presentation catalog, activate components or resolve host resources.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit ObservabilityComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddObservability();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`ObservabilityComponents` exposes `Counter`, `Logger`, and `Metrics` typed contracts. The retained `AddX` methods use those same contracts, and every handle exposes `Events`. See [typed code-first authoring](../../../../docs/39-typed-code-first-authoring.md).

A definition built from these contracts retains its executable descriptors.
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and
does not repeat the family registration above. Use that service registration
for JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contracts.
