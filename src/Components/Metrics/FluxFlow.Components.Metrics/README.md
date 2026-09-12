# FluxFlow.Components.Metrics

Standalone metric aggregation.

`MetricsAggregateNode` accepts `MetricSampleInput` and emits
`MetricSnapshotOutput`. Rate windows, grouping, latest/min/max/size tracking,
missing-value policy, and completion snapshots remain typed aggregation
behavior.

Snapshots are normal values. Invalid samples or aggregation failures become
`FlowError` on the same Output. The node exposes Events and uses an optional
host-owned `TimeProvider`; no Engine or Composition dependency is required.

Final coalesced snapshots use the shared processing instrumentation with a root
operation and one correlation link to the last accepted input. This is not a complete
history of contributors. Per-sample updates retain their existing input operations.
No context history is accumulated; host sampling/export remain optional. The retained
typed sample is cleared after final emission or disposal. The disposal override uses
an existing protected lifecycle hook; no new callable public API is introduced.

## Composition

Install `FluxFlow.Components.Metrics.Composition` for optional runtime registration and authoring. Editor metadata is separate: install `FluxFlow.Components.Metrics.Designer` and call `AddMetricsDesign()` after runtime registration. This runtime package remains free of Composition, Designer, and Engine dependencies.
