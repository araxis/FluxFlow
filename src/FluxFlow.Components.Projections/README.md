# FluxFlow.Components.Projections

Standalone event projection and rolling-rate snapshots.

`EventProjectionNode` accepts `ProjectionEvent` and emits
`EventProjectionSnapshot`. Filtering, snapshot folding, rolling rate,
emit-every-match, preview limits, and final completion snapshot behavior remain
inside the node.

Snapshots are normal values. Invalid events or projection failures become
`FlowError` on Output. Events supplies diagnostics and an optional host-owned
clock supplies deterministic time.

## Composition

Install `FluxFlow.Components.Projections.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
