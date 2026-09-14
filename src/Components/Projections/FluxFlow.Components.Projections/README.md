# FluxFlow.Components.Projections

Standalone event projection and rolling-rate snapshots.

Event deserialization lives locally in `EventProjectionNode`. Composition reads
`ProjectionInputShapes.Event` from the Contracts namespace independently of parsing.

Migration: the public `ProjectionEventMaterializer` class has been removed.
Replace its `InputShape` access with `ProjectionInputShapes.Event`. Direct
`Materialize` callers can deserialize locally with their own error policy, or send
a `FlowMessage` to the node for the existing rejection-output behavior. The
Expectations component also parses its events locally. Constructors and ports
are unchanged.

`EventProjectionNode` accepts `ProjectionEvent` and emits
`EventProjectionSnapshot`. Filtering, snapshot folding, rolling rate,
emit-every-match, preview limits, and final completion snapshot behavior remain
inside the node.

Processing keeps the original `FlowMessage` beside the parsed `ProjectionEvent`.
Snapshots emit directly with `message.With(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))`; errors use
`message.WithError(error)`. There are no intermediate typed message envelopes.
Final snapshots derive from the last matched input, or create an independent
message when nothing matched. The node retains one last matched envelope,
including its original JSON payload, instead of a typed event envelope.

Snapshots are normal values. Invalid events or projection failures become
`FlowError` on Output. Events supplies diagnostics and an optional host-owned
clock supplies deterministic time. `ProjectionEvent`, `EventFilter`,
`EventSummary`, and `EventProjectionSnapshot` copy incoming attribute maps into
read-only ordinal snapshots, so later source mutations cannot change an event,
filter, or emitted snapshot.

## Composition

Install `FluxFlow.Components.Projections.Composition` for optional runtime registration and definition. This runtime package remains free of Composition, Designer, and Engine dependencies.
