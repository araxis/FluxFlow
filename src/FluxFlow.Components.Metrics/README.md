# FluxFlow.Components.Metrics

Standalone metric aggregation.

`MetricsAggregateNode` accepts `MetricSampleInput` and emits
`MetricSnapshotOutput`. Rate windows, grouping, latest/min/max/size tracking,
missing-value policy, and completion snapshots remain typed aggregation
behavior.

Snapshots are normal values. Invalid samples or aggregation failures become
`FlowError` on the same Output. The node exposes Events and uses an optional
host-owned `TimeProvider`; no Engine or Composition dependency is required.

## Composition

Install `FluxFlow.Components.Metrics.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
