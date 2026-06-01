# Timers Delay And Schedule

Date: 2026-06-01

## Decision

Extend `FluxFlow.Components.Timers` with two more general timing primitives:

- `timer.delay`: typed pass-through transform for delaying any registered input
  type.
- `timer.schedule`: cron-backed source for scheduled workflow ticks.

Both nodes remain protocol-neutral and application-neutral.

## Delay Shape

Node type:

```text
timer.delay
```

Ports:

- `Input`: configured input type
- `Output`: same configured input type

Options:

- `inputType`
- `name`
- `delay` or `delayMilliseconds`
- `boundedCapacity`

Behavior:

- delay cannot be negative
- zero delay is allowed
- ordering is preserved
- completion drains pending inputs
- fault cancels pending delay work
- output is bounded

## Schedule Shape

Node type:

```text
timer.schedule
```

Ports:

- `Output`: `ScheduleTick`

Options:

- `cron` or `expression`
- `name`
- `timeZoneId`
- `maxTicks`
- `boundedCapacity`

Cron support:

- five fields: minute hour day month day-of-week
- six fields: second minute hour day month day-of-week
- lists
- ranges
- steps
- `?` as a day wildcard
- month and day names

## Contracts

Added `ScheduleTick` with:

- timestamp
- name
- sequence
- started-at time
- due time
- cron expression
- time zone id
- drift

## Boundaries

The package does not own:

- application holiday calendars
- persisted schedules
- UI schedule builders
- scenario/test runner rules
- external job execution

Those remain host responsibilities or future packages.

## Verification

Focused tests cover:

- typed delay pass-through
- delay ordering
- delay diagnostics
- delay disposal and validation
- schedule tick emission
- schedule diagnostics and events
- deterministic cron occurrence rules
- cron names and question-mark day wildcards
- invalid cron, time zone, max tick, and capacity options

Release tag:

```text
components-timers-v0.2.0-alpha.1
```
