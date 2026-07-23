# FluxFlow.Components.Timers

Standalone temporal nodes over immutable workflow values. Interval and
Schedule are sources of `FlowValue`; Delay, Throttle, and Debounce consume
`FlowValue` and emit one normal `FlowResult<FlowValue>` stream. Every node also
publishes component Events and has no universal Errors port.

The package uses TPL Dataflow through `FluxFlow.Nodes`, but does not require
Composition, Engine, hosting, reflection, or assembly scanning. An optional
`TimeProvider` controls all scheduling and diagnostic timestamps.

## Nodes

| Node | Input | Output | Purpose |
|------|-------|--------|---------|
| `TimerIntervalNode` | none | `FlowValue` | Emits fixed-interval tick objects. |
| `TimerScheduleNode` | none | `FlowValue` | Emits cron-schedule tick objects. |
| `TimerDelayNode` | `FlowValue` | `FlowResult<FlowValue>` | Delays each accepted value from arrival. |
| `TimerThrottleNode` | `FlowValue` | `FlowResult<FlowValue>` | Queues and rate-limits values in order. |
| `TimerDebounceNode` | `FlowValue` | `FlowResult<FlowValue>` | Emits the latest value after a quiet period. |

Sources start once through `StartAsync()` and stop through `Complete()` or
disposal. Every tick has fresh message identity. Interval objects contain
`timestamp`, `name`, `sequence`, `startedAt`, `dueAt`, `elapsed`, `interval`,
and `drift`. Schedule objects contain `timestamp`, `name`, `sequence`,
`startedAt`, `dueAt`, `cron`, `timeZoneId`, and `drift`.

## Direct Use

```csharp
await using var interval = new TimerIntervalNode(
    new TimerIntervalSettings
    {
        Name = "poll",
        Interval = TimeSpan.FromSeconds(1),
        EmitImmediately = true,
        MaxTicks = 10
    });

interval.Output.LinkTo(ticks);
await interval.StartAsync();

var value = FlowValue.FromObject(new Dictionary<string, FlowValue>
{
    ["id"] = FlowValue.From("A-100")
});

await using var delay = new TimerDelayNode(
    new TimerDelaySettings { Delay = TimeSpan.FromMilliseconds(250) });

delay.Output.LinkTo(results);
await delay.Input.SendAsync(FlowMessage.Create(value));
```

A pre-canceled source start does not consume the one-start state.
`BoundedCapacity` bounds accepted work. Outputs are live broadcast fan-out,
not durable replay storage.

Delay preserves arrival order and measures each due time from intake, so a
burst does not become a throttle. Throttle queues accepted values and preserves
order. Debounce intentionally produces no result for superseded inputs and
emits its latest value exactly once when its timer expires or input completes.

Configuration errors fail construction. Unexpected source or pipeline faults
fault `Completion`. Expected per-message timing failures remain ordinary
results with stable `TimerResultKinds` and `TimerErrorCodeNames`; later inputs
continue. Success and failure results preserve correlation, trace, headers,
and causation.

## Migration From 4.x

The concise node names now own the canonical contracts. Replace
`FlowValueTimerIntervalNode`, `FlowValueTimerScheduleNode`,
`FlowValueTimerDelayNode`, `FlowValueTimerThrottleNode`, and
`FlowValueTimerDebounceNode` with their corresponding concise names.

The typed `TimerTick`, `ScheduleTick`, and generic timer transform APIs were
removed. Read source tick fields from immutable `FlowValue` objects, convert
typed inputs to `FlowValue` at the application boundary, read successful
transform values from `FlowResult.Value`, and route failures using `Kind`,
`IsError`, and `Error.Code` on the normal Output.

## Composition

Install `FluxFlow.Components.Timers.Composition` for canonical factories,
Designer metadata, flat configuration binding, and optional host-owned clocks.
The adapter owns neither clock lifetime nor durable output storage.
