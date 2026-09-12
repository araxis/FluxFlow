# FluxFlow.Components.RequestReply

A transport-neutral bridge for adapting request/reply callers to
one-way workflow graphs. The package is support infrastructure, not a component
family and not a transport owner.

## How It Works

```text
host context -> Incoming -> Output -> workflow -> Responses -> context.ReplyAsync
```

- The host creates an `IRequestContext<TRequest, TResponse>` containing the
  request and transport-specific acknowledge, reply, and failure callbacks.
- `RequestReplyCoordinator<TRequest, TResponse>` uses Core's `FlowRequestReply`
  to mint a fresh graph exchange correlation, emit a canonical `FlowMessage`,
  and await the matching response, timeout, failure, or shutdown. The context's
  original transport correlation is unchanged.
- The workflow should create its response with `message.With(FlowValue.From(response))`
  so correlation and trace metadata are preserved. Only the transport context
  remains typed; Output and Responses use the same canonical graph envelope.
- Fire-and-forget mode emits and acknowledges without registering pending state.
- Input, output and response queues are bounded by `RequestReplyOptions.Capacity`.
  The same limit bounds owned caller tasks, including slow terminal callbacks
  after an exchange has left `InFlightCount`. Excess callers receive a capacity
  failure without their request being materialized.

## Shared exchange APIs

The legacy `CorrelatedRequestTracker`, its options/start-result types, no-op
`SweepInterval`, and unused duplicate-correlation diagnostic are removed in this
development major. Choose the existing boundary that matches your task:

- Graph request/reply: application `RequestAsync` or Core `FlowRequestReply`.
- Automatic transport callbacks: this bridge with `IRequestContext`.
- Explicit domain keys: Core `PendingExchangeCoordinator<TKey, TContext, TOutcome>`.

The keyed primitive exposes `TryStart`, `TryResolve`, `TryFault` and `TryCancel`.
Keys and contexts must be non-null; validate protocol-specific key rules at your
own boundary, or use the graph API's minted exchange identity.
Await the accepted start's `Completion` for its typed context, terminal kind,
outcome and error. `TryCancel` replaces silent removal: the consumer decides how
to handle the Cancelled completion. `FaultAll` settles the current batch without
stopping new starts; `Stop` also closes admission. Timers remain deadline-driven.

The primitive does not invoke consumer callbacks. If an advanced keyed consumer
needs to await notification, it awaits its own callback with its own token after
receiving Completion. That ordinary async call preserves its callback exception.
Consumers own and join those tasks, including when bulk settlement reports a
timer cleanup failure. Ordinary transport adapters need none of that bookkeeping:
the bridge already owns their bounded terminal notifications.

Options now contain only Mode, Timeout and Capacity. Their init accessors validate
assignments and record updates; duplicate constructor checks are unnecessary.

## Identity

Transport adapters may supply `IRequestContext.TraceContext`, a standard
`ActivityContext` captured before queue admission. The bridge carries that parent
into its canonical message and exchange operation; it never inherits a worker's
ambient activity. The optional default means no parent. Capture the transport's
current operation, not merely its remote parent, to retain the complete chain.
This value is separate from domain trace IDs and exchange correlations. It does
not retain an Activity object or configure a tracing SDK/exporter.

`TraceId` remains stable across one processing lineage; it is not sufficient to
identify a particular request/reply exchange. `MessageId` identifies an
individual envelope and `CausationId` identifies its parent envelope.

Core matches replies by fresh exchange correlation and expected reply route.
External IDs may be reused, including concurrently, without a stale response
settling a different caller. Respond from the graph request envelope, not by
copying an external ID into a newly created response. Fire-and-forget mode
retains the context's optional correlation, or mints one when absent.

## Lifecycle And Diagnostics

- Construct the coordinator directly and add it with `workflow.Add(name, bridge)`.
  Its `Ports` expose Output, Events and a terminal Responses control input. Connect
  a handler's output to `component.Inputs["Responses"]`. There is no dummy business
  input, creator or graph-facing typed conversion queue.
- Configure links before `StartAsync()`. The application starts registered sources;
  standalone callers must start the coordinator explicitly. Pre-start admission
  is bounded but does not process the request. A canceled start can be retried.
- `StopProduction()` closes incoming admission and drains emitted requests without
  closing Responses. `ProductionCompletion` covers that production phase. The
  application closes response controls after business work drains. Standalone
  callers must complete Responses after their handlers finish, then await Completion.
- The optional response materializer runs once in the bounded caller task, after
  Core matches the canonical reply. It returns a typed response or an error
  envelope; it must not perform graph dispatch or change exchange matching. Default deserialization
  failures fail that caller with `request.reply.invalid_shape`.

- `Output` provides bounded reliable delivery to active links. `Events` exposes
  the bridge's diagnostic events.
- A supplied `TimeProvider` controls timeout scheduling and timestamps.
- Capacity, timeout, unmatched response, and invalid input outcomes
  remain local to the bridge and do not own host lifetime.
- `Complete()`, `Fault(...)`, and `DisposeAsync()` settle tracked callers
  exactly once and close the bridge's Dataflow blocks.
- `Completion` is one stable lifetime task. It joins tracked caller notifications
  and timer cleanup before closing `Events`; shutdown callbacks are not detached.
  Timer and cancellation callback failures remain observable while remaining
  cleanup is attempted. Disposal reports cleanup failures even after a fault.
- Unexpected request extraction or serialization failures close admission and
  fail every accepted queued caller. A diagnostic failure after a terminal
  callback faults the bridge without notifying that caller again.
- Graceful completion and disposal drain accepted output to active consumers;
  consumers must continue accepting it or detach their links. `Fault(...)`
  cancels blocked delivery without waiting for a full consumer to drain.

## Composition

This package does not expose `FluxFlow.Composition` factories. Transport adapters
own their external contexts and use the shared Core exchange primitive through
this bridge. Workflow acknowledgement signals and broker acknowledgements remain
separate concerns; they must not be treated as terminal request/reply controls.
