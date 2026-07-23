# FluxFlow.Components.Sources

Standalone deterministic sources over immutable workflow values.
`GeneratedSourceNode` emits configured values and `SequenceSourceNode` emits
numeric sequence objects. Both publish one `FlowMessage<FlowValue>` Output plus
lifecycle Events and have no universal Errors port.

The package uses TPL Dataflow through `FluxFlow.Nodes`, but does not require
Composition, Engine, hosting, reflection, or assembly scanning. An optional
`TimeProvider` controls scheduling and diagnostic timestamps.

## Nodes

| Node | Output | Purpose |
|------|--------|---------|
| `GeneratedSourceNode` | `FlowValue` | Emits configured immutable values. |
| `SequenceSourceNode` | `FlowValue` | Emits deterministic sequence objects. |

Both nodes start once through `StartAsync()`, stop through `Complete()` or
disposal, preserve configured order, and mint fresh message identity for every
emission. A pre-canceled start does not consume the one-start state.

Configuration errors fail construction. Unexpected source-loop failures fault
`Completion` and `Output` and publish a failed Event. Source nodes have no
per-input expected-failure case because they accept no input operations.

## Generated Values

```csharp
var first = FlowValue.FromObject(new Dictionary<string, FlowValue>
{
    ["id"] = FlowValue.From("A-100"),
    ["total"] = FlowValue.From(125)
});

await using var source = new GeneratedSourceNode(
    new GeneratedSourceOptions
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
await using var source = new SequenceSourceNode(
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

Each sequence output is an immutable object with `name`, `sequence`, `value`,
`start`, `step`, and `timestamp` fields. The timestamp comes from the configured
clock.

## Timing And Fan-Out

Both nodes honor `InitialDelayMilliseconds` and `IntervalMilliseconds`.
`BoundedCapacity` bounds the live broadcast output, and source loops await
output acceptance. Immutable payloads are shared safely across fan-out targets
without deep cloning.

Broadcast output is not durable storage. Use a durable component when replay or
guaranteed persistence is required.

## Migration From 4.x

The concise node names now own the canonical contracts. Replace
`FlowValueGeneratedSourceNode` with `GeneratedSourceNode` and
`FlowValueGeneratedSourceOptions` with `GeneratedSourceOptions`. Replace
`FlowValueSequenceSourceNode` with `SequenceSourceNode`.

The generic `GeneratedSourceNode<TOutput>`, typed `SourceSequenceItem`, numeric
`SourceErrorCodes`, and inherited Errors surfaces were removed. Convert typed
values to immutable `FlowValue` at the application boundary and observe
unexpected runtime faults through `Completion` and Events.

## Composition

Install `FluxFlow.Components.Sources.Composition` for canonical factories,
Designer metadata, flat scalar-or-array item decoding, and optional host-owned
clocks. The adapter owns neither clock lifetime nor durable output storage.
