# Optional Durable Output Capture

`FluxFlow.Engine.DurableOutput` is the small provider-neutral foundation for
durable output capture. A host explicitly selects application outputs, supplies
a store, and gives each selected payload an explicit stable contract name and
`JsonTypeInfo<T>`. Optional leased delivery builds on the captured records
through a separate store capability and registration.

The feature is opt-in. Outputs without a capture declaration retain the normal
bounded in-process path with no serialization, store call, extra queue, or
background service.

## Boundary And Guarantee

For a selected output, Engine performs these operations in order:

1. Read the next `FlowMessage<T>` from the existing bounded output ingress.
2. Serialize it through its declared `JsonTypeInfo<T>`.
3. Await `IDurableOutputStore.EnqueueAsync(...)`.
4. Accept `Enqueued` or equivalent-content `AlreadyExists`.
5. Dispatch the original message to revision routes, ordinary links, receive
   waiters, and observations.

`Conflict`, serialization failure, or a store exception prevents step 5 and is
reported as `ApplicationPortRejectionReason.OutputCaptureFailed`. The output
completion also exposes the failure.

The guarantee begins when the application output port processes the message.
A component source can transfer a message into the in-memory port ingress before
the store commit. This round therefore does not claim producer-level durable
acknowledgement, atomic business-state plus outbox commit, or exactly-once
execution.

`ApplicationPorts.ReceiveAsync(...)` and `ObserveAsync(...)` remain live host
taps. They see selected outputs only after capture, but they are not persistence
APIs and cannot be used to implement a durable outbox by themselves.

## Registration

Use one flat builder action. There are no nested callbacks, named options,
assembly scans, or reflection-based contract discovery.

```csharp
using System.Text.Json.Serialization;
using FluxFlow.Composition.Addressing;
using FluxFlow.Engine.DurableOutput;

[JsonSerializable(typeof(OrderCompleted))]
internal partial class ApplicationJsonContext : JsonSerializerContext
{
}

services.AddSingleton<IDurableOutputStore, HostDurableOutputStore>();
services.AddFluxFlowDurableOutput(outputs =>
{
    outputs.Capture(
        ApplicationAddress.WorkflowPort("Orders", "Complete", "Output"),
        "orders.completed.v1",
        ApplicationJsonContext.Default.OrderCompleted);
});
```

The address is the same canonical workflow-port address regardless of whether
the application definition came from JSON or the C# authoring DSL. Durability
does not create a second application model.

The builder is mutable only during registration. It freezes declarations into
immutable runtime configuration. Repeating an equivalent declaration is
idempotent. Reusing an address with a different contract or payload type, or
reusing a contract name for an incompatible payload type, fails immediately.
Exactly one `IDurableOutputStore` and one output-capture resolver are allowed.

## Persisted Envelope

`DurableOutputEnvelope` is immutable and provider-neutral. It contains:

- canonical application output address;
- stable contract name and schema version;
- value/error discriminator;
- detached JSON payload or structured `FlowError`;
- original message, trace, correlation, and causation identity;
- original message timestamp and store-capture time;
- defensively copied string headers.

The stable `DurableOutputKey` is the output address plus the existing
`MessageId`; capture does not invent another identity. Providers clone or own
persisted data and must not retain mutable caller buffers.

## Store Contract

`IDurableOutputStore` deliberately contains one method:

```csharp
ValueTask<DurableOutputEnqueueResult> EnqueueAsync(
    DurableOutputEnvelope envelope,
    CancellationToken cancellationToken = default);
```

The atomic result is:

| Status | Meaning | Engine dispatch |
|--------|---------|-----------------|
| `Enqueued` | A new record committed | Continues |
| `AlreadyExists` | The same key and equivalent message content already exist | Continues |
| `Conflict` | The same key contains different message content | Faults |

Capture time is provider metadata and does not make otherwise equivalent
message content conflict. Cancellation before the atomic commit leaves
ownership with the caller. After commit, cancellation must not retract the
record; a later shutdown may prevent live dispatch, but the stored record
remains available to a future delivery layer.

## Ordering, Backpressure, And Lifecycle

Configured capture runs inside the existing serial application-output pump.
Messages on one output retain order. A slow store applies backpressure through
the existing bounded ingress; there is no hidden unbounded buffer or retry loop.

