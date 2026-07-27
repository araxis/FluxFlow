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
services.AddMetricsComponents();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.
