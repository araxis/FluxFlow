# FluxFlow.Components.State

Standalone keyed typed state reduction.

`StateReducerNode<T>` accepts `StateReducerInput<T>` and emits
`StateReducerResult<T>`. Input includes key, optional value/initial state,
immutable variables, and Reduce/Reset/Clear operation. Result includes previous
state, input, new state, operation, version, and update time.

Mutation is serialized per key. Invalid input, key limits, or reducer evaluation
failure becomes `FlowError` on Output. `JsonStateReducerNode` is the explicit
schema-less JSON specialization. The expression engine and clock are supplied
by the host; the node owns only its in-memory keyed state.

## Composition

Install `FluxFlow.Components.State.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
