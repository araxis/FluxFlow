# FluxFlow.Components.Metrics.Composition

Composition registration and Designer metadata for the canonical
`metric.aggregate` component. It consumes typed metric samples and emits one
normal `FlowResult<MetricSnapshotOutput>` output.

Existing definitions using `metrics.aggregate` remain supported as a hidden
alias; new definitions and Designer palettes use `metric.aggregate`.

This package does not scan assemblies, create metric exporters, own clocks, or
add renderer behavior.

## Registration

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterMetricsAggregate());
```

| Type | Resources | Input | Output |
|------|-----------|-------|--------|
| `metric.aggregate` | optional `clock` | `MetricSampleInput` | `FlowResult<MetricSnapshotOutput>` |

The descriptor exposes Events and no universal Errors port. Snapshots are
successful variants; invalid samples and partial group-limit applications are
normal error variants on Output.

## Flat Definition

```json
{
  "Resources": {
    "System": {
      "Clock": {
        "Type": "host.clock"
      }
    }
  },
  "Workflows": {
    "Telemetry": {
      "AggregateRequests": {
        "Type": "metric.aggregate",
        "clock": "Resources.System.Clock",
        "rateWindowSeconds": 60,
        "maxGroups": 100,
        "emitEverySample": true,
        "trackLatest": true,
        "trackMinMax": true,
        "trackSize": true,
        "groupByTag": "tenant",
        "treatMissingValueAsZero": false,
        "Input": "MetricSource.Output"
      }
    }
  }
}
```

Component settings, resource references, and port links are flat. Resource
addresses and component/port names are exact and case-sensitive. With
`emitEverySample` disabled, one final snapshot is emitted automatically after
accepted input drains during normal composition completion.

## Design Metadata

Hosts should compose this provider through `ComponentDesignMetadataCatalog`.
The canonical catalog adds the traced `Events` output and an optional semantic
`processing` profile picker, and omits legacy `name`, `boundedCapacity`,
`maxDegreeOfParallelism`, and `ensureOrdered` options from normal editing.
Default execution requires no processing profile; raw provider metadata retains
released declarations for compatibility.


`MetricsComponentDesignMetadataProvider` describes the typed sample input,
normal result output, option sections/editor hints, and optional host-owned
clock picker using an exact `Resources.{name}` address. The metadata is
descriptive only; hosts own palette and inspector rendering, resource selection,
validation UI, activation, and persistence.

The runtime package still contains the released direct-result aggregate node
for code-authored compatibility. Composition `2.x` registers only the canonical
fixed contract; install Composition `1.x` for an existing legacy definition.
