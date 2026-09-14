# FluxFlow.Components.Metrics.Composition

Optional runtime registration and definition for metric.aggregate.

Canonical FlowMessage input is materialized as a metric sample inside the component.
Output carries aggregate snapshots or errors; Events remains diagnostic. Rate,
grouping, emission, snapshot and aggregation options stay flat. The optional clock
is host-owned. Metric aggregation remains an optional module, not a Core concern.

## DI Registration

This adapter registers direct node factories through `IServiceCollection`:

```csharp
services.AddFluxFlowMetrics();
```

The resulting type-to-factory dictionary is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first definition

`AddMetricAggregation` retain typed options and named port handles. They embed the
same callbacks used by DI in ApplicationSetup.ComponentFactories. Build the setup
with application services or pass it to AddFluxFlowUpdates. Repeating the family
DI registration is optional: matching delegates are idempotent and conflicts fail.
Links validate the actual ports after node construction. Factories remain in process
and cannot be exported as JSON; the flat JSON configuration shape is unchanged.

The former MetricsComponents contract container is removed. Standalone node
construction remains available. Shared host resources are not disposed by components.
