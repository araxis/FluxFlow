# Timers Component Package

Date: 2026-06-01

## Decision

Add `FluxFlow.Components.Timers` as a separate package-owned component family
for time-driven workflow sources. The package should emit neutral tick records
and stay independent of protocols, storage, dashboards, or scenario runners.

## Package Shape

Implemented node:

| Node type | Ports | Purpose |
|-----------|-------|---------|
| `timer.interval` | `Output` | Emits `TimerTick` values on a fixed interval. |

Package contracts:

- `TimerTick`

Package options:

- `name`
- `interval` or `intervalMilliseconds`
- `initialDelay` or `initialDelayMilliseconds`
- `emitImmediately`
- `maxTicks`
- `boundedCapacity`

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

## Boundaries

The package does not include:

- application scheduling rules
- calendar storage
- dashboard models
- scenario/test runner behavior
- external protocol calls

Schedule expressions, delay transforms, throttling, and debounce behavior are
future nodes in the same package family.

## Verification

Focused tests cover:

- module registration
- fixed interval tick emission
- initial delay
- explicit completion
- diagnostics and events
- second start rejection
- invalid duration, max tick, and bounded capacity options

Release tag:

```text
components-timers-v0.1.0-alpha.1
```
