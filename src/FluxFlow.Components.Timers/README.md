# FluxFlow.Components.Timers

Reusable timer components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `timer.interval` | `Output` | Emits `TimerTick` values on a fixed interval. |
| `timer.schedule` | `Output` | Emits `ScheduleTick` values from a cron expression. |
| `timer.delay` | `Input` -> `Output` | Delays typed inputs and emits them unchanged. |
| `timer.throttle` | `Input` -> `Output` | Rate-limits typed inputs without changing them. |
| `timer.debounce` | `Input` -> `Output` | Emits the latest typed input after a quiet period. |

The package emits neutral tick contracts only. Hosts decide whether ticks drive
polling, periodic health checks, metrics, file work, message publishing, or
other workflow activity.

## Interval

```json
{
  "type": "timer.interval",
  "name": "poll",
  "intervalMilliseconds": 1000,
  "initialDelayMilliseconds": 250,
  "maxTicks": 10,
  "boundedCapacity": 128
}
```

`timer.interval` emits `TimerTick` values with a sequence number, timestamp,
due time, elapsed time, interval, and drift. Use `emitImmediately: true` when
the first tick should be emitted as soon as the node starts.

## Schedule

```json
{
  "type": "timer.schedule",
  "name": "weekday-noon",
  "cron": "0 12 ? * MON-FRI",
  "timeZoneId": "UTC",
  "maxTicks": 10,
  "boundedCapacity": 128
}
```

`timer.schedule` emits `ScheduleTick` values. Cron expressions can use five
fields or six fields when seconds are needed.

## Delay

```json
{
  "type": "timer.delay",
  "inputType": "message",
  "delayMilliseconds": 250,
  "boundedCapacity": 128
}
```

`timer.delay` preserves input order and emits the original item after the
configured delay. Register custom input aliases on the package options.

## Throttle

```json
{
  "type": "timer.throttle",
  "inputType": "message",
  "intervalMilliseconds": 100,
  "emitFirstImmediately": true,
  "boundedCapacity": 128
}
```

`timer.throttle` preserves input order and emits the original item no more
than once per configured interval. It queues items through normal bounded
capacity instead of dropping them.

## Debounce

```json
{
  "type": "timer.debounce",
  "inputType": "message",
  "quietPeriodMilliseconds": 250,
  "boundedCapacity": 128
}
```

`timer.debounce` keeps the latest input and emits it after no new input arrives
for the configured quiet period. When the input completes, the latest pending
item is flushed before the output completes.

## Registration

```csharp
registry.RegisterTimerComponents(options => options
    .UseClock(timerClock)
    .RegisterType<MyMessage>("message"));
```

`UseClock(...)` is optional. The default uses normal system time. Hosts and
tests can provide an `ITimerClock` to control tick timestamps, schedule due-time
delays, delay nodes, throttle windows, and debounce quiet periods.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
