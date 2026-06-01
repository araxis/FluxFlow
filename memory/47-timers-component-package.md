# Timers Component Package

Date: 2026-06-01

## Decision

Add `FluxFlow.Components.Timers` as a separate package-owned component family
for time-driven workflow sources. The package should emit neutral tick records
and stay independent of protocols, storage, dashboards, or scenario runners.

## Package Shape

Implemented nodes:

| Node type | Ports | Purpose |
|-----------|-------|---------|
| `timer.interval` | `Output` | Emits `TimerTick` values on a fixed interval. |
| `timer.schedule` | `Output` | Emits `ScheduleTick` values from cron expressions. |
| `timer.delay` | `Input` -> `Output` | Delays typed inputs and emits them unchanged. |

Package contracts:

- `TimerTick`
- `ScheduleTick`

Package options:

- `name`
- `interval` or `intervalMilliseconds`
- `initialDelay` or `initialDelayMilliseconds`
- `emitImmediately`
- `maxTicks`
- `boundedCapacity`

Additional `timer.schedule` options:

- `cron` or `expression`
- `timeZoneId`

Additional `timer.delay` options:

- `inputType`
- `delay` or `delayMilliseconds`

## Behavior

- The interval must be greater than zero.
- Initial delay cannot be negative.
- Max tick count must be greater than zero when configured.
- `emitImmediately` emits the first tick as soon as the node starts.
- Without `emitImmediately`, the first tick occurs after the initial delay when
  configured, otherwise after the interval.
- Output is bounded and uses normal runtime backpressure.
- Completion cancels the timer and completes the output.
- Unexpected runtime failure emits `FlowError` and faults the node.
- The node emits diagnostics and `FlowEvent` entries for tick activity.
- `timer.schedule` supports five-field and six-field cron expressions, lists,
  ranges, steps, question-mark day wildcards, and month/day names.
- `timer.delay` uses host-registered input type aliases and preserves input
  ordering.

## Boundaries

The package does not include:

- application scheduling rules
- calendar storage
- dashboard models
- scenario/test runner behavior
- external protocol calls

Throttling and debounce behavior are future nodes in the same package family.

## Verification

Focused tests cover:

- module registration
- fixed interval tick emission
- initial delay
- explicit completion
- diagnostics and events
- second start rejection
- invalid duration, max tick, and bounded capacity options
- typed delay pass-through
- cron schedule tick emission
- invalid cron expression, time zone, input type, and duplicate option handling

Release tag:

```text
components-timers-v0.1.0-alpha.1
```
