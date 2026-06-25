# FluxFlow.Components.Metrics.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone metrics
aggregate node from `FluxFlow.Components.Metrics`.

This package does not scan assemblies, resolve CLR types from strings, create
metric exporters, or own telemetry sinks. Hosts register the metrics aggregate
factory explicitly and may provide an optional keyed `TimeProvider`.

## Registration

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterMetricsAggregate());
```

## Node Types

| Type | Node | Ports |
|------|------|-------|
| `metrics.aggregate` | `MetricsAggregateNode` | `Input`, `Output` |

The factory exposes `Events` and `Errors`. `clock` is an optional keyed
`TimeProvider` resource for deterministic fallback timestamps, event timestamps,
and error timestamps. The request/result CLR types are fixed to
`MetricSampleInput` and `MetricSnapshotOutput`.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "metrics": {
              "type": "metrics.aggregate",
              "resources": {
                "clock": "fixed"
              },
              "configuration": {
                "rateWindowSeconds": 60,
                "boundedCapacity": 128,
                "maxGroups": 1024,
                "emitEverySample": true,
                "trackLatest": true,
                "trackMinMax": true,
                "trackSize": true,
                "groupByTag": "topic",
                "treatMissingValueAsZero": false
              }
            }
          },
          "links": []
        }
      }
    }
  }
}
```

The node binds the existing `MetricsAggregateOptions` shape from composition
configuration.

## Design Metadata

`MetricsComponentDesignMetadataProvider` exposes neutral Designer metadata for
`metrics.aggregate` so hosts can build palettes, editors, validation hints, or
documentation without copying package descriptors. The metadata describes the
existing metrics aggregate option record, fixed ports, and optional `clock`
resource hint. Optional keyed `TimeProvider` clocks remain host-owned resources
and are not modeled as editable node options.
