# FluxFlow.Components.State

Standalone keyed state reducers for FluxFlow. The canonical node keeps dynamic
state as immutable `FlowValue` data and emits every expected outcome through one
normal `FlowResult<T>` output. The package does not require Composition or
Engine; construct a node and link its Dataflow ports directly.

## Canonical Node

| Node | Input | Output |
|------|-------|--------|
| `FlowValueStateReducerNode` | `FlowValueStateReducerInput` | `FlowResult<FlowValueStateReducerResult>` |

The node also exposes `Events` for lifecycle and operation diagnostics. It has
no universal Errors port. Expected failures are ordinary result data, so links
can inspect and route them without faulting the workflow.

```csharp
await using var node = new FlowValueStateReducerNode(
    new FlowValueStateReducerOptions
    {
        Reducer = "increment-count",
        InitialState = FlowValue.From(0),
        BoundedCapacity = 128,
        MaxKeys = 1024
    },
    expressionEngine);

node.Output.LinkTo(resultSink);

await node.Input.SendAsync(FlowMessage.Create(
    new FlowValueStateReducerInput
    {
        Key = "orders",
        Input = FlowValue.From(1)
    }));
```

The reducer and optional key expression are compiled once at construction with
`IFlowExpressionEngine.Compile<T>(...)`. Reducers return `FlowValue`; key
expressions return `string`. The expression context contains `key`, `request`,
`input`, `value`, `state`, `previousState`, `initialState`, `version`,
`operation`, and the command's immutable ordinal `Variables`.

## Commands And Results

`FlowValueStateReducerInput.Operation` defaults to `Reduce`:

- `Reduce` evaluates the reducer and stores the returned value.
- `Reset` stores the command `InitialState`, or the node option when absent.
- `Clear` removes the key and emits `FlowValue.Null` as the new state.

Successful results use `updated`, `reset`, or `cleared` kinds.
Each value records the key, previous state, input, new state, operation, version,
and update time. Updates are processed serially and preserve input order.

Invalid messages, invalid keys, key-expression failures, reducer failures, and
key-limit rejections use the `operation-failed` result kind. Their stable
`FlowError.Code` comes from `StateErrorCodeNames`. A normal failure does not
stop later input.

Input messages retain business correlation, trace, causation, headers, and hop
lineage through `FlowMessage<T>.With(...)`. `UpdatedAt` and diagnostic timestamps
use the injected `TimeProvider`, defaulting to `TimeProvider.System`.

## Lifecycle

`Complete()` drains accepted commands before completing Output and Events.
`Fault(exception)` faults the data path for unexpected runtime failures.
`DisposeAsync()` completes and drains the node. Component failures remain local
and do not define host lifetime.

## CLR Boundaries

`FlowValueStateReducerNode` is the single maintained State contract. Hosts with
CLR domain objects convert them explicitly to `FlowValue` at the application
boundary and inspect `FlowResult.Kind`, `IsError`, and `Error.Code` when routing
outcomes. The package does not provide an object-state compatibility node or an
implicit conversion path.

## Composition

Use `FluxFlow.Components.State.Composition` when a Composition host should
register the canonical `state.reduce` factory and Designer metadata. The host
owns keyed expression-engine and clock resources; the component package does
not create or manage them.
