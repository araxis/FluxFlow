# FluxFlow.Components.Timers.Composition

Composition registration and Designer metadata for canonical FlowValue timer
sources and FlowResult temporal transforms. Canonical descriptors expose
Events and no universal Errors port.

This package does not scan assemblies, resolve CLR types from strings, own
clock lifetime, add durable scheduling, convert time-zone ids, or depend on
Engine.

## Canonical Registration

```csharp
services.AddKeyedSingleton<TimeProvider>(
    "Resources.System.Clock",
    timeProvider);

registry
    .RegisterTimerInterval()
    .RegisterTimerSchedule()
    .RegisterTimerDelay()
    .RegisterTimerThrottle()
    .RegisterTimerDebounce();
```

| Type | Node | Input | Output | Optional resource |
|------|------|-------|--------|-------------------|
| `timer.interval` | `FlowValueTimerIntervalNode` | none | `FlowValue` | `clock` |
| `timer.schedule` | `FlowValueTimerScheduleNode` | none | `FlowValue` | `clock` |
| `timer.delay` | `FlowValueTimerDelayNode` | `FlowValue` | `FlowResult<FlowValue>` | `clock` |
| `timer.throttle` | `FlowValueTimerThrottleNode` | `FlowValue` | `FlowResult<FlowValue>` | `clock` |
| `timer.debounce` | `FlowValueTimerDebounceNode` | `FlowValue` | `FlowResult<FlowValue>` | `clock` |

The runtime starts Interval and Schedule through `IFlowSource`. Invalid options
fail activation. Delay and Throttle emit one result per accepted input.
Debounce intentionally emits no result for values superseded within the quiet
window.

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
    "Main": {
      "Poll": {
        "Type": "timer.interval",
        "clock": "Resources.System.Clock",
        "name": "poll",
        "interval": "00:00:01",
        "emitImmediately": true,
        "maxTicks": 10,
        "boundedCapacity": 128,
        "Output": "Hold.Input"
      },
      "Hold": {
        "Type": "timer.delay",
        "clock": "Resources.System.Clock",
        "name": "hold",
        "delay": "00:00:00.250",
        "boundedCapacity": 128,
        "Output": ["Audit.Input", "Continue.Input"]
      }
    }
  }
}
```

Settings, resource addresses, and links are flat. The sample links require the
target inputs to accept the exact source payload type; the runtime does not add
implicit mappers. A link condition can select success or error result variants.

## Host-Owned Clock

`clock` is optional and resolves an exact keyed `TimeProvider` address. Without
it, nodes use `TimeProvider.System`. The host owns the selected service, its
lifetime, and disposal.

Schedule Composition binds `TimerScheduleSettings` and uses its UTC default.
Designer metadata explicitly reports `timeZone` as omitted because this adapter
does not add string-to-`TimeZoneInfo` conversion.

## Typed Compatibility

Code-authored hosts can retain released typed contracts explicitly:

```csharp
registry
    .RegisterTimerIntervalTicks("timer.interval.tick")
    .RegisterTimerScheduleTicks("timer.schedule.tick")
    .RegisterTimerDelay<OrderMessage>("timer.delay.order")
    .RegisterTimerThrottle<OrderMessage>("timer.throttle.order")
    .RegisterTimerDebounce<OrderMessage>("timer.debounce.order");
```

Use distinct node type names when typed and canonical registrations share a
registry. Typed nodes retain their released error ports and behavior.

## Design Metadata

`TimersComponentDesignMetadataProvider` describes canonical fixed ports,
timing/runtime option sections, and the optional host-owned clock picker.
Metadata is descriptive: hosts own palettes, inspectors, validation UI,
resource selection, activation, persistence, and runtime status display.
