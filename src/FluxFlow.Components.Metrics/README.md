# FluxFlow.Components.Metrics

Reusable metrics aggregation components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `metrics.aggregate` | `Input` -> `Output`, `Errors` | Aggregates metric samples into count, rate, size, value, latest, and group snapshots. |

## Aggregate

```json
{
  "type": "metrics.aggregate",
  "name": "metrics",
  "rateWindowSeconds": 60,
  "boundedCapacity": 128,
  "maxGroups": 1024,
  "emitEverySample": true,
  "trackLatest": true,
  "trackMinMax": true,
  "trackSize": true,
  "groupByTag": "topic"
}
```

`metrics.aggregate` consumes `MetricSampleInput` values and emits
`MetricSnapshotOutput` values. Missing numeric values still count as samples;
they are not added to numeric totals unless `treatMissingValueAsZero` is
enabled.

Group tracking is bounded by `maxGroups`. Samples that exceed the group limit
still update global totals, but the new group is not added and a `FlowError`
is emitted once per rejected group.

Snapshot output is bounded. When the output buffer is full, processing
continues and the dropped snapshot is reported through `Errors`. Unlinked
outputs do not block input processing.

## Sample

```csharp
new MetricSampleInput
{
    Timestamp = receivedAt,
    Name = "message",
    Group = topic,
    Size = payloadLength,
    Tags = new Dictionary<string, string>
    {
        ["topic"] = topic
    }
};
```

## Registration

```csharp
registry.RegisterMetricsComponents();
```

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
