# FluxFlow.Components.Timers

Standalone temporal nodes for FluxFlow. Canonical timer sources emit immutable
`FlowValue` messages; canonical delay, throttle, and debounce nodes consume
`FlowValue` and emit one normal `FlowResult<FlowValue>` stream. All canonical
nodes also publish lifecycle `Events` and have no universal `Errors` port.

The package uses TPL Dataflow through `FluxFlow.Nodes`, but does not require
Composition, Engine, hosting, reflection, or assembly scanning. Every duration
is driven by an injected `TimeProvider`, so tests can advance time
deterministically.

## Canonical Nodes

| Node | Input | Output | Purpose |
|------|-------|--------|---------|
| `FlowValueTimerIntervalNode` | none | `FlowValue` | Emits fixed-interval tick objects. |
| `FlowValueTimerScheduleNode` | none | `FlowValue` | Emits cron-schedule tick objects. |
| `FlowValueTimerDelayNode` | `FlowValue` | `FlowResult<FlowValue>` | Delays every input from its arrival time. |
| `FlowValueTimerThrottleNode` | `FlowValue` | `FlowResult<FlowValue>` | Queues and rate-limits inputs in order. |
| `FlowValueTimerDebounceNode` | `FlowValue` | `FlowResult<FlowValue>` | Emits only the latest value after a quiet period. |

Sources start once through `StartAsync()` and stop through `Complete()` or
disposal. Every source tick has fresh message identity. Interval tick objects
contain `timestamp`, `name`, `sequence`, `startedAt`, `dueAt`, `elapsed`,
`interval`, and `drift`. Schedule tick objects contain `timestamp`, `name`,
`sequence`, `startedAt`, `dueAt`, `cron`, `timeZoneId`, and `drift`.

Configuration errors fail construction. Unexpected source or pipeline faults
fault `Completion` and are surfaced by the host through runtime system streams.
Expected per-message timing failures remain ordinary results with stable
`TimerResultKinds` and `TimerErrorCodeNames`; later inputs continue.

## Interval And Schedule

```csharp
await using var interval = new FlowValueTimerIntervalNode(
    new TimerIntervalSettings
    {
        Name = "poll",
        Interval = TimeSpan.FromSeconds(1),
        EmitImmediately = true,
        MaxTicks = 10
    });

interval.Output.LinkTo(downstream);
await interval.StartAsync();

await using var schedule = new FlowValueTimerScheduleNode(
    new TimerScheduleSettings
    {
        Name = "weekday-noon",
        Cron = "0 12 ? * MON-FRI",
        TimeZone = TimeZoneInfo.Utc,
        MaxTicks = 10
    });
```

A pre-canceled start does not consume the source's one-start state.
`BoundedCapacity` bounds source output. Output is live broadcast fan-out, not
durable replay storage.

## Delay, Throttle, And Debounce

```csharp
var value = FlowValue.FromObject(new Dictionary<string, FlowValue>
{
    ["id"] = FlowValue.From("A-100")
});

await using var delay = new FlowValueTimerDelayNode(
    new TimerDelaySettings { Delay = TimeSpan.FromMilliseconds(250) });

delay.Output.LinkTo(results);
await delay.Input.SendAsync(FlowMessage.Create(value));
```

- Delay preserves arrival order and measures each due time from intake, so a
  burst does not accidentally become a throttle.
- Throttle preserves order and queues inputs through bounded intake; it does
  not drop values.
- Debounce intentionally produces no result for superseded inputs. It emits
  the selected latest value after the quiet period or flushes it once when the
  input completes. Concurrent timer expiry and completion cannot duplicate it.
- Success results carry the original immutable value and preserve correlation,
  trace, headers, and causation. Timing failures carry immutable error details
  on the same Output.

## Typed Compatibility

Released direct-use nodes remain available unchanged:

- `TimerIntervalNode` emits `TimerTick`.
- `TimerScheduleNode` emits `ScheduleTick`.
- `TimerDelayNode<T>`, `TimerThrottleNode<T>`, and `TimerDebounceNode<T>` emit
  the original typed payload.

Those types retain their released Output, Errors, and Events surfaces. They are
compatibility APIs for code-authored typed pipelines; canonical workflow
definitions use the FlowValue/result nodes.

## Composition

Install `FluxFlow.Components.Timers.Composition` for canonical factories,
Designer metadata, flat configuration binding, and optional host-owned keyed
clocks:

```csharp
registry
    .RegisterTimerInterval()
    .RegisterTimerSchedule()
    .RegisterTimerDelay()
    .RegisterTimerThrottle()
    .RegisterTimerDebounce();
```

The adapter owns neither clock lifetime nor durable output storage.
