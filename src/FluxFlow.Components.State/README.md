# FluxFlow.Components.State

Reusable state components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `state.reducer` | `Input` -> `Output`, `Errors` | Keeps per-key state and updates it with a reducer expression. |

## Reducer

```json
{
  "type": "state.reducer",
  "name": "topicState",
  "keyExpression": "topic",
  "initialState": { "count": 0 },
  "reducer": "update-topic-state",
  "engine": "sample",
  "boundedCapacity": 128,
  "maxKeys": 1024
}
```

`state.reducer` consumes `StateReducerInput` and emits
`StateReducerResult`. The reducer expression receives variables for `key`,
`input`, `value`, `state`, `previousState`, `initialState`, `version`,
`operation`, and any per-message `Variables`.

If `keyExpression` is set, it resolves the state key from the same expression
context. Otherwise the node uses `StateReducerInput.Key`.

## Operations

`StateReducerInput.Operation` defaults to `Reduce`.

- `Reduce`: evaluate the reducer and store the new state.
- `Reset`: store the request `InitialState` or node `initialState`.
- `Clear`: remove the state for the key and emit a result with `NewState` null.

Reducer failures are emitted through `Errors` and later messages continue.
State updates are serial, so each key observes deterministic ordered changes.

## Registration

```csharp
registry.RegisterStateComponents(options =>
    options.UseExpressionEngine(myExpressionEngine));
```
