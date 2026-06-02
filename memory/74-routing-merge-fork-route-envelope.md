# Routing Merge, Fork, And Route Envelope

Date: 2026-06-02

## Decision

Add `flow.fork`, `flow.merge`, and optional switch route envelopes to
`FluxFlow.Components.Routing`.

These are routing/stream primitives. They stay neutral and do not know any
transport, dashboard, storage, or scenario model.

## Scope

Added package version `0.6.0-alpha.1` with:

- `flow.fork`
- `flow.merge`
- `FlowMergeItem<TInput>`
- `FlowRoute<TInput>`
- `emitRouteEnvelope` on `flow.switch`
- `Routed` switch output port
- configured fork output ports
- configured merge input ports
- validation for empty, duplicate, invalid, and built-in port names
- diagnostics for fork forwarding and merge output emission
- tests for fan-out, merge source tagging, switch route envelopes, completion,
  diagnostics, invalid config, unknown input types, and module registration

## Behavior

`flow.fork` receives one typed `Input<TInput>` and sends each value to every
configured output in configured order. It completes all output ports when the
input completes.

`flow.merge` receives several same-type named inputs and emits one
`FlowMergeItem<TInput>` per input value. The envelope includes:

- sequence number
- source input port
- received timestamp
- original value

`flow.merge` completes only after every configured input completes.

`flow.switch` keeps its existing `Result`, `Matched`, `Default`, and direct
route outputs. When `emitRouteEnvelope=true`, it also emits `FlowRoute<TInput>`
on `Routed` with route metadata and the original value.

## Rationale

Consumers need both graph shapes:

- direct named ports for clear wiring and branch-local downstream behavior
- neutral envelopes for cases where one downstream node wants route metadata

Keeping route envelopes optional avoids changing existing switch users while
giving hosts a simpler shape for logging, storage, assertions, and mapping.

## Deferred

Future Routing candidates:

- richer `flow.switch` route result helpers if consumer usage shows a common
  mapper pattern.
- multi-input merge with different input types if a real workflow needs it.
- richer fork delivery policies if consumers need fail-fast or best-effort
  fan-out choices.

The recommended next step is to run this package through release verification,
then continue with the next backlog package only after Routing is green.
