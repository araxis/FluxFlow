# Timers Finalization

Date: 2026-06-01

## Decision

Finalize the first general-purpose timer component set:

- `timer.interval`
- `timer.schedule`
- `timer.delay`
- `timer.throttle`
- `timer.debounce`

No new timer behavior was added in this pass. The package now has a shared
internal typed node factory path for the typed transform nodes.

The review also found and fixed a throttle timestamp race: an immediate
downstream feedback path could observe an emitted item before `timer.throttle`
recorded its last emitted time. The node now records the emission time before
the item is handed to the output buffer.

## Why

`timer.delay`, `timer.throttle`, and `timer.debounce` all resolve a configured
host-registered input type and then create a closed generic node. Keeping that
reflection and exception handling in one internal helper reduces drift between
the nodes and makes future maintenance less fragile.

## Behavior

The finalization keeps all public behavior unchanged:

- existing node types remain stable
- existing ports remain stable
- existing options remain stable
- diagnostics and error codes remain stable
- package remains protocol-neutral and application-neutral
- throttle feedback paths respect the configured interval consistently

## Verification

Focused and release verification cover:

- interval source behavior
- cron schedule source behavior
- delay transform behavior
- throttle transform behavior
- debounce transform behavior
- typed registration and factory creation
- validation and lifecycle behavior

Release tag:

```text
components-timers-v0.4.1-alpha.1
```
