# Durable Output Dead-Letter Operations

Durable-output dead letters are an optional operational boundary over captured
outputs that exhausted a host-configured positive attempt limit. They keep the
normal in-process path lightweight, preserve unlimited retry as the default,
and require deliberate operator replay.

## Enable Bounded Attempts

```csharp
services.AddFluxFlowDurableOutputDelivery(delivery =>
{
    delivery.LeaseDuration = TimeSpan.FromMinutes(1);
    delivery.LeaseRenewalInterval = TimeSpan.FromSeconds(20);
    delivery.RetryDelay = TimeSpan.FromSeconds(10);
    delivery.IdleDelay = TimeSpan.FromMilliseconds(500);
    delivery.MaxDeliveryAttempts = 5;
});
```

Attempts are one-based leases handed to the delivery handler. Failed attempts
below five are retried. A handler exception on attempt five atomically settles
the current lease as `HandlerFailure`. Cancellation does not consume a
settlement; the lease remains recoverable after expiry. If the maximum is
omitted or null, retry is unlimited and no automatic dead letter is created.

The stored reason is deliberately stable and small. FluxFlow does not persist
handler exception type, message, stack trace, payload excerpt, headers, or
arbitrary diagnostic data.

## Resolve The Operator Capability

The SQLite provider registers `IDurableOutputDeadLetterStore` as another alias
of its capture/delivery singleton:

```csharp
var deadLetters = services.GetRequiredService<IDurableOutputDeadLetterStore>();
```

The dispatcher does not depend on this interface. A custom provider may support
delivery settlement without providing operator listing or replay.

## List Metadata Safely

```csharp
var page = await deadLetters.ListAsync(
    new DurableOutputDeadLetterQuery(
        address: ApplicationAddress.WorkflowPort("Orders", "Complete", "Output"),
        reason: DurableOutputDeadLetterReason.HandlerFailure,
        deadLetteredFrom: from,
        deadLetteredBefore: before,
        pageSize: 50),
    cancellationToken);
```

Pages contain only key, contract name, envelope schema version, error/value
flag, capture time, attempt, stable reason, dead-letter time, and generation.
Payloads, `FlowError` data, headers, and tracing/lineage identifiers are not
selected for listing.

Page size defaults to 50 and cannot exceed 200. Time filtering is inclusive at
the lower bound and exclusive at the upper bound. Ordering is dead-letter time
descending, then binary application address and message ID ascending. Continue
with keyset state, not an offset:

```csharp
var next = page.NextCursor is null
    ? null
    : await deadLetters.ListAsync(
        new DurableOutputDeadLetterQuery(
            cursor: page.NextCursor,
            pageSize: 50),
        cancellationToken);
```

Carry the same filters when continuing a filtered scan. A cursor identifies the
last returned item. Concurrently inserted newer dead letters do not cause
offset-shift duplicates within the existing keyset walk.

## Retrieve One Complete Envelope

```csharp
var details = await deadLetters.GetAsync(key, cancellationToken);
```

Exact lookup returns the complete immutable envelope plus attempt, reason,
dead-letter time, and generation only while the key is currently dead-lettered.
Missing, completed, pending, leased, or already replayed keys return null. Treat
this method as access to application data and protect any endpoint that exposes
it with host-owned authentication, authorization, and auditing.

## Replay Deliberately

```csharp
var now = clock.GetUtcNow();
var result = await deadLetters.ReplayAsync(
    new DurableOutputReplay(
        details.Envelope.Key,
        expectedGeneration: details.Generation,
        replayedAt: now,
        nextAttemptAt: now + TimeSpan.FromMinutes(1)),
    cancellationToken);
```

Possible statuses are:

| Status | Meaning |
|--------|---------|
| `Replayed` | The exact current generation returned to pending. |
| `NotFound` | No delivery state exists for the key. |
| `NotDeadLettered` | The key exists but is no longer a current dead letter. |
| `GenerationMismatch` | It was dead-lettered again after the operator's view. |

A successful replay preserves the captured envelope and its original capture
time, clears lease/completion/dead-letter metadata, sets the requested next
attempt time, and resets attempt to zero. The next lease is attempt one. The
generation is retained; a later final failure increments it, preventing a stale
view from replaying the later cycle.

Replay does not call the handler or bypass scheduling. Running delivery still
requires the explicitly registered dispatcher and handler.

## SQLite Schema And Migration

SQL-file delivery schema version 2 adds dead-letter state, stable reason, exact
timestamp/offset, generation, and a partial keyset-listing index. The first
delivery or operator operation migrates version 1 inside one write transaction.
Pending, leased, and completed rows preserve all existing state. Capture schema
and co-located durable-input tables are untouched. Cancellation, invalid schema,
or failure rolls the migration back.

Capture-only usage does not initialize the delivery schema. Resolving services
or registering the provider performs no file or database work.

The networked T-SQL provider exposes the same inspection and replay contract
with generation compare-and-set protection in its shared version-1 schema. See
[T-SQL Durable Outputs](32-tsql-durable-outputs.md).

## Operational Limits

The API handles current dead-letter inspection and one-record explicit replay.
The host still owns endpoint security, authorization, auditing, bulk workflows,
and operator policy. FluxFlow supplies no automatic replay, bulk redrive,
automatic purge, archive, transport, UI/CLI, parallel delivery, distributed
coordinator, workflow checkpoint, or exactly-once destination guarantee.
Permanent bounded dead-letter deletion is available only through the separate
explicit retention service; see
[Durable Terminal Retention](36-durable-terminal-retention.md).
