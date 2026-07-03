# FluxFlow.Components.State.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone state
reducer node from `FluxFlow.Components.State`.

This package does not scan assemblies, resolve CLR types from strings, create
reducer registries, or own expression engines. Hosts register the state reducer
factory explicitly and provide a keyed `IFlowExpressionEngine`; they may also
provide an optional keyed `TimeProvider`.

## Registration

```csharp
services
    .AddKeyedSingleton<IFlowExpressionEngine>("state", expressionEngine)
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterStateReducer());
```

## Node Types

| Type | Node | Ports |
|------|------|-------|
| `state.reducer` | `StateReducerNode` | `Input`, `Output` |

The factory exposes `Events` and `Errors`. `engine` is a required keyed
`IFlowExpressionEngine` resource. `clock` is an optional keyed `TimeProvider`
resource for deterministic result, event, and error timestamps.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "state": {
              "type": "state.reducer",
              "resources": {
                "engine": "state",
                "clock": "fixed"
              },
              "configuration": {
                "reducer": "count",
                "keyExpression": "topic-key",
                "initialState": 0,
                "maxKeys": 1024,
                "boundedCapacity": 128,
                "expressionName": "topic counter"
              }
            }
          },
          "links": []
        }
      }
    }
  }
}
```

The node binds the existing `StateReducerOptions` shape from composition
configuration. `StateReducerOptions.Engine` remains configuration metadata; the
composition adapter resolves the expression engine from the `engine` resource.

## Design Metadata

`StateComponentDesignMetadataProvider` exposes neutral Designer metadata for
`state.reducer` so hosts can build palettes, editors, validation hints, or
documentation without copying package descriptors. The metadata describes the
existing `StateReducerOptions` configuration surface, option grouping/editor
hints, fixed `Input`/`Output` ports, and host-owned resource picker hints for
the required keyed expression engine and optional keyed `TimeProvider`. Those
resources stay host-owned; the `engine` option remains only diagnostic/config
metadata and is not used for DI selection.
The metadata is authored through the shared validated Designer metadata builder
while preserving the same public metadata contracts consumed by hosts.
