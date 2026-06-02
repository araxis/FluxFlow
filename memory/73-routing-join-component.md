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

`flow.merge`, `flow.fork`, and optional switch route envelopes were added in
`74-routing-merge-fork-route-envelope.md`.

Future Routing candidates:

- separate request and response input ports for correlation if consumers need
  that graph shape.
- different-type merge if a real workflow needs one converged envelope over
  heterogeneous inputs.

The next recommendation is to release-verify Routing before moving to the next
package backlog item.
