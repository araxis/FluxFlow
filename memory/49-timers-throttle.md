# Timers Throttle

Date: 2026-06-01

## Decision

Extend `FluxFlow.Components.Timers` with a typed rate-limiting transform:

```text
timer.throttle
```

The node is package-owned and stays application-neutral. It does not know what
the item represents; it only controls output timing.

## Shape

Ports:

- `Input`: configured input type
- `Output`: same configured input type

Options:

- `inputType`
- `name`
- `interval` or `intervalMilliseconds`
- `emitFirstImmediately`
- `boundedCapacity`

## Behavior

- interval must be greater than zero
- the first item is emitted immediately by default
- setting `emitFirstImmediately` to false delays the first item by one interval
- later items are emitted no more than once per interval
- items are queued and not intentionally dropped
- ordering is preserved
- completion drains pending inputs
- fault cancels pending throttle work
- output is bounded

## Boundaries

The package does not own:

- per-key throttling
- grouped rate limits
- host clock injection
- persisted rate-limit state
- external protocol retries

Those can be future additions if real workflows need them.

## Verification

Focused tests cover:

- typed throttle pass-through
- immediate first emission
- delayed first emission
- output spacing
- ordering
- diagnostics
- disposal and completion
- invalid interval, duplicate interval options, input type, and bounded capacity

Release tag:

```text
components-timers-v0.3.0-alpha.1
```
