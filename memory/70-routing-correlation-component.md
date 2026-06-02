# Routing Correlation Component

Date: 2026-06-02

## Decision

Keep request/response pairing inside `FluxFlow.Components.Routing` as
`flow.correlation`.

This is a routing primitive, not a transport feature. The node receives one
typed input stream, evaluates a key expression and a side expression, and emits
matched pairs or unmatched timeout records.

## Scope

Added package version `0.2.0-alpha.2` with:

- `flow.correlation`
- `FlowCorrelationMatch<TInput>`
- `FlowCorrelationTimeout<TInput>`
- neutral routing context support for more than one routing node type
- validation for key expression, side expression, sides, timeout, pending
  capacity, and bounded capacity
- focused tests for matching, out-of-order input, completion timeouts,
  observed timeouts, expression failures, invalid sides, capacity, diagnostics,
  and module registration

## Behavior

- `Input<TInput>` receives every item.
- `Matched` emits paired request and response values for the same key.
- `Timeouts` emits unmatched pending values when the timeout is observed before
  the next input and when the node completes.
- `Errors` emits recoverable failures and later items continue processing.
- Pending state is bounded by `maxPending`.
- Processing is serial and ordered by default.

## Deferred

Direct switch route outputs were added in
`71-routing-switch-output-ports.md`. Count/time windows were added in
`72-routing-window-component.md`. The next routing candidates are correlation
with separate input ports and joins. Those should wait until this single-stream
correlation shape is exercised by a consumer.

## Release Note

The first `0.2.0-alpha.1` tag did not publish a package because release
verification exposed a timer interval test that could wait on a buffered target
without draining remaining ticks. The test was hardened and the routing package
release moved forward as `0.2.0-alpha.2`.
