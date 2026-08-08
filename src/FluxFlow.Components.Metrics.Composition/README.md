# FluxFlow.Components.Metrics.Composition

Optional registration and Designer metadata for `metric.aggregate`.

Metadata declares `MetricSampleInput` Input, `MetricSnapshotOutput` Output, and
Events. Rate, grouping, emission, snapshot, aggregation, and runtime options
remain flat. The optional clock is host-owned. Errors are routed from normal
Output; there is no Errors port.

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

`MetricsComponents.MetricAggregation` is the typed contract used by both generic `AddComponent` and `AddMetricAggregation`. Its handle exposes named `Input`, `Output`, and `Events` ports. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor. Normal
code-first hosting therefore calls only `AddFluxFlow(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.
