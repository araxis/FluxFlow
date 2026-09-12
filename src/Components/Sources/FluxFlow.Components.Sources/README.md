# FluxFlow.Components.Sources

Standalone generated and sequence sources; each is directly an `IFlowComponent`
through `FlowSource`, with no second private source object.

- `GeneratedSourceNode<T>` emits configured T items with optional looping and
  timing. `GeneratedSourceNode` is its `JsonElement` specialization; its optional
  `Create(options, items, clock)` factory accepts one JSON value or an array and
  detaches those items from the caller's document before returning.
- `SequenceSourceNode` emits typed `SequenceItem` values for a numeric sequence.

Both expose their actual Output, Events, Completion, start/stop lifecycle, bounded
internal delivery, and an optional host-owned `TimeProvider`. They have no input.
Generation failures emit diagnostic events and fault Completion. There is no
Errors output. In Core, `Ports.Events` is a separate best-effort diagnostic stream,
not a business output named Events.

Add an instance to an existing workflow and link it to an existing consumer:

```csharp
var source = workflow.Add("sequence", new SequenceSourceNode(
    new SequenceSourceOptions { Start = 2, Step = 2, Count = 5 }));
source.Output.LinkTo(consumer.Input);
```

The application owns the component by default, but never owns the supplied clock.
The source creates a fresh message per emission; Core fanout shares that immutable
message across selected consumers. Generic input lists are shallow-copied at
construction; arbitrary mutable items are not promised a deep constructor snapshot.

## Composition

Install `FluxFlow.Components.Sources.Composition` for optional runtime registration and authoring. Editor metadata is separate: install `FluxFlow.Components.Sources.Designer` and call `AddSourcesDesign()` after runtime registration. This runtime package remains free of Composition, Designer, and Engine dependencies.
