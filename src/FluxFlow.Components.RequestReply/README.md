# FluxFlow.Components.RequestReply

A transport-neutral compatibility bridge for adapting request/reply callers to
one-way workflow graphs. The package is support infrastructure, not a component
family and not a transport owner.

## How It Works

```text
host context -> Incoming -> Output -> workflow -> Responses -> context.ReplyAsync
```

- The host creates an `IRequestContext<TRequest, TResponse>` containing the
  request and transport-specific acknowledge, reply, and failure callbacks.
- `RequestReplyCoordinator<TRequest, TResponse>` retains or creates the
  context's `CorrelationId`, emits a `FlowMessage<TRequest>`, and holds the
  context until a matching response, timeout, failure, or shutdown.
- The workflow should create its response with `message.With(response)` so the
  existing envelope identity is preserved.
- Fire-and-forget mode emits and acknowledges without registering pending state.
- Queue and in-flight counts are bounded by `RequestReplyOptions.Capacity`.

`CorrelatedRequestTracker<TContext, TResponse>` preserves the package's
correlation-based public API. Internally it delegates atomic pending state,
deadlines, capacity, duplicate handling, and cleanup to
`FluxFlow.Coordination` instead of maintaining a separate sweep-based tracker.
`SweepInterval` remains accepted for configuration compatibility but does not
control the shared coordinator's deadline queue.

## Identity

`TraceId` is FluxFlow's default internal workflow coordination identity and
remains stable across one processing lineage. `MessageId` identifies an
individual envelope and `CausationId` identifies its parent envelope.

This compatibility package still matches its established API by
`CorrelationId`. That value represents optional external or business protocol
correlation; it is not the default key required by `FluxFlow.Coordination`.
New workflow acknowledgement and signal coordination should normally use
`PendingExchangeCoordinator<TraceId, ...>` directly.

## Lifecycle And Diagnostics

- `Output` is a bounded reliable buffer. `Errors` and `Events` retain the
  compatibility bridge's existing diagnostic contracts.
- A supplied `TimeProvider` controls timeout scheduling and timestamps.
- Duplicate, capacity, timeout, unmatched response, and invalid input outcomes
  remain local to the bridge and do not own host lifetime.
- `Complete()`, `Fault(...)`, and `DisposeAsync()` settle accepted callers
  exactly once and close the bridge's Dataflow blocks.

## Composition

This package does not expose `FluxFlow.Composition` factories. HTTP adapters may use it for
their established correlation contract. MQTT workflow ACK/NAK coordination is
a separate TraceId-based concern and broker acknowledgements remain MQTT-owned.
