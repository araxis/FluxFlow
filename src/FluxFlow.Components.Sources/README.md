# FluxFlow.Components.Sources

Standalone deterministic source nodes for FluxFlow. Canonical sources emit
immutable `FlowValue` messages on one normal `Output` plus lifecycle `Events`.
They have no input and no universal `Errors` port.

The package depends on TPL Dataflow through `FluxFlow.Nodes`, but it does not
require Composition, Engine, hosting, reflection, or assembly scanning.

## Canonical Sources

| Node | Output | Purpose |
|------|--------|---------|
| `FlowValueGeneratedSourceNode` | `FlowMessage<FlowValue>` | Emits configured immutable values. |
| `FlowValueSequenceSourceNode` | `FlowMessage<FlowValue>` | Emits deterministic sequence objects. |

Both nodes mint a fresh message identity for every emitted value. They start
once through `StartAsync()`, stop through `Complete()` or disposal, preserve
configured order, and publish started, emitted, completed, and failed lifecycle
events. A pre-canceled start does not consume the one-start state.

Configuration errors fail construction. An unexpected source-loop failure
faults `Completion` and `Output`; hosts surface that fault through the canonical
runtime system streams. There is no per-input expected failure because source
nodes have no input operation.

## Generated Values

```csharp
var first = FlowValue.FromObject(new Dictionary<string, FlowValue>
{
    ["id"] = FlowValue.From("A-100"),
    ["total"] = FlowValue.From(125)
});

await using var source = new FlowValueGeneratedSourceNode(
    new FlowValueGeneratedSourceOptions
    {
        Name = "orders",
        Loop = true,
        MaxItems = 5,
        IntervalMilliseconds = 100,
        BoundedCapacity = 128
    },
    [first, FlowValue.From("complete")]);

source.Output.LinkTo(downstream);
await source.StartAsync();
```

`Loop = true` requires `MaxItems`. Without looping, `MaxItems` can cap the
configured list. Missing or empty items complete without output.

## Sequence Values

```csharp
await using var source = new FlowValueSequenceSourceNode(
    new SequenceSourceOptions
    {
        Name = "numbers",
        Start = 10,
        Step = 5,
        Count = 3,
        BoundedCapacity = 128
    });

source.Output.LinkTo(downstream);
await source.StartAsync();
```

Each sequence output is a `FlowValue` object with `name`, `sequence`, `value`,
`start`, `step`, and `timestamp` properties. The timestamp comes from the
configured `TimeProvider`.

## Timing And Capacity

Both canonical nodes accept an optional `TimeProvider` and honor
`InitialDelayMilliseconds` and `IntervalMilliseconds`. Tests can inject a fake
clock and advance delays deterministically.

`BoundedCapacity` bounds the source broadcast block. Source loops await output
acceptance. Broadcast output remains a live fan-out surface rather than durable
storage; use a durable component when replay or guaranteed persistence is
required.

## Typed Compatibility

Released direct-use nodes remain available unchanged:

- `GeneratedSourceNode<TOutput>` emits `FlowMessage<TOutput>`.
- `SequenceSourceNode` emits `FlowMessage<SourceSequenceItem>`.

Those types retain their released `Output`, `Errors`, and `Events` surfaces.
They are compatibility APIs for code-authored typed pipelines; canonical
workflow definitions use the FlowValue nodes.

## Composition

Install `FluxFlow.Components.Sources.Composition` for canonical factories,
Designer metadata, flat JSON item decoding, and optional host-owned keyed
clocks:

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterGeneratedSource()
        .RegisterSequenceSource());
```

The composition adapter owns neither clock lifetime nor source output storage.
