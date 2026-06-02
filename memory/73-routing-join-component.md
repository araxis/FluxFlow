# Routing Join Component

Date: 2026-06-02

## Decision

Add `flow.join` to `FluxFlow.Components.Routing`.

Join is a routing/stream primitive: it pairs values from two typed streams by
related keys. It stays neutral and does not know any transport, dashboard,
storage, or scenario model.

## Scope

Added package version `0.5.0-alpha.1` with:

- `flow.join`
- `FlowJoinResult<TLeft, TRight>`
- `FlowJoinTimeout<TLeft, TRight>`
- `FlowJoinSide`
- `Left` and `Right` input ports
- per-side key expressions
- host-registered left and right input types
- host-provided expression engine and context factories
- FIFO pairing for repeated keys
- timer-driven timeout output
- completion flush for unmatched values
- bounded pending-item tracking
- diagnostics for matches, timeouts, and recoverable failures
- tests for matching, duplicate-key order, timeouts, expression failures,
  capacity, diagnostics, invalid config, unknown input types, and module
  registration

## Behavior

- `Left<TLeft>` receives left-side values.
- `Right<TRight>` receives right-side values.
- `Output` emits `FlowJoinResult<TLeft, TRight>`.
- `Timeouts` emits `FlowJoinTimeout<TLeft, TRight>` for unmatched values.
- Repeated keys are paired in FIFO order.
- `maxPending` bounds unmatched values across both sides.
- Key failures, empty keys, and capacity failures emit `FlowError` and later
  values continue processing.
- Processing is serialized through one command queue to avoid races between
  left inputs, right inputs, timeouts, and completion.

## Deferred

Future Routing candidates:

- `flow.merge` for combining streams without key pairing.
- `flow.fork` for duplicating a stream into named outputs.
- route envelope helpers if consumers need one object output instead of
  dynamic switch ports.
- separate request and response input ports for correlation if consumers need
  that graph shape.

The next recommendation is to wait for a consumer graph that proves whether
merge/fork or route envelopes should come first.
