# FluxFlow.Components.RequestReply

A transport-agnostic **request/reply bridge** for FluxFlow. HTTP is request→reply;
a dataflow graph is one-way. This bridges the two and correlates the answer back to
the caller — reused by the HTTP and MQTT triggers.

## How it works

```
host adapter ──IRequestContext──▶ Incoming ─┐
                                             │ mint/keep CorrelationId, hold in-flight
                                  Output ◀───┘  FlowMessage<TRequest>  ──▶ the graph
                                                                              │
host caller ◀── context.ReplyAsync ◀── Responses ◀── FlowMessage<TResponse> ─┘ (same id)
```

- The host creates an `IRequestContext<TRequest, TResponse>` per inbound request —
  it carries the request and a `ReplyAsync`/`FailAsync` that write back to the real
  transport (`HttpContext`, an MQTT reply topic, …). The bridge never sees the transport.
- `RequestReplyCoordinator<TRequest, TResponse>` assigns a `CorrelationId` (or honours one
  the context supplies), holds the context in-flight, and emits `FlowMessage<TRequest>`
  on `Output`.
- The graph maps request → response with `message.With(response)`, which preserves the
  correlation id, and posts it to `Responses`.
- The bridge matches by id, calls `ReplyAsync`, and evicts. Requests with no response
  within `Timeout` are failed (`FailAsync`) and evicted, so the map never leaks and no
  caller hangs forever.
- `CorrelatedRequestTracker<TContext, TResponse>` is the lower-level reusable core
  for nodes that already own their transport ports. It handles pending correlation,
  duplicate detection, timeout, and cleanup while the node decides how to emit,
  acknowledge, reject, or reply.

## Notes

- `Output` is a **bounded buffer** (reliable, backpressure) — a trigger must not drop
  inbound requests. `Errors`/`Events` are broadcast (observability).
- Everything is keyed on `CorrelationId` from `FluxFlow.Nodes` — the same envelope id
  that flows through the whole graph.
- Inject a `TimeProvider` for deterministic timeout tests.
- `Events` emits `Received` when a request is accepted for processing,
  `Published` after it reaches the graph-facing `Output`, and `Replied`,
  `TimedOut`, `Unmatched`, or `Invalid` for the corresponding terminal or
  diagnostic state.
- `RequestReplyOptions` and `CorrelatedRequestTrackerOptions` validate simple
  invariants when values are assigned. Unsupported modes, non-positive capacity,
  non-positive timeout, and non-positive sweep interval fail fast before
  dataflow blocks or timers are created.
- Invalid null request contexts and null response messages are reported through
  `Errors` and `Events` without faulting the coordinator, so later valid
  messages can still flow. `CorrelatedRequestTracker` rejects null contexts
  before storing them as pending requests.
- `Complete()` and `DisposeAsync()` close both coordinator inputs and fail any
  in-flight callers with `OperationCanceledException`, so `Completion` can be
  awaited without leaving callers hanging.

## Composition

This package does not expose standalone nodes or `FluxFlow.Composition`
factories. It is support infrastructure for transport adapters that need to
bridge inbound request/reply behavior into one-way workflow graphs.

HTTP and MQTT trigger packages own their transport-specific integration. Normal
composition packages consume those adapters or their host-owned resources rather
than composing `RequestReplyCoordinator<TRequest, TResponse>` directly.
