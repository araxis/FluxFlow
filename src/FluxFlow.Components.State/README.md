# FluxFlow.Components.State

A standalone keyed state-reducer node for FluxFlow. It depends only on
`FluxFlow.Nodes` and `FluxFlow.Mapping` — no engine, registry, or runtime. You
`new` the node and `LinkTo` the next one.

## Node

| Node | Shape | Purpose |
|------|-------|---------|
| `StateReducerNode` | `Input` -> `Output`, `Errors`, `Events` | Keeps per-key state and updates it with a reducer expression. |

Every message travels as a `FlowMessage<T>` envelope. The updated state snapshot
is broadcast on `Output` as `FlowMessage<StateReducerResult>` carrying the same
correlation id as the input. State updates are serial, so each key observes
deterministic, ordered changes.

```csharp
await using var node = new StateReducerNode(
    new StateReducerOptions
    {
        KeyExpression = "topic",
        InitialState = new { count = 0 },
        Reducer = "update-topic-state",
        BoundedCapacity = 128,
        MaxKeys = 1024
    },
    myExpressionEngine);

node.Output.LinkTo(resultSink, new DataflowLinkOptions { PropagateCompletion = false });

await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput { Key = "a", Input = payload }));
```

The reducer (and optional key) expression is compiled once at construction via
`IFlowExpressionEngine.Compile<T>(...)`, so parsing happens there rather than per
message. `StateReducerNode` consumes `StateReducerInput` and emits
`StateReducerResult`. The reducer expression receives variables for `key`,
`input`, `value`, `state`, `previousState`, `initialState`, `version`,
`operation`, and any per-message `Variables`.

If `KeyExpression` is set, it resolves the state key from the same expression
context. Otherwise the node uses `StateReducerInput.Key`.

## Operations

`StateReducerInput.Operation` defaults to `Reduce`.

- `Reduce`: evaluate the reducer and store the new state.
- `Reset`: store the request `InitialState` or node `InitialState`.
- `Clear`: remove the state for the key and emit a result with `NewState` null.

## Behavior

Reducer, key-evaluation, and key-limit failures emit a `FlowError` on `Errors`
(carrying the input's correlation id and a `Code` from `StateErrorCodes`) and the
node keeps processing later messages. Per-operation notes
(`state.reducer.updated`/`reset`/`cleared`), reducer failures, and key-limit
warnings flow on the `Events` port (also carrying the correlation id where one is
available).

## Runtime timing

`StateReducerResult.UpdatedAt` uses the node's clock (default
`TimeProvider.System`). Provide a deterministic clock for tests:

```csharp
new StateReducerNode(options, myExpressionEngine, clock: new FakeTimeProvider(timestamp));
```

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
