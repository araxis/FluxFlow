# FluxFlow.Engine.DurableInput

Optional provider-neutral durable input delivery for `FluxFlow.Engine`.

Use this package only when an input must survive process failure before the
Engine accepts it. Ordinary `ApplicationPorts.SendAsync(...)` remains the
smallest and fastest path for in-process workflows.

## Registration

Register the Engine first, then this adapter, explicit payload contracts, and
one host-owned store implementation:

```csharp
using FluxFlow.Engine;
using FluxFlow.Engine.DurableInput;

services.AddFluxFlow(configuration);

services.AddSingleton<IDurableInputStore, YourDurableInputStore>();

services.AddFluxFlowDurableInput(options =>
{
    options.BatchSize = 64;
    options.LeaseDuration = TimeSpan.FromSeconds(30);
    options.PollInterval = TimeSpan.FromMilliseconds(250);
    options.RetryDelay = TimeSpan.FromSeconds(1);
    options.StoreFailureDelay = TimeSpan.FromSeconds(2);
    options.MaxDeliveryAttempts = 10;
});

services.AddFluxFlowDurableInputContract<OrderSubmitted>("orders.submitted.v1");
```

The default `AcknowledgementMode` is `EngineAccepted`, preserving the original
lightweight behavior. When a workflow has an explicit terminal boundary, opt in
without nesting configuration callbacks:

```csharp
services.AddSingleton<IDurableInputCompletionSource, OrderCompletionSource>();

services.AddFluxFlowDurableInput(options =>
{
    options.AcknowledgementMode = DurableInputAcknowledgementMode.WorkflowCompleted;
    options.WorkflowCompletionTimeout = TimeSpan.FromMinutes(10);
    options.LeaseDuration = TimeSpan.FromSeconds(30);
    options.LeaseRenewalInterval = TimeSpan.FromSeconds(10);
});
```

Workflow-completion mode also requires the selected provider to register
exactly one `IDurableInputLeaseRenewalStore`. The dispatcher gives the
completion source the exact `DurableInputLease` before it sends the message.
The source must correlate its host-defined terminal signal with both the key
and lease token; FluxFlow does not infer completion from outputs, trace ids,
queue state, timing, or graph structure.

For trimming/AOT-oriented hosts, use the overload that accepts
`JsonTypeInfo<T>` from a source-generated JSON context.

Enqueue an existing message without changing its identity:

```csharp
var durableInputs = provider.GetRequiredService<DurableApplicationInputs>();
var result = await durableInputs.EnqueueAsync(
    "Orders.Submit.Input",
    FlowMessage.Create(new OrderSubmitted(orderId)));
```

`Enqueued` and `AlreadyExists` are accepted outcomes. `Conflict` means the same
`(ApplicationAddress, MessageId)` was used with different persisted content.

## Delivery Contract

In the default mode the dispatcher leases at most `BatchSize` records and
processes them sequentially. Workflow-completion mode leases one record at a
time so no later lease expires while the current workflow is still active. It
resolves current Engine port metadata on every attempt, so an application
revision can replace the addressed input without pinning an old runtime
generation.

- In `EngineAccepted`, `Accepted` is atomically marked delivered.
- In `WorkflowCompleted`, `Accepted` remains leased until the explicit
  subscription succeeds. The exact token is renewed at
  `LeaseRenewalInterval`; completion failure or timeout follows the existing
  retry/maximum-attempt policy.
- `Full`, `Unavailable`, `Completed`, and a temporarily missing address are
  released for retry.
- Unknown contracts, unsupported schemas, malformed payloads, output/signal
  addresses, and payload-type mismatches are dead-lettered.
- A transient result on the final configured attempt is dead-lettered as
  `MaximumAttemptsExceeded`.

If the process fails after Engine accepts a message but before the store records
delivery, the lease expires and the same `MessageId` may be delivered again.
This is intentional at-least-once behavior. Consumers that perform external
side effects should deduplicate by `MessageId`.

Workflow completion narrows when the inbox tombstone is written, but it cannot
close the store/workflow transaction gap. A crash after terminal side effects
and before `MarkDeliveredAsync` can still redeliver the same message. This mode
does not provide exactly-once execution, workflow checkpoints, rollback, or a
distributed transaction.

## Store Provider Obligations

`IDurableInputStore` is the only persistence boundary. An implementation must:

1. Key records by `(ApplicationAddress, MessageId)`.
2. Make an equivalent duplicate enqueue return `AlreadyExists`, and different
   content under the same key return `Conflict`.
