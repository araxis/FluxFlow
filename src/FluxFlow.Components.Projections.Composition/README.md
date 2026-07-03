# FluxFlow.Components.Projections.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone event
projection node from `FluxFlow.Components.Projections`.

This package does not scan assemblies, resolve CLR types from strings, create
projection stores, or add UI adapters. Hosts register the event projection
factory explicitly and may provide an optional keyed `TimeProvider`.

## Registration

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterEventProjection());
```

## Node Types

| Type | Node | Ports |
|------|------|-------|
| `event.projection` | `EventProjectionNode` | `Input`, `Output` |

The factory exposes `Events` and `Errors`. `clock` is an optional keyed
`TimeProvider` resource for deterministic snapshot, event, and error timestamps.
The request/result CLR types are fixed to `ProjectionEvent` and
`EventProjectionSnapshot`.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "projection": {
              "type": "event.projection",
              "resources": {
                "clock": "fixed"
              },
              "configuration": {
                "name": "failed-operations",
                "rateWindowSeconds": 60,
                "emitEveryMatch": true,
                "emitFinalSnapshot": false,
                "maxPreviewChars": 256,
                "boundedCapacity": 128,
                "filter": {
                  "typePrefix": "operation.",
                  "status": "failed",
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

The node binds the existing `EventProjectionOptions` shape from composition
configuration.

## Design Metadata

`ProjectionsComponentDesignMetadataProvider` exposes neutral Designer metadata
for `event.projection` so hosts can build palettes, editors, validation hints,
or documentation without copying package descriptors. The metadata describes the
existing event projection option record, option grouping/editor hints, fixed
ports, and optional `clock` resource picker hint. Optional keyed `TimeProvider`
clocks remain host-owned resources with a key-pattern hint and are not modeled
as editable node options.
The metadata is authored through the shared validated Designer metadata builder
while preserving the same public metadata contracts consumed by hosts.

`EmitFinalSnapshot` remains a direct node lifecycle feature in v1. Composition
runtime stop uses normal node completion; callers that need a final snapshot
should use `EventProjectionNode.CompleteWithFinalSnapshotAsync()` directly until
a composition lifecycle hook is added.
