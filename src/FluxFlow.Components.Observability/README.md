# FluxFlow.Components.Observability

Standalone Counter, Logger, and Metrics nodes for immutable workflow values.
Expected rejection, selection, and evaluation outcomes use one normal
`FlowResult<T>` Output. No Composition or Engine package is required.

## Nodes

| Node | Input | Output |
|------|-------|--------|
| `FlowCounterNode` | `FlowValue` | `FlowResult<FlowCounterSnapshot>` |
| `FlowLoggerNode` | `FlowValue` | `FlowResult<FlowLogEntry>` |
| `FlowMetricsNode` | `FlowValue` | `FlowResult<FlowMetricSnapshot>` |

Each node also exposes `Events` for lifecycle and observation diagnostics. None
has a universal Errors port.

```csharp
await using var node = new FlowCounterNode(
    new FlowCounterOptions
    {
        Name = "accepted-orders",
        Predicate = "input.status = 'accepted'"
    },
    expressionEngine);

node.Output.LinkTo(results);

await node.Input.SendAsync(FlowMessage.Create(
    FlowValue.FromObject(new Dictionary<string, FlowValue>
    {
        ["id"] = FlowValue.From("order-42"),
        ["status"] = FlowValue.From("accepted")
    })));
```

## Results

Counter emits `counter-snapshot` when an input is counted and
`counter-rejected` when its predicate returns false. Both are successful
results carrying the current snapshot. Predicate evaluation failures use
`counter-failed` and `observability.counter_predicate_failed`.

Logger emits `log-entry` with a `FlowLogEntry`. Input and selected attributes
remain immutable `FlowValue` data. Selector failures produce exactly one
`log-entry-partial` error result carrying the usable entry without failed
attributes.

Metrics emits `metric-snapshot` with count, rate, timestamp, and optional size
state. A size-selector failure still updates count and rate, then emits one
`metric-snapshot-partial` result carrying that snapshot.

Missing inputs and expected evaluation failures are result data. Later inputs
continue, and every result preserves correlation, trace, causation, and headers
through `FlowMessage<T>.With(...)`.

## Selectors

Logger attributes and Metrics size use `IObservabilityValueSelector`:

```csharp
public sealed class SizeSelector : IObservabilityValueSelector
{
    public FlowValue Select(FlowValue input, ObservabilityNodeContext context)
        => input.GetObject()["size"];
}
```

Metrics accepts numeric values and derives size from string, binary, array, or
object length/count. No object conversion or serialization round trip is
required.

## Lifecycle

Nodes process one message at a time in acceptance order. `Complete()` drains
accepted input, `Fault(exception)` remains the unexpected Dataflow fault
surface, and `DisposeAsync()` completes and drains the node. Output is
broadcast and may be linked to multiple consumers.

## Migration To 5.0

- Replace `FlowValueCounterNode`, `FlowValueLoggerNode`, and
  `FlowValueMetricsNode` with the concise node names above.
- Replace the corresponding `FlowValue*Options`, `FlowValueLogEntry`, and
  `IObservabilityFlowValueSelector` names with their concise equivalents.
- Map CLR input to `FlowValue` before the observability boundary instead of
  using generic nodes and object selectors.
- Route failures by `FlowResult.IsError`, `Kind`, or `Error.Code` on Output
  instead of linking a universal Errors stream.
- Provide expression engines, context factories, selectors, and clocks through
  constructor injection; they remain host-owned.

## Composition

Use `FluxFlow.Components.Observability.Composition` for fixed registrations and
Designer metadata. The package does not own expression engines, mapping
contexts, selectors, clocks, logging sinks, metric exporters, or their
lifetimes.
