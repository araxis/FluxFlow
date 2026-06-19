# FluxFlow.Components.Sources

Standalone deterministic source nodes for FluxFlow, built on the
[FluxFlow.Nodes](../FluxFlow.Nodes) kit. Every node is a self-contained TPL
Dataflow processor: `new` it, `LinkTo` the next node, `StartAsync()` it, and run
it — no engine, registry, or runtime required. All timing is driven by an
injected `TimeProvider`, so tests stay deterministic with a `FakeTimeProvider`.

## Nodes

| Node | Kind | Shape | Purpose |
|------|------|-------|---------|
| `GeneratedSourceNode<T>` | source | `Output` | Emits a configured list of `T` values as `FlowMessage<T>`. |
| `SequenceSourceNode` | source | `Output` | Emits a deterministic numeric `FlowMessage<SourceSequenceItem>` sequence. |

Both nodes are zero-input emitters. Call `StartAsync()` and they produce until
their configured count is reached (source complete) or they are stopped via
`Complete()`/`DisposeAsync()`. Every emitted item is a `FlowMessage<T>` envelope
with a fresh `CorrelationId`. Lifecycle notes (started, emitted, completed,
failed) surface on the `Events` port using `SourceDiagnosticNames`; failures
surface a `FlowError` on the `Errors` port.

## Generated

```csharp
await using var node = new GeneratedSourceNode<AppMessage>(
    new GeneratedSourceOptions
    {
        Name = "feed",
        OutputType = "app.message",
        Loop = true,
        MaxItems = 5,
        IntervalMilliseconds = 100
    },
    items: new[]
    {
        new AppMessage("A-100", "alpha"),
        new AppMessage("A-101", "beta")
    });
node.Output.LinkTo(downstream);
await node.StartAsync();
```

The host materializes (and, if it started from JSON, deserializes) the items it
wants to emit and hands them in directly — the package no longer resolves output
types from strings or deserializes JSON. `MaxItems` caps the emitted count, and
`Loop = true` (which requires `MaxItems`) cycles through the items until that cap
is reached.

## Sequence

```csharp
await using var node = new SequenceSourceNode(new SequenceSourceOptions
{
    Name = "numbers",
    Start = 10,
    Step = 5,
    Count = 3,
    InitialDelayMilliseconds = 0,
    IntervalMilliseconds = 0
});
node.Output.LinkTo(downstream);
await node.StartAsync();
```

`SequenceSourceNode` emits `SourceSequenceItem` values with a name, sequence
number, computed value (`Start + Step * index`), the start/step inputs, and a
timestamp from the injected clock.

## Deterministic time

Pass a `TimeProvider` to either node's constructor (it defaults to
`TimeProvider.System`). Both honor `InitialDelayMilliseconds` and
`IntervalMilliseconds` off that clock, so tests can supply a
[`FakeTimeProvider`](https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing)
and advance it to release each delay without real-time waits, and item
timestamps come from the configured clock's timeline.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
