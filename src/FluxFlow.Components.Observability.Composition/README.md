# FluxFlow.Components.Observability.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone
observability nodes from `FluxFlow.Components.Observability`.

This package does not create telemetry sinks, scan assemblies, resolve CLR types
from strings, or own expression and selector services. Hosts register the
observability node factories explicitly and provide keyed resources when a node
configuration needs them.

## Registration

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterCounter<MyMessage>()
        .RegisterLogger<MyMessage>()
        .RegisterMetrics<MyMessage>());
```

## Node Types

| Type | Node | Ports |
|------|------|-------|
| `flow.counter` | `FlowCounterNode<TInput>` | `Input`, `Output` |
| `flow.logger` | `FlowLoggerNode<TInput>` | `Input`, `Output` |
| `flow.metrics` | `FlowMetricsNode<TInput>` | `Input`, `Output` |

All factories expose `Events` and `Errors`. Registrations are closed over
`TInput`; hosts that need multiple input shapes should use custom node type
strings such as `flow.counter.order`.

## Resources

- `clock`: optional keyed `TimeProvider` for all nodes.
- `engine`: required keyed `IFlowExpressionEngine` only when counter options
  configure `predicate` or `expression`.
- `contextFactory`: optional keyed `IFlowMapContextFactory<TInput>` for counters.
- `sizeSelector`: optional keyed `IObservabilityValueSelector<TInput>` for
  metrics.
- `attribute:{name}`: required keyed `IObservabilityValueSelector<TInput>` for
  each logger `attributeSelectors` entry.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "logger": {
              "type": "flow.logger",
              "resources": {
                "clock": "fixed",
                "attribute:kind": "kind-selector"
              },
              "configuration": {
                "inputType": "message",
                "level": "Information",
                "category": "workflow",
                "messageTemplate": "Observed {kind} item #{sequence}",
                "attributeSelectors": [ "kind" ],
                "boundedCapacity": 128
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

Each node binds its existing options record from composition configuration.
Invalid observability options, such as blank `inputType`, non-positive
`boundedCapacity`, or unsupported logger `level`, fail during composition build
and surface as factory diagnostics when build failures are configured as
diagnostics.

## Design Metadata

`ObservabilityComponentDesignMetadataProvider` exposes neutral Designer metadata
for `flow.counter`, `flow.logger`, and `flow.metrics` so hosts can build
palettes, editors, validation hints, or documentation without copying package
descriptors. The metadata describes the existing observability option records,
fixed ports, option grouping/editor hints, and host-owned resource hints.
Counter metadata exposes the `engine`, `contextFactory`, and `clock` resources,
with `engine` marked as conditionally required when `predicate` or `expression`
is configured. Logger metadata exposes `clock` and the dynamic
`attribute:{name}` selector resource pattern used by `attributeSelectors`.
Metrics metadata exposes `sizeSelector` and `clock`. Host-owned resource
metadata also includes key-pattern hints for expression engines, context
factories, selectors, and clocks.
The metadata is authored through the shared validated Designer metadata builder
while preserving the same public metadata contracts consumed by hosts.

Expression engines, context factories, selectors, and optional keyed
`TimeProvider` clocks remain host-owned resources; option fields such as
`engine`, `attributeSelectors`, and `sizeSelector` only carry the existing
configuration metadata used by the nodes and factories.
