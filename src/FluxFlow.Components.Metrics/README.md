# FluxFlow.Components.Metrics

A standalone metrics-aggregation node for FluxFlow — a "blockified" rolling aggregator.

## What it is

`MetricsAggregateNode` is a self-contained TPL Dataflow processor. You post
`MetricSampleInput`s to its input and it broadcasts `MetricSnapshotOutput`s on its
output (rejected-group / invalid-sample failures on the error port, diagnostics on
the event port). It needs **nothing else** — no engine, registry, or runtime:

```csharp
await using var node = new MetricsAggregateNode();

node.Output.LinkTo(dashboard.Input);   // broadcast: link the output to as many
node.Output.LinkTo(logger.Input);      // downstream nodes as you like

await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput
{
    Name = "message",
    Group = topic,
    Size = payloadLength
}));
```

Every message travels as a `FlowMessage<T>` envelope, so the correlation id flows
from the sample that produced a snapshot through to that snapshot for free.

## Ports

| Port | Block | Purpose |
|------|-------|---------|
| `Input` | `BufferBlock<FlowMessage<MetricSampleInput>>` | bounded intake — `SendAsync` applies backpressure |
| `Output` | `BroadcastBlock<FlowMessage<MetricSnapshotOutput>>` | the rolling snapshot, fanned out to every linked consumer |
| `Errors` | `BroadcastBlock<FlowError>` | invalid samples (`InvalidSample`) and rejected groups (`GroupLimitReached`) |
| `Events` | `BroadcastBlock<FlowEvent>` | `metrics.aggregate.updated` / `.failed` / `.group-limit-reached` notes |

Outputs are broadcast (latest-wins, no backpressure): a consumer that keeps up
sees every snapshot; one that falls badly behind may miss some. That is the
deliberate trade for simplicity. If a graph genuinely must not drop, bridge that
edge through its own bounded buffer.

## Behavior

`metrics.aggregate` folds each `MetricSampleInput` into a running snapshot —
count, value (total/average/min/max), rate (windowed current + lifetime average),
size, the latest sample, and per-group breakdowns. Missing numeric values still
count as samples; they are not added to numeric totals unless
`TreatMissingValueAsZero` is enabled.

Group tracking is bounded by `MaxGroups`. Samples that exceed the group limit
still update global totals, but the new group is not added and a `FlowError`
(`GroupLimitReached`) is emitted once per rejected group; rejected-group tracking
is itself capped, after which a single summary error is emitted.

By default the node emits a snapshot on every sample (`EmitEverySample = true`).
Set `EmitEverySample = false` to coalesce: the node emits a single final snapshot
as the input drains.

## Options

```csharp
new MetricsAggregateOptions
{
    RateWindowSeconds = 60,        // window for the "current rate" calculation
    BoundedCapacity = 128,         // input buffer size
    MaxGroups = 1024,              // per-group itemization cap
    EmitEverySample = true,        // false to emit only the final snapshot
    TrackLatest = true,
    TrackMinMax = true,
    TrackSize = true,
    GroupByTag = "topic",          // group by a tag value instead of Group
    TreatMissingValueAsZero = false
};
```

A `TimeProvider` can be injected for deterministic fallback timestamps. Explicit
sample timestamps always win; the time provider is used only when
`MetricSampleInput` omits `Timestamp`.

```csharp
await using var node = new MetricsAggregateNode(options, timeProvider);
```

## Composition

Building a workflow, reading config, creating nodes, and linking them is a
separate concern from the node. This package is just the standalone node.

Use `FluxFlow.Components.Metrics.Composition` when a `FluxFlow.Composition`
host should register the optional `metrics.aggregate` factory:

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterMetricsAggregate());
```

The composition adapter binds `MetricsAggregateOptions` from node configuration
and can resolve an optional keyed `TimeProvider` resource named `clock`.

The optional composition package also exposes
`MetricsComponentDesignMetadataProvider` for neutral Designer metadata over the
`metrics.aggregate` composition node type. The standalone Metrics package
remains free of Designer, Composition, and Engine dependencies.
