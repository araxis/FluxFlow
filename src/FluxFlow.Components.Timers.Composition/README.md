# FluxFlow.Components.Timers.Composition

Optional `FluxFlow.Composition` registrations and Designer metadata for the
canonical Timers nodes. The host may provide a keyed `TimeProvider`; this
package does not own the clock or add durable scheduling.

## Registration

```csharp
services.AddKeyedSingleton<TimeProvider>(
    "Resources.System.Clock",
    timeProvider);

var registry = new CompositionNodeRegistry()
    .RegisterTimerInterval()
    .RegisterTimerSchedule()
    .RegisterTimerDelay()
    .RegisterTimerThrottle()
    .RegisterTimerDebounce();
```

| Type | Node | Input | Output |
|------|------|-------|--------|
| `timer.interval` | `TimerIntervalNode` | none | `FlowValue` |
| `timer.schedule` | `TimerScheduleNode` | none | `FlowValue` |
| `timer.delay` | `TimerDelayNode` | `FlowValue` | `FlowResult<FlowValue>` |
| `timer.throttle` | `TimerThrottleNode` | `FlowValue` | `FlowResult<FlowValue>` |
| `timer.debounce` | `TimerDebounceNode` | `FlowValue` | `FlowResult<FlowValue>` |

All descriptors expose Events and no universal Errors surface. Invalid options
fail activation. Delay and Throttle emit one result per accepted input;
Debounce intentionally emits no result for values superseded in the quiet
window.

## Flat Document

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
    "OrderProcessing": {
      "Poll": {
        "Type": "timer.interval",
        "clock": "Resources.System.Clock",
        "interval": "00:00:01",
        "maxTicks": 10,
        "Output": "Hold.Input"
      },
      "Hold": {
        "Type": "timer.delay",
        "clock": "Resources.System.Clock",
        "delay": "00:00:00.250",
        "Output": ["HandleResult.Input", "Audit.Input"]
      },
      "HandleResult": {
        "Type": "timer.result"
      },
      "Audit": {
        "Type": "audit.result"
      }
    }
  }
}
```

`timer.result` and `audit.result` are host example types. Composition does not
insert mappers or serializers. Links must connect exact payload types, and
conditions can route success or failure result variants.

`clock` is optional and resolves an exact canonical resource address such as
`Resources.System.Clock`. Without it, nodes use `TimeProvider.System`. The host
owns the selected service and its disposal.

Schedule composition uses the UTC `TimerScheduleSettings` default. Designer
metadata explicitly reports `timeZone` as omitted because this adapter does
not convert strings to `TimeZoneInfo`.

## Migration From 2.x

Only the fixed canonical registrations remain. Remove
`RegisterTimerIntervalTicks`, `RegisterTimerScheduleTicks`, and generic
`RegisterTimerDelay<T>`, `RegisterTimerThrottle<T>`, and
`RegisterTimerDebounce<T>` calls. Convert workflow boundary values to
`FlowValue` and route expected failures from the normal result Output.

## Design Metadata

`TimersComponentDesignMetadataProvider` describes fixed ports, timing/runtime
options, omitted schedule time-zone conversion, and the optional host-owned
clock picker using the `Resources.{name}` address pattern. Hosts own palettes,
inspectors, validation UI, persistence, activation, and runtime status.
