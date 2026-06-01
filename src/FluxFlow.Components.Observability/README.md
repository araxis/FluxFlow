# FluxFlow.Components.Observability

Reusable observer components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `flow.counter` | `Input` -> `Snapshots` | Counts matching inputs and emits counter snapshots. |
| `flow.logger` | `Input` -> `Entries` | Emits structured log entries from inputs. |
| `flow.metrics` | `Input` -> `Snapshots` | Emits count, rate, timestamp, and optional size snapshots. |

The package emits neutral contracts only. Hosts decide whether those entries or
snapshots become app logs, dashboards, files, telemetry, or test assertions.

## Counter

```json
{
  "type": "flow.counter",
  "inputType": "message",
  "name": "received",
  "predicate": "value.Enabled",
  "engine": "default",
  "boundedCapacity": 128
}
```

`flow.counter` emits `FlowCounterSnapshot` values with count, rejected count,
last observed timestamp, name, and input type. When no predicate is configured,
all inputs are counted.

## Logger

```json
{
  "type": "flow.logger",
  "inputType": "message",
  "level": "Information",
  "category": "workflow",
  "messageTemplate": "Observed {kind} item #{sequence}",
  "attributeSelectors": [ "kind", "size" ],
  "boundedCapacity": 128
}
```

`flow.logger` emits `FlowLogEntry` values. Hosts register selector functions for
custom attributes. Built-in selectors `input` and `value` return the original
input.

## Metrics

```json
{
  "type": "flow.metrics",
  "inputType": "message",
  "name": "received",
  "sizeSelector": "payloadBytes",
  "boundedCapacity": 128
}
```

`flow.metrics` emits `FlowMetricSnapshot` values with total count, current rate,
average rate, last observed timestamp, and optional size values. Size selectors
can return numeric values, strings, byte arrays, or collections.

## Registration

```csharp
registry.RegisterObservabilityComponents(options => options
    .RegisterType<MyMessage>("message")
    .UseValueSelector<MyMessage>("kind", (message, _) => message.Kind)
    .UseValueSelector<MyMessage>("payloadBytes", (message, _) => message.Payload.Length));
```

Register an expression engine only when using counter predicates:

```csharp
registry.RegisterObservabilityComponents(options => options
    .UseExpressionEngine(expressionEngine)
    .RegisterType<MyMessage>("message"));
```

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
