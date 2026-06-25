# FluxFlow.Components.Timers

Standalone timer nodes for FluxFlow, built on the [FluxFlow.Nodes](../FluxFlow.Nodes)
kit. Every node is a self-contained TPL Dataflow processor: `new` it, `LinkTo`
the next node, and run it — no engine, registry, or runtime required. All timing
is driven by an injected `TimeProvider`, so tests stay deterministic with a
`FakeTimeProvider`.

## Nodes

| Node | Kind | Shape | Purpose |
|------|------|-------|---------|
| `TimerIntervalNode` | source | `Output` | Emits `FlowMessage<TimerTick>` on a fixed interval. |
| `TimerScheduleNode` | source | `Output` | Emits `FlowMessage<ScheduleTick>` from a cron expression. |
| `TimerDelayNode<T>` | transform | `Input` → `Output` | Re-emits each input after a delay, unchanged. |
| `TimerThrottleNode<T>` | transform | `Input` → `Output` | Rate-limits inputs to one per interval, unchanged. |
| `TimerDebounceNode<T>` | transform | `Input` → `Output` | Emits the latest input after a quiet period. |

Sources start with `StartAsync()` and produce until they hit `MaxTicks` (source
complete) or are stopped via `Complete()`/`DisposeAsync()`. Every emitted tick is
a `FlowMessage<T>` envelope with a fresh `CorrelationId`. The transforms preserve
the correlation id of each input message they re-emit. Errors surface on the
`Errors` port and diagnostics on the `Events` port. The package emits neutral
tick contracts only — hosts decide whether ticks drive polling, health checks,
metrics, file work, message publishing, or other activity.

For interval and schedule sources, `BoundedCapacity` configures source output
capacity. Tick loops await output delivery, so the source output block can apply
backpressure when its configured capacity is full. Delay, throttle, and debounce
keep using `BoundedCapacity` as their bounded input capacity.

## Interval

```csharp
await using var node = new TimerIntervalNode(new TimerIntervalSettings
{
    Name = "poll",
    Interval = TimeSpan.FromSeconds(1),
    InitialDelay = TimeSpan.FromMilliseconds(250),
    EmitImmediately = false,
    MaxTicks = 10
});
node.Output.LinkTo(downstream);
await node.StartAsync();
```

`TimerIntervalNode` emits `TimerTick` values with a sequence number, timestamp,
due time, elapsed time, interval, and drift. Set `EmitImmediately = true` to emit
the first tick as soon as the node starts.

## Schedule

```csharp
await using var node = new TimerScheduleNode(new TimerScheduleSettings
{
    Name = "weekday-noon",
    Cron = "0 12 ? * MON-FRI",
    TimeZone = TimeZoneInfo.Utc,
    MaxTicks = 10
});
node.Output.LinkTo(downstream);
await node.StartAsync();
```

`TimerScheduleNode` emits `ScheduleTick` values. Cron expressions use five fields,
or six when seconds are needed. The expression is compiled and validated in the
constructor.

## Delay / Throttle / Debounce

```csharp
await using var delay = new TimerDelayNode<MyMessage>(
    new TimerDelaySettings { Delay = TimeSpan.FromMilliseconds(250) });
await using var throttle = new TimerThrottleNode<MyMessage>(
    new TimerThrottleSettings { Interval = TimeSpan.FromMilliseconds(100), EmitFirstImmediately = true });
await using var debounce = new TimerDebounceNode<MyMessage>(
    new TimerDebounceSettings { QuietPeriod = TimeSpan.FromMilliseconds(250) });

await delay.Input.SendAsync(FlowMessage.Create(message));
```

- `TimerDelayNode<T>` preserves input order and re-emits each item after the
  configured delay.
- `TimerThrottleNode<T>` preserves input order and re-emits no more than once per
  interval, queuing items through bounded intake instead of dropping them.
- `TimerDebounceNode<T>` keeps the latest input and emits it after no new input
  arrives for the quiet period; a pending item is flushed when the input completes.

## Deterministic time

Pass a `TimeProvider` to any node's constructor (it defaults to
`TimeProvider.System`). Tests can supply a
[`FakeTimeProvider`](https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing)
and advance it to fire interval ticks, schedule occurrences, delays, throttle
windows, and debounce quiet periods without real-time waits.

## Composition

The optional `FluxFlow.Components.Timers.Composition` package registers timer
factories for `FluxFlow.Composition`. It binds the existing timer settings from
node configuration and resolves an optional keyed `TimeProvider` resource owned
by the host.

```csharp
services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry
        .RegisterTimerInterval()
        .RegisterTimerSchedule()
        .RegisterTimerDelay<MyMessage>()
        .RegisterTimerThrottle<MyMessage>()
        .RegisterTimerDebounce<MyMessage>());
```

Use custom node type strings for multiple transform input shapes, for example
`timer.delay.order` and `timer.debounce.http`. Schedule composition uses the
existing `TimerScheduleSettings` model; this adapter does not add time zone id
conversion.
