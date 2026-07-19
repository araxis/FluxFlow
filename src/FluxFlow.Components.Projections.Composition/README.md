# FluxFlow.Components.Projections.Composition

Composition registration and Designer metadata for the canonical
`event.projection` component. It consumes typed domain events and emits one
normal `FlowResult<EventProjectionSnapshot>` output.

This package does not scan assemblies, create projection stores, own clocks,
or add renderer behavior.

## Registration

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterEventProjection());
```

| Type | Resources | Input | Output |
|------|-----------|-------|--------|
| `event.projection` | optional `clock` | `ProjectionEvent` | `FlowResult<EventProjectionSnapshot>` |

The descriptor exposes Events and no universal Errors port. Matching and final
snapshots are successful variants; expected projection failures are normal
error variants on Output.

## Flat Definition

```json
{
  "Resources": {
    "System": {
      "Clock": {
        "Type": "host.clock"
      }
    }
  },
  "Workflows": {
    "Operations": {
      "ProjectFailures": {
        "Type": "event.projection",
        "clock": "Resources.System.Clock",
        "name": "failed-operations",
        "rateWindowSeconds": 60,
        "emitEveryMatch": true,
        "emitFinalSnapshot": true,
        "maxPreviewChars": 256,
        "boundedCapacity": 128,
        "filter": {
          "typePrefix": "operation.",
          "status": "failed",
          "subjectPrefix": "orders/",
          "attributes": {
            "tenant": "north"
          }
        },
        "Input": "SystemEvents.Output"
      }
    }
  }
}
```

Component settings, resource references, and port links are flat. Resource
addresses and component/port names are exact and case-sensitive. A configured
final snapshot is emitted automatically after accepted input drains during
normal composition completion.

## Design Metadata

`ProjectionsComponentDesignMetadataProvider` describes the typed event input,
normal result output, option sections/editor hints, and optional host-owned
clock picker using an exact `Resources.{name}` address. The metadata is
descriptive only; hosts own palette and inspector rendering, resource selection,
validation UI, activation, and persistence.

The runtime package still contains the released direct-result projection node
for code-authored compatibility. Composition `2.x` registers only the canonical
fixed contract; install Composition `1.x` for an existing legacy definition.
