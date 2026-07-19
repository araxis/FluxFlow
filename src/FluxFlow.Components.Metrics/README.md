# FluxFlow.Components.Metrics

Standalone metric aggregation nodes for FluxFlow. The canonical node retains
the typed sample and snapshot domain contracts while representing successful,
partial, and failed aggregation outcomes through one normal `FlowResult<T>`
output. No Composition or Engine package is required.

## Canonical Node

| Node | Input | Output |
|------|-------|--------|
| `FlowMetricsAggregateNode` | `MetricSampleInput` | `FlowResult<MetricSnapshotOutput>` |

The node also exposes Events for lifecycle and aggregation diagnostics. It has
no universal Errors port.

```csharp
await using var node = new FlowMetricsAggregateNode(
    new MetricsAggregateOptions
    {
        RateWindowSeconds = 60,
        GroupByTag = "tenant",
        MaxGroups = 100,
        EmitEverySample = true
    });

var results = new BufferBlock<FlowMessage<FlowResult<MetricSnapshotOutput>>>();
node.Output.LinkTo(results);

await node.Input.SendAsync(FlowMessage.Create(new MetricSampleInput
{
    Timestamp = DateTimeOffset.UtcNow,
    Name = "request.duration",
    Value = 42.5,
    Unit = "ms",
    Size = 512,
    Tags = new Dictionary<string, string>
    {
        ["tenant"] = "north"
    }
}));
```

Accepted samples update total and per-group counts, numeric aggregates, size
totals, latest sample state, and rolling rates. The injected `TimeProvider`
supplies missing sample timestamps and diagnostic timestamps.

## Result Contract

Normal snapshots use the `snapshot` result kind. When
`EmitEverySample = false`, normal `Complete()` drains accepted input and emits
one `final-snapshot` result with the lineage of the last accepted sample.

Invalid samples use `aggregate-failed` with the stable
`metrics.invalid_sample` error code. Unexpected evaluation failures use the
same result kind with `metrics.aggregate_failed`. These are normal output
values, leave aggregate state unchanged, and do not prevent later input.

When `MaxGroups` is reached, the sample still updates the global aggregate but
cannot update a per-group entry. That partial application emits exactly one
`group-limit-reached` error result carrying the updated snapshot as its optional
Value. Every partially applied sample remains explicit, while internal tracking
of distinct rejected groups is bounded.

All result messages preserve correlation, trace, causation, and headers through
`FlowMessage<T>.With(...)`.

## Options

- `RateWindowSeconds` controls rolling current-rate calculations.
- `BoundedCapacity` bounds accepted input queued for ordered processing.
- `MaxGroups` bounds per-group aggregate state.
- `EmitEverySample` selects per-sample snapshots or one completion snapshot.
- `TrackLatest`, `TrackMinMax`, and `TrackSize` select snapshot detail.
- `GroupByTag` selects a tag value instead of `MetricSampleInput.Group`.
- `TreatMissingValueAsZero` counts absent numeric values as zero observations.

## Lifecycle

`Complete()` drains accepted samples and handles the configured final snapshot.
`Fault(exception)` remains the unexpected Dataflow fault surface, and
`DisposeAsync()` completes and drains the node.

## Direct-Result Compatibility

`MetricsAggregateNode` remains available with its released direct
`MetricSnapshotOutput` Output, Errors port, Events, and aggregation behavior. It
is a compatibility surface for existing code-authored workflows. No implicit
conversion exists between its output and
`FlowResult<MetricSnapshotOutput>` links.

## Composition

Use `FluxFlow.Components.Metrics.Composition` when a Composition host should
register the canonical `metrics.aggregate` factory and Designer metadata. Hosts
own optional keyed clocks and decide how snapshots are stored, displayed, or
forwarded.
