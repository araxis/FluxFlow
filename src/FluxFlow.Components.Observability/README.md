# FluxFlow.Components.Observability

Standalone observability nodes for FluxFlow. The canonical nodes consume
immutable `FlowValue` data and represent complete, rejected, partial, and failed
outcomes through one normal `FlowResult<T>` output. No Composition or Engine
package is required.

## Canonical Nodes

| Node | Input | Output |
|------|-------|--------|
| `FlowValueCounterNode` | `FlowValue` | `FlowResult<FlowCounterSnapshot>` |
| `FlowValueLoggerNode` | `FlowValue` | `FlowResult<FlowValueLogEntry>` |
| `FlowValueMetricsNode` | `FlowValue` | `FlowResult<FlowMetricSnapshot>` |

Each node also exposes Events for lifecycle and observation diagnostics. None
has a universal Errors port.

```csharp
await using var node = new FlowValueCounterNode(
    new FlowValueCounterOptions
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

## Result Contracts

Counter emits `counter-snapshot` when an input is counted and
`counter-rejected` when its predicate returns false. Both are successful
results carrying the current snapshot, so every accepted input remains
traceable. Predicate evaluation failures use `counter-failed` with a stable
`observability.counter_predicate_failed` error.

Logger emits `log-entry` with a `FlowValueLogEntry`. Attributes are one
immutable FlowValue object selected directly from the input. If one or more
selectors fail, Logger emits exactly one `log-entry-partial` error result that
carries the usable entry without failed attributes. It does not emit a second
success for the same input.

Metrics emits `metric-snapshot` with count, rate, timestamp, and optional size
state. If the FlowValue size selector fails, the input still updates count/rate
state and one `metric-snapshot-partial` error result carries that snapshot.

Missing inputs and unexpected evaluation failures are normal error results.
Later accepted inputs continue. Every result preserves correlation, trace,
causation, and headers through `FlowMessage<T>.With(...)`.

## FlowValue Selectors

Logger attributes and Metrics size use `IObservabilityFlowValueSelector`:

```csharp
public sealed class SizeSelector : IObservabilityFlowValueSelector
{
    public FlowValue Select(FlowValue input, ObservabilityNodeContext context)
        => input.GetObject()["size"];
}
```

Selectors return `FlowValue` directly. Metrics accepts numeric values and also
uses string, binary, array, or object length/count as size. No object conversion
or serialization round trip is required.

## Lifecycle

Canonical nodes process one message at a time in acceptance order. `Complete()`
drains accepted input, `Fault(exception)` remains the unexpected Dataflow fault
surface, and `DisposeAsync()` completes and drains the node. Output is broadcast
and may be linked to multiple downstream consumers.

## Generic Compatibility

`FlowCounterNode<TInput>`, `FlowLoggerNode<TInput>`, and
`FlowMetricsNode<TInput>` remain available with their released option records,
object selectors, direct Outputs, Errors ports, Events, and runtime behavior.
They are compatibility surfaces for existing code-authored workflows. No
implicit conversion exists between direct outputs and canonical FlowResult
links.

## Composition

Use `FluxFlow.Components.Observability.Composition` when a Composition host
should register the canonical fixed factories and Designer metadata. Hosts own
expression engines, mapping contexts, FlowValue selectors, clocks, and all
logging/metrics sinks.
