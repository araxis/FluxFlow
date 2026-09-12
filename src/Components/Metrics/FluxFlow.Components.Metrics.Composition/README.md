# FluxFlow.Components.Metrics.Composition

Optional runtime registration and authoring for metric.aggregate.

Canonical FlowMessage input is materialized as a metric sample inside the component.
Output carries aggregate snapshots or errors; Events remains diagnostic. Rate,
grouping, emission, snapshot and aggregation options stay flat. The optional clock
is host-owned. Metric aggregation remains an optional module, not a Core concern.

Editor hints are separate: install `FluxFlow.Components.Metrics.Designer` and call
`AddMetricsDesign()` after `AddMetrics()`. Runtime registration alone does not
register a presentation catalog, activate components or resolve host resources.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit MetricsComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddMetrics();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`MetricsComponents.MetricAggregation` is the typed contract used by both generic `AddComponent` and `AddMetricAggregation`. Its handle exposes named `Input`, `Output`, and `Events` ports. See [typed code-first authoring](../../../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor.
Definition-based update hosting therefore calls only `AddFluxFlowUpdates(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.
