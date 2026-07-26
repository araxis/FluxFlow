# FluxFlow.Components.Metrics.Composition

Optional registration and Designer metadata for `metric.aggregate`.

Metadata declares `MetricSampleInput` Input, `MetricSnapshotOutput` Output, and
Events. Rate, grouping, emission, snapshot, aggregation, and runtime options
remain flat. The optional clock is host-owned. Errors are routed from normal
Output; there is no Errors port.

## Registration And Design Metadata

Register components with `RegisterMetricsAggregate`. `MetricsComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.
