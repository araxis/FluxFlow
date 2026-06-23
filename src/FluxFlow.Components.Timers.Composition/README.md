# FluxFlow.Components.Timers.Composition

Optional `FluxFlow.Composition` registration helpers for standalone timer nodes
from `FluxFlow.Components.Timers`.

This package does not scan assemblies, resolve CLR types from strings, add hot
reload behavior, or convert schedule time zone ids. Hosts register closed
generic transform node types explicitly and provide optional keyed
`TimeProvider` services.

## Registration

```csharp
services.AddKeyedSingleton<TimeProvider>("fixed", timeProvider);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterTimerInterval()
        .RegisterTimerSchedule()
        .RegisterTimerDelay<OrderMessage>()
        .RegisterTimerThrottle<OrderMessage>()
        .RegisterTimerDebounce<OrderMessage>());
```

Use custom node type names when a host needs more than one input shape:

```csharp
registry
    .RegisterTimerDelay<OrderMessage>("timer.delay.order")
    .RegisterTimerDebounce<HttpMessage>("timer.debounce.http");
```

## Node Types

| Type | Node | Optional resource | Ports |
|------|------|-------------------|-------|
| `timer.interval` | `TimerIntervalNode` | `clock` | `Output` |
| `timer.schedule` | `TimerScheduleNode` | `clock` | `Output` |
| `timer.delay` | `TimerDelayNode<TInput>` | `clock` | `Input`, `Output` |
| `timer.throttle` | `TimerThrottleNode<TInput>` | `clock` | `Input`, `Output` |
| `timer.debounce` | `TimerDebounceNode<TInput>` | `clock` | `Input`, `Output` |

The composition runtime starts interval and schedule sources through the normal
`IFlowSource` lifecycle. Transform nodes preserve the input correlation id when
they re-emit the original payload.

## Design Metadata

`TimersComponentDesignMetadataProvider` exposes neutral Designer metadata for the
five timer composition nodes. Hosts can add it to a
`ComponentDesignMetadataCatalog` to populate palettes, editors, validation
views, or generated documentation.

The provider describes node options and ports only. The optional `clock`
resource remains a host-owned composition resource and is not exposed as an
editable node option. Schedule metadata covers cron/default UTC composition
behavior; this package still does not add time zone id conversion.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "poll": {
              "type": "timer.interval",
              "resources": {
                "clock": "fixed"
              },
              "configuration": {
                "name": "poll",
                "interval": "00:00:01",
                "emitImmediately": true,
                "maxTicks": 10,
                "boundedCapacity": 128
              }
            },
            "rate-limit": {
              "type": "timer.throttle",
              "configuration": {
                "name": "rate-limit",
                "interval": "00:00:00.100",
                "emitFirstImmediately": true,
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

Timer settings bind to the existing settings records. `timer.schedule` uses the
existing `TimerScheduleSettings` shape; no additional time zone id conversion is
added by this adapter.
