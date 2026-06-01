# State Reducer Component Package

Date: 2026-06-01

## Decision

Add `FluxFlow.Components.State` as a separate component package with one
initial node:

- `state.reducer`

The reducer is the centerpiece. It owns in-memory per-key state inside the node
and uses the existing expression engine abstraction for key resolution and state
updates.

## Contracts

The first slice includes:

- `StateReducerInput`
- `StateReducerResult`
- `StateReducerOperation`

`StateReducerInput` carries a required key, input payload, optional initial
state, per-message variables, and an operation. `Reduce` is the default
operation. `Reset` and `Clear` are handled on the same input stream.

## Behavior

`state.reducer` keeps state per key. On each reduce input it passes the current
state, new input, initial state, version, operation, key, and variables into the
configured reducer expression. The expression result becomes the new stored
state and is emitted with the previous state and version.

The node is serial and deterministic. It bounds key cardinality with `maxKeys`.
Reducer and key-expression failures emit structured `FlowError` values and the
node continues processing later messages.

## Deferred

Persisted state is deferred. This first package owns runtime reducer behavior
only. A future storage-backed package or host adapter can be added after real
consumer usage proves the shape.

Named reducer libraries or script registries are also deferred. The first slice
supports one reducer expression per node.

## Verification

Focused coverage includes:

- ordered per-key state updates
- initial state from node options and requests
- key expression evaluation
- reset and clear operations
- reducer failure continuation
- maximum key limit behavior
- diagnostics
- option validation
- missing expression engine validation
- node registration

Planned release tag:

```text
components-state-v0.1.0-alpha.1
```
