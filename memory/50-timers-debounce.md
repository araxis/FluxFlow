# Timers Debounce

Date: 2026-06-01

## Decision

Extend `FluxFlow.Components.Timers` with a typed quiet-period transform:

```text
timer.debounce
```

The node is package-owned and application-neutral. It only controls when a
typed item is emitted.

## Shape

Ports:

- `Input`: configured input type
- `Output`: same configured input type

Options:

- `inputType`
- `name`
- `quietPeriod` or `quietPeriodMilliseconds`
- `boundedCapacity`

## Behavior

- quiet period must be greater than zero
- each new input replaces the current pending item
- the latest pending item is emitted after the configured quiet period
- input completion flushes the latest pending item before output completion
- disposal completes input and flushes pending work
- fault cancels pending debounce work
- output is bounded

## Boundaries

The package does not own:

- per-key debounce groups
- persisted debounce state
- host clock injection
- external protocol retries
- application-specific coalescing rules

Those can be future additions if real workflows need them.

## Verification

Focused tests cover:

- typed debounce pass-through
- latest-input emission after quiet period
- latest-pending flush on completion
- latest value per quiet window
- diagnostics
- disposal and completion
- invalid quiet period, duplicate quiet-period options, input type, and bounded
  capacity

Release tag:

```text
components-timers-v0.4.0-alpha.1
```
