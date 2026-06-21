# FluxFlow.Components.Observability

Standalone observer nodes for FluxFlow — counter, logger, and metrics. Each depends
only on `FluxFlow.Nodes` (and `FluxFlow.Mapping` for the counter's optional predicate)
— no engine, registry, or runtime. You `new` the node and `LinkTo` the next one.

## Nodes

| Node | Shape | Purpose |
|------|-------|---------|
| `FlowCounterNode<TInput>` | `Input` -> `Output` (`FlowCounterSnapshot`) | Counts accepted inputs and emits counter snapshots. |
| `FlowLoggerNode<TInput>` | `Input` -> `Output` (`FlowLogEntry`) | Emits structured log entries from inputs. |
| `FlowMetricsNode<TInput>` | `Input` -> `Output` (`FlowMetricSnapshot`) | Emits count, rate, timestamp, and optional size snapshots. |

Every message travels as a `FlowMessage<T>` envelope. Each node broadcasts its result
on `Output` carrying the same correlation id as the input; failures surface on
`Errors` (with the input's correlation id and a `Code` from `ObservabilityErrorCodes`)
and diagnostics flow on `Events`. The package emits neutral contracts only — hosts
decide whether those entries or snapshots become app logs, dashboards, files,
telemetry, or test assertions.

## Counter

```csharp
await using var node = new FlowCounterNode<MyMessage>(
    new FlowCounterOptions { InputType = "message", Name = "received", Predicate = "value.Enabled" },
    expressionEngine);

node.Output.LinkTo(snapshotSink, new DataflowLinkOptions { PropagateCompletion = false });
await node.Input.SendAsync(FlowMessage.Create(message));
```

`FlowCounterNode` emits `FlowCounterSnapshot` values with count, rejected count, last
observed timestamp, name, and input type. When a predicate is configured it is
compiled once at construction from the supplied `IFlowExpressionEngine`; inputs the
predicate rejects are not counted (but are tallied in `RejectedCount`). With no
predicate every input is counted and no engine is required. Pass an
`IFlowMapContextFactory<TInput>` to control the variables the predicate sees
(defaults to `input`/`value`).

## Logger

```csharp
await using var node = new FlowLoggerNode<MyMessage>(
    new FlowLoggerOptions
    {
        InputType = "message",
        Level = "Information",
        Category = "workflow",
        MessageTemplate = "Observed {kind} item #{sequence}"
    },
    attributeSelectors: new Dictionary<string, IObservabilityValueSelector<MyMessage>>
    {
        ["kind"] = new KindSelector()
    });
```

`FlowLoggerNode` emits `FlowLogEntry` values. Supply `IObservabilityValueSelector<TInput>`
selectors keyed by attribute name to enrich each entry; an attribute-selector failure
is reported on `Errors`, the offending attribute is skipped, and the entry is still
emitted. An unsupported `Level` throws `InvalidOperationException` at construction.

## Metrics

```csharp
await using var node = new FlowMetricsNode<MyMessage>(
    new FlowMetricsOptions { InputType = "message", Name = "received", SizeSelector = "payloadBytes" },
    sizeSelector: new PayloadSizeSelector());
```

`FlowMetricsNode` emits `FlowMetricSnapshot` values with total count, current rate,
average rate, last observed timestamp, and optional size values. The optional size
selector can return numeric values, strings, byte arrays, or collections; a
size-selector failure is reported on `Errors` and the node keeps processing.

## Runtime timing

Snapshots and log entries use the node's clock for `Timestamp` (default
`TimeProvider.System`). Provide a deterministic clock for tests:

```csharp
new FlowMetricsNode<MyMessage>(options, sizeSelector, clock: new FakeTimeProvider(timestamp));
```

A time provider also makes counter/metrics rate calculations deterministic.

## Composition

Building a workflow, reading config, creating nodes, and linking them is a
separate concern from these observer nodes. This package is just the standalone
nodes.

Use `FluxFlow.Components.Observability.Composition` when a
`FluxFlow.Composition` host should register optional observability factories:

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterCounter<MyMessage>()
        .RegisterLogger<MyMessage>()
        .RegisterMetrics<MyMessage>());
```

The composition adapter binds the existing options records from node
configuration. It resolves host-owned keyed resources for `clock`, counter
`engine` and `contextFactory`, metrics `sizeSelector`, and logger attribute
selectors named as `attribute:{name}`.