Revision drain waits for the dispatch gate, including an in-flight capture.
Abort passes the port lifecycle cancellation token to the store. Capture creates
no independently owned task, hosted service, timer, thread, or service scope.

The capture resolver is selected once when a typed output port is constructed.
Unselected ports keep a null capture reference and bypass envelope construction
and serialization.

## Capture Instrumentation

The package publishes capture signals through the BCL source and meter named
`FluxFlow.Engine.DurableOutput`:

| Instrument | Type and unit | Semantic tags |
|------------|---------------|---------------|
| `fluxflow.durable_output.captures` | counter, `{capture}` | `result=enqueued|already_exists|conflict|canceled|failed` |
| `fluxflow.durable_output.capture.duration` | histogram, `ms` | the same `result` values |

The `fluxflow.durable_output.capture` producer activity spans serialization and
the awaited store call. It may carry `flow.trace_id`; its `outcome` is the same
bounded result used by capture metrics. Conflict, cancellation, serialization
failure, and store failure keep their existing behavior while becoming
observable. Unselected outputs do not create capture activities or metrics.

No metric tag contains an address, contract, message or trace identity,
payload, header, exception text, provider setting, path, connection detail, or
credential. Listener failure is isolated and cannot make capture fail or turn a
failed capture into success.

## Provider Guidance

A provider must:

- atomically insert or compare one record;
- enforce idempotency by `DurableOutputKey`;
- compare persisted message content without treating `CapturedAt` as business
  content;
- remain safe when different output ports enqueue concurrently;
- return a result for the exact requested key;
- honor the documented cancellation/commit ownership boundary;
- keep provider settings and migrations outside `FluxFlowApplicationOptions`.

Do not add delivery, retry, or dead-letter operations to
`IDurableOutputStore`. Delivery uses the separate
`IDurableOutputDeliveryStore`; a provider may support capture without it.

## Optional Delivery

Providers may also expose the separate `IDurableOutputStatusStore` operational
capability. It reports payload-free capture/delivery state without backfilling
or otherwise changing delivery data. See
[Durability Operational Status](35-durability-operational-status.md).


Capture does not start a worker or create delivery state. A host that also
registers exactly one `IDurableOutputDeliveryHandler`, a delivery-capable store,
and `AddFluxFlowDurableOutputDelivery(...)` gets serial leased at-least-once
delivery with fixed retry, exact lease renewal for long-running handlers, and
crash recovery after lease expiry. Successful
completion remains a durable tombstone. Unlimited retry is the default; a host
may configure a positive maximum that moves the final failed attempt to a
durable dead letter.

Handlers own destination behavior and should use `DurableOutputEnvelope.Key`
for idempotency. The dispatcher has no transport discovery, parallelism,
batching, automatic replay, or exactly-once claim. See
[Optional Durable Output Delivery](29-durable-output-delivery.md) and
[Durable Output Dead-Letter Operations](30-durable-output-dead-letter-operations.md).
Renewal behavior is detailed in
[Durable Output Lease Renewal](37-durable-output-lease-renewal.md).

## Not Included

This foundation does not provide:

- a store inside the provider-neutral package; the separate
  `FluxFlow.Engine.DurableOutput.SqlFile` package supplies the local SQLite
  implementation and `FluxFlow.Engine.DurableOutput.TSql` supplies the shared
  networked T-SQL implementation;
- built-in external transport adapters;
- automatic cleanup or retention scheduling;
- automatic/bulk replay or an administration endpoint/UI;
- producer/business-state transaction integration;
- workflow-completion acknowledgement;
- workflow state or component checkpoints;
- distributed transactions or exactly-once processing.

The first provider is documented in
[SQL-File Durable Outputs](28-sql-file-durable-outputs.md). Delivery is
documented in
[Optional Durable Output Delivery](29-durable-output-delivery.md); bounded
dead-letter inspection and explicit replay are documented in
[Durable Output Dead-Letter Operations](30-durable-output-dead-letter-operations.md).
The networked provider is documented in
[T-SQL Durable Outputs](32-tsql-durable-outputs.md).
Transport, retention, bulk operations, and administration endpoints remain
independent.
