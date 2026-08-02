# Durable Terminal Retention

FluxFlow durable providers expose optional retention services for permanently
deleting old terminal records in bounded batches:

- `IDurableInputRetentionStore` deletes delivered input tombstones or input
  dead letters;
- `IDurableOutputRetentionStore` deletes completed output captures or output
  dead letters.

Retention is an operational action. It is not part of workflow definitions,
the JSON model, the C# DSL, component settings, or
`FluxFlowApplicationOptions`. FluxFlow does not register a timer, worker,
default age, or automatic cleanup policy. The host chooses the cutoff, scope,
schedule, pacing, and monitoring.

## Bounded Host-Owned Execution

Capture one cutoff for a cleanup run and repeat bounded calls until a call
deletes fewer rows than requested:

```csharp
var retention = serviceProvider
    .GetRequiredService<IDurableInputRetentionStore>();

var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
const int batchSize = 250;

DurableInputRetentionResult result;
do
{
    result = await retention.PurgeDeliveredAsync(
        new DurableInputRetentionRequest(
            terminalBefore: cutoff,
            maxCount: batchSize),
        cancellationToken);
}
while (result.DeletedCount == batchSize);
```

Requests default to 100 rows and accept 1 through 1,000 rows. `Address` can
restrict a run to one exact canonical application address:

```csharp
var request = new DurableOutputRetentionRequest(
    terminalBefore: cutoff,
    address: ApplicationAddress.Parse("orders/production"),
    maxCount: 100);

var result = await outputRetention.PurgeDeadLettersAsync(
    request,
    cancellationToken);
```

The cutoff is exclusive: only a terminal timestamp strictly earlier than
`TerminalBefore` qualifies. Providers compare the UTC instant, so equivalent
values with different offsets select the same records. Within a batch,
candidates are selected by terminal timestamp, application address, and
message identifier in ascending order.

`DeletedCount == MaxCount` means another batch may exist; it does not guarantee
one. State can change between calls, so a multi-batch run is not a database
snapshot. A production loop should observe cancellation and may add host-owned
pacing appropriate to database load.

## Input Semantics

`PurgeDeliveredAsync` deletes only `Delivered` records whose
`delivered_at_utc_ticks` is before the cutoff. It never deletes pending,
leased, or dead-lettered inputs.

`PurgeDeadLettersAsync` deletes only `DeadLettered` records whose
`dead_lettered_at_utc_ticks` is before the cutoff. A dead letter replayed before
the purge acquires it is no longer eligible. If purge wins first, replay
observes not found.

Delivered input rows are idempotency tombstones. Deleting one ends its
deduplication window: a later enqueue with the same durable input identity can
be accepted as new work. Deleting an input dead letter is irreversible and
removes its inspection and replay source.

## Output Semantics

`PurgeCompletedAsync` selects only delivery state `Completed` whose
`delivered_at_utc_ticks` is before the cutoff.

`PurgeDeadLettersAsync` selects only delivery state `DeadLettered` whose
`dead_lettered_at_utc_ticks` is before the cutoff.

For either operation, the provider deletes the durable-output capture parent
inside the same transaction. The existing foreign-key cascade removes the
delivery row. Deleting only the delivery row would be unsafe because delivery
materialization could recreate it and redeliver the capture.

Pending and leased rows are never retention candidates. Renewing a live output
lease changes only its expiry and does not make it terminal or eligible for
purge.

Pending, leased, replayed, opposite-terminal, and capture-only unmaterialized
records are not eligible. Deleting a completed capture ends its idempotency and
history window, so a later capture with the same identity can be accepted as
new. Deleting an output dead letter is irreversible.

## Provider And Registration Behavior

The SQL-file and T-SQL providers implement retention with direct parameterized,
set-based SQL. Candidate selection and deletion happen in one bounded
transaction. Cancellation or failure before commit rolls the batch back; commit
uses a non-cancelable token after the final cancellation check so the result is
not ambiguous.

The retention contract resolves as another alias of the provider's existing
container-owned singleton. Repeated equivalent registration remains
idempotent, conflicting ownership fails, and registration or service
resolution performs no storage I/O.

Retention adds no table, column, migration, schema version, index, ORM, or
runtime dependency. Calling a retention method uses the provider's normal lazy
schema lifecycle. Output retention is a delivery-state operation and can
initialize the already-existing delivery schema. Capture-only hosts that never
call output retention remain capture-only; read-only status continues to leave
the delivery schema absent.

The T-SQL provider uses database row/update locks for cooperative multi-process
purging. SQL-file uses its established immediate write transaction. Concurrent
callers cannot both report deleting the same row.

## Operational Guidance

Choose a retention cutoff only after deciding how long the application needs:

- delivered/completed identities for deduplication and audit diagnosis;
- dead letters for inspection and replay;
- backups or external records for recovery requirements.

Monitor deletion counts and database growth in the host's existing operations
system. FluxFlow does not claim archival, legal-hold management, compliance
policy enforcement, exactly-once processing, or a cross-call snapshot.

See also:

- [Optional Durable Inputs](25-durable-inputs.md)
- [Optional Durable Output Delivery](29-durable-output-delivery.md)
- [Durability Operational Status](35-durability-operational-status.md)
- [Durable Output Lease Renewal](37-durable-output-lease-renewal.md)