3. Persist `Pending`, `Leased`, `Delivered`, and `DeadLettered` state.
4. Lease due pending records and expired leases in deterministic oldest-due
   order, with exclusive owner/token metadata, while atomically incrementing
   the attempt count.
5. Apply delivered, release, and dead-letter transitions only when the supplied
   lease token is still current and unexpired. A stale transition must never
   mutate a newer lease.
6. Persist the full immutable envelope, including structured `FlowError`, JSON
   payload, schema version, stable contract name, identity, timestamp, and
   headers.

Providers that support `WorkflowCompleted` additionally implement
`IDurableInputLeaseRenewalStore`. Renewal atomically matches a current,
unexpired key/token and changes only the requested expiry. Existing providers
that implement only `IDurableInputStore` remain fully valid for
`EngineAccepted`.

This core package intentionally ships no database provider, migrations, public
in-memory store, output durability, application-revision persistence, or
exactly-once claim. Add `FluxFlow.Engine.DurableInput.SqlFile` for a local,
self-contained SQLite store or `FluxFlow.Engine.DurableInput.TSql` for a shared
networked relational store. Payloads and headers are not written to dispatcher
logs.

## Instrumentation

The package publishes BCL signals through `ActivitySource` and `Meter`, both
named `FluxFlow.Engine.DurableInput`. The consumer activity is
`fluxflow.durable_input.process`. Counters cover acquired leases, applied
delivered/retry/dead-letter outcomes, lease-renewal results, and store failures;
`fluxflow.durable_input.processing.duration` records milliseconds.

Metric dimensions are limited to bounded outcomes, results, failure kinds, and
store operation names. They never include persisted identities, addresses,
contracts, payloads, headers, lease data, provider settings, exception text, or
secrets. A host attaches its own listener or exporter; this package registers
none, performs no status polling, and isolates listener failure from delivery.

## Optional Dead-Letter Operations

`IDurableInputDeadLetterStore` is a separate optional provider capability. It
does not enlarge `IDurableInputStore`, alter dispatcher behavior, or require a
new registration callback. Providers may implement durable delivery without
supporting operational inspection or replay.

Capable providers offer bounded metadata-only listing, exact full-envelope
lookup, and explicit single-record replay:

```csharp
var deadLetters = provider.GetRequiredService<IDurableInputDeadLetterStore>();
var page = await deadLetters.ListAsync(new DurableInputDeadLetterQuery(
    failureKind: DurableInputFailureKind.UnknownContract,
    pageSize: 50));

var selected = page.Items[0];
var details = await deadLetters.GetAsync(selected.Key);
var now = timeProvider.GetUtcNow();
var replay = await deadLetters.ReplayAsync(new DurableInputReplay(
    selected.Key,
    selected.Generation,
    replayedAt: now,
    nextAttemptAt: now));
```

Listing is newest-first with stable ordinal keyset pagination. Summaries omit
payloads, headers, error details, and tracing identities; exact lookup returns
the complete envelope. Replay is an atomic compare-and-set on key, current
dead-letter state, and generation. Success resets the delivery-attempt budget
and schedules the preserved envelope as Pending. There is no bulk, automatic,
or delivered-record replay or audit history. Permanent deletion is available
only through the separate explicit retention capability below.

## Optional Operational Status

Providers may separately implement `IDurableInputStatusStore`. The caller
supplies an explicit observation time and receives an immutable payload-free
snapshot of pending/ready, leased/expired, delivered, and dead-letter counts:

```csharp
var statusStore = provider.GetRequiredService<IDurableInputStatusStore>();
var status = await statusStore.GetStatusAsync(
    new DurableInputStatusQuery(timeProvider.GetUtcNow()),
    cancellationToken);
```

Status does not enlarge `IDurableInputStore`, drive the dispatcher, or reveal
keys, payloads, headers, lease identities, or failure descriptions. Providers
must keep inspection read-only and must not cache snapshots or own another
clock.

## Optional Terminal Retention

Providers may separately implement `IDurableInputRetentionStore` for explicit,
bounded deletion of delivered tombstones or current dead letters:

```csharp
var retention = provider.GetRequiredService<IDurableInputRetentionStore>();
var result = await retention.PurgeDeliveredAsync(
    new DurableInputRetentionRequest(
        terminalBefore: timeProvider.GetUtcNow().AddDays(-30),
        maxCount: 250),
    cancellationToken);
```

The exclusive cutoff can be scoped to one application address. Calls delete at
most 1,000 rows and return only the number deleted. The host owns scheduling
and repetition; FluxFlow performs no automatic cleanup. Purging a delivered
record ends its deduplication window, and purging a dead letter permanently
removes its inspection and replay source.
