# FluxFlow.Components.Sessions.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone session
recorder, replay, and query nodes from `FluxFlow.Components.Sessions`.

This package does not scan assemblies, create stores, own retention policy, or
configure persistence. Hosts register session node factories explicitly and
provide a keyed `ISessionStore`; they may also provide an optional keyed
`TimeProvider`.

## Registration

```csharp
services.AddKeyedSingleton<ISessionStore>("sessions", sessionStore);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterSessionRecorder()
        .RegisterSessionReplay()
        .RegisterSessionQuery());
```

## Node Types

| Type | Node | Ports |
|------|------|-------|
| `session.recorder` | `SessionRecorderNode` | `Input`, `Output` |
| `session.replay` | `SessionReplayNode` | `Output` |
| `session.query` | `SessionQueryNode` | `Input`, `Output`, `Sessions` |

Each factory exposes `Events` and `Errors`. `store` is a required keyed
`ISessionStore` resource. `clock` is an optional keyed `TimeProvider` resource
for deterministic result, event, error, and replay timing.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "recorder": {
              "type": "session.recorder",
              "resources": {
                "store": "sessions",
                "clock": "fixed"
              },
              "configuration": {
                "sessionId": "run-1",
                "name": "integration run",
                "boundedCapacity": 128
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

The adapter binds the existing session option records from node configuration.
The `Store` option remains configuration metadata; the composition adapter
resolves the concrete `ISessionStore` from the `store` resource.
