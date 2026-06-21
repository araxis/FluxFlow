# FluxFlow.Components.Expectations.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone event
expectation node from `FluxFlow.Components.Expectations`.

This package does not scan assemblies, resolve CLR types from strings, create
scenario runners, or add lifecycle hooks. Hosts register the event expectation
factory explicitly and may provide an optional keyed `TimeProvider`.

## Registration

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterEventExpectation());
```

## Node Types

| Type | Node | Ports |
|------|------|-------|
| `event.expectation` | `EventExpectationNode` | `Input`, `Output` |

The factory exposes `Events` and `Errors`. `clock` is an optional keyed
`TimeProvider` resource for deterministic timeout, result, event, and error
timestamps. The request/result CLR types are fixed to `ProjectionEvent` and
`EventExpectationResult`.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "expectation": {
              "type": "event.expectation",
              "resources": {
                "clock": "fixed"
              },
              "configuration": {
                "kind": 0,
                "name": "order-completed",
                "timeoutMilliseconds": 5000,
                "maxObservedEvents": 10,
                "maxPreviewChars": 256,
                "boundedCapacity": 128,
                "filter": {
                  "type": "operation.completed",
                  "status": "ok",
                  "subjectPrefix": "orders/",
                  "attributes": {
                    "tenant": "north"
                  }
                }
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

The node binds the existing `EventExpectationOptions` shape from composition
configuration. `kind` follows the existing enum values: `0` for expect and `1`
for guard.

`CompleteWithResultAsync()` remains a direct node lifecycle feature in v1.
Composition runtime stop uses normal node completion; callers that need a
completion-result flush should use `EventExpectationNode.CompleteWithResultAsync()`
directly until a composition lifecycle hook is added.
