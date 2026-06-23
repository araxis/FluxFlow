# FluxFlow.Components.Projections

A standalone event-projection node for FluxFlow, built on the `FluxFlow.Nodes` kit.
No engine required: `new` the node, post events, link the output.

## Node

| Node | Shape | Purpose |
|------|-------|---------|
| `EventProjectionNode` | `Input` -> `Output`, `Errors`, `Events` | Folds matching events into count, latest-event, and rolling-rate snapshots. |

`EventProjectionNode` is a `FlowNode<ProjectionEvent, EventProjectionSnapshot>`. Post a
`FlowMessage<ProjectionEvent>` to `Input`; the node broadcasts a
`FlowMessage<EventProjectionSnapshot>` on `Output` carrying the triggering event's
correlation id. Errors surface on `Errors` (`FlowError`) and diagnostics on `Events`
(`FlowEvent`).

## Example

```csharp
var node = new EventProjectionNode(new EventProjectionOptions
{
    Name = "failed-operations",
    RateWindowSeconds = 60,
    MaxPreviewChars = 256,
    Filter = new EventFilter
    {
        TypePrefix = "operation.",
        Status = "failed",
        SubjectPrefix = "orders/",
        Attributes = new Dictionary<string, string> { ["tenant"] = "north" }
    }
});

var snapshots = new BufferBlock<FlowMessage<EventProjectionSnapshot>>();
node.Output.LinkTo(snapshots, new DataflowLinkOptions { PropagateCompletion = true });

await node.Input.SendAsync(FlowMessage.Create(new ProjectionEvent
{
    Timestamp = DateTimeOffset.UtcNow,
    Type = "operation.completed",
    Source = "orders",
    Subject = "orders/42",
    Status = "failed",
    Attributes = new Dictionary<string, string> { ["tenant"] = "north" }
}));
```

The snapshot includes:

- observed event count
- matched event count
- rolling current rate
- first and last matched timestamps
- latest matching event summary
- latest payload preview with a configured length limit

## Filters

`EventFilter` supports:

- event type or type prefix
- subject prefix
- channel prefix
- excluded subject prefix
- excluded channel prefix
- status
- source
- source node id
- component id through an event attribute named `componentId`
- attribute key/value pairs
- event timestamp range

Filters use ordinal string comparison.

## Timing

Pass a `TimeProvider` to the constructor for deterministic snapshot timestamps in
tests or hosts:

```csharp
var node = new EventProjectionNode(options, timeProvider);
```

Event rate uses matching event timestamps. Snapshot timestamps use the supplied
`TimeProvider` (defaults to `TimeProvider.System`).

## Final snapshot

With `EmitFinalSnapshot = true` (and typically `EmitEveryMatch = false`) the node
emits a single closing snapshot when the stream ends. Drain and close it via
`await node.CompleteWithFinalSnapshotAsync()` instead of `Complete()`; the flush rides
the ordered input pump so it lands after every event already posted.

When the node is hosted through `FluxFlow.Composition`, v1 runtime stop uses
normal node completion. Use the direct node API when a closing final snapshot is
required until composition grows an explicit final-flush lifecycle hook.

## Composition

Building a workflow, reading config, creating nodes, and linking them is a
separate concern from the node. This package is just the standalone node.

Use `FluxFlow.Components.Projections.Composition` when a
`FluxFlow.Composition` host should register the optional `event.projection`
factory:

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterEventProjection());
```

The composition adapter binds `EventProjectionOptions` from node configuration
and can resolve an optional keyed `TimeProvider` resource named `clock`.

The optional composition package also exposes
`ProjectionsComponentDesignMetadataProvider` for neutral Designer metadata over
the `event.projection` composition node type. The standalone Projections package
remains free of Designer, Composition, and Engine dependencies.

## Boundaries

This package has no UI dependency and no host-specific resource assumptions. It
depends only on `FluxFlow.Nodes`. Hosts decide how snapshots are displayed, stored,
tested, or forwarded.
