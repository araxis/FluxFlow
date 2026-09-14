# FluxFlow.Components.Metrics

Standalone metric aggregation.

Sample deserialization now lives directly in `MetricsAggregateNode`, using local
System.Text.Json options. Composition reads `MetricsInputShapes.Sample` from the
Contracts namespace independently of runtime parsing.

Migration: the public `MetricSampleInputMaterializer` class has been removed.
Replace its `InputShape` access with `MetricsInputShapes.Sample`. Direct
`Materialize` callers can deserialize locally with their own error policy, or send
a `FlowMessage` to the node for the existing Metrics error-output behavior.
Node constructors and ports are unchanged.

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
input envelope is cleared after final emission or disposal. The disposal override uses
an existing protected lifecycle hook; no new callable public API is introduced.

Processing keeps the original `FlowMessage` beside the locally parsed
`MetricSampleInput`. Snapshots are emitted with `message.With(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))`
and errors with `message.WithError(error)`, without intermediate typed envelopes.
When emitting only at completion, the node retains one last accepted input envelope,
including its JSON payload, until final emission or disposal.

## Composition

Install `FluxFlow.Components.Metrics.Composition` for optional runtime registration and definition. This runtime package remains free of Composition, Designer, and Engine dependencies.
