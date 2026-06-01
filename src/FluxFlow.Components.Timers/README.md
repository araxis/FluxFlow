# FluxFlow.Components.Timers

Reusable timer components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `timer.interval` | `Output` | Emits `TimerTick` values on a fixed interval. |
| `timer.schedule` | `Output` | Emits `ScheduleTick` values from a cron expression. |
| `timer.delay` | `Input` -> `Output` | Delays typed inputs and emits them unchanged. |
| `timer.throttle` | `Input` -> `Output` | Rate-limits typed inputs without changing them. |

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

## Registration

```csharp
registry.RegisterTimerComponents(options => options
    .RegisterType<MyMessage>("message"));
```
