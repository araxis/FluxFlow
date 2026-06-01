# FluxFlow.Components.Timers

Reusable timer components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `timer.interval` | `Output` | Emits `TimerTick` values on a fixed interval. |

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

## Registration

```csharp
registry.RegisterTimerComponents();
```
