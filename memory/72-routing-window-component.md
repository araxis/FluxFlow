# Routing Window Component

Date: 2026-06-02

## Decision

Add `flow.window` to `FluxFlow.Components.Routing`.

Windowing is a routing/stream primitive: it groups a stream into bounded batches
before downstream nodes run. It stays neutral and does not know any transport,
dashboard, storage, or scenario model.

## Scope

Added package version `0.4.0-alpha.1` with:

- `flow.window`
- `FlowWindow<TInput>`
- `FlowWindowEmitReason`
- `maxItems` count boundary
- `timeMilliseconds` time boundary
- `emitPartialOnCompletion`
- bounded input and output buffers
- diagnostics for emitted windows
- tests for count windows, time windows, count-before-time behavior, completion
  partials, suppressed partials, empty completion, invalid config, diagnostics,
  and module registration

## Behavior

- `Input<TInput>` receives every item.
- `Output` emits `FlowWindow<TInput>`.
- A window starts when the first item arrives.
- `maxItems` emits when the current window reaches that item count.
- `timeMilliseconds` emits when the current window has been open that long,
  even if no later input arrives.
- When both boundaries are configured, the first boundary reached emits.
- Completion emits a partial window by default.
- `emitPartialOnCompletion=false` discards partial windows on completion.

## Deferred

`flow.join` was added in `73-routing-join-component.md`.
`flow.fork`, `flow.merge`, and switch route envelopes were added in
`74-routing-merge-fork-route-envelope.md`.

The remaining hardening choice is separate request and response input ports for
correlation if consumers need that graph shape.
