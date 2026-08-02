# SQL-File Durable Outputs

`FluxFlow.Engine.DurableOutput.SqlFile` is the production SQLite single-file
implementation of `IDurableOutputStore` for local FluxFlow hosts. It preserves
selected application outputs before live Engine dispatch. The same singleton
also implements the separate `IDurableOutputDeliveryStore`,
`IDurableOutputDeadLetterStore`, and `IDurableOutputStatusStore` capabilities;
the same singleton also implements `IDurableOutputRetentionStore`. Engine still has no provider
dependency.

Use the provider-neutral `FluxFlow.Engine.DurableOutput` package with a custom
store when a shared database, document store, or distributed deployment model
is required. Provider changes do not alter workflow definitions, the C# DSL,
Engine, or output capture declarations.

## Registration

Provider selection and output selection are two independent flat calls:

```csharp
using FluxFlow.Composition.Addressing;
using FluxFlow.Engine.DurableOutput;
using FluxFlow.Engine.DurableOutput.SqlFile;

services
    .AddFluxFlowSqlFileDurableOutput(store =>
    {
        store.DatabasePath = "data/fluxflow-output.db";
        store.BusyTimeout = TimeSpan.FromSeconds(30);
    })
    .AddFluxFlowDurableOutput(outputs =>
    {
        outputs.Capture(
            ApplicationAddress.WorkflowPort("Orders", "Complete", "Output"),
            "orders.completed.v1",
            ApplicationJsonContext.Default.OrderCompleted);
    })
    .AddSingleton<IDurableOutputDeliveryHandler, OrderDeliveryHandler>()
    .AddFluxFlowDurableOutputDelivery(delivery =>
    {
        delivery.LeaseDuration = TimeSpan.FromMinutes(1);
        delivery.LeaseRenewalInterval = TimeSpan.FromSeconds(20);
        delivery.RetryDelay = TimeSpan.FromSeconds(10);
        delivery.IdleDelay = TimeSpan.FromMilliseconds(500);
        delivery.MaxDeliveryAttempts = 5;
    });
```

`DatabasePath` is required. Relative paths are enabled by default; absolute
paths require `AllowAbsoluteDatabasePath = true`. `CreateDatabase` and
`CreateDirectory` default to `true`. `BusyTimeout` must be positive and fit the
SQLite millisecond timeout range.

The registration callback builds immutable options. Registration is
side-effect-free: it does not create a directory, file, connection, schema,
task, timer, scope, or hosted service. Equivalent repeated registration is
idempotent. Different settings or pre-existing ownership of any durable-output
store capability fails before any provider descriptors are appended.

One service provider supports exactly one durable-output store. Routing
different outputs to different databases is deliberately deferred until a
concrete requirement justifies keyed provider ownership.

The last two calls are optional. Omitting them keeps a capture-only host: no
delivery hosted service runs and no delivery table is created, validated, read,
or written.

## Atomic Enqueue

The provider performs one immediate SQLite write transaction:

1. Read the row by canonical application address and `MessageId`.
2. Insert an absent row and return `Enqueued`.
3. Compare an existing row with
   `DurableOutputEnvelope.HasSameContent(...)`.
4. Return `AlreadyExists` for equivalent content without replacing the first
   capture.
5. Return `Conflict` for different content without overwriting it.

The comparison includes contract/schema identity, value or structured error,
structural JSON, message/trace/correlation/causation identity, the original
timestamp including its offset, and ordinal headers. `CapturedAt` alone is
excluded because it records the capture attempt rather than message content.

Cancellation is honored through the final pre-commit check. Commit then uses a
non-cancelable token so an accepted transaction is not retracted or reported
ambiguously after ownership transfers to the store.

## Schema And Lifecycle

Initialization occurs lazily on the first enqueue. Version 1 owns:

- `fluxflow_durable_output_schema`;
- `fluxflow_durable_outputs`.

The composite binary-collated primary key is
`(application_address, message_id)`. The row stores the complete immutable
envelope, JSON payload/error details, original and capture timestamps plus
offsets, and headers. It contains no delivery-state columns.

Delivery initialization is independent and occurs only on the first delivery or
dead-letter operation. Version 2 owns:

- `fluxflow_durable_output_delivery_schema`;
- `fluxflow_durable_output_deliveries`;
- `ix_fluxflow_durable_output_deliveries_eligibility`;
- `ix_fluxflow_durable_output_deliveries_dead_lettered`.

Each lease transaction backfills missing pending rows from immutable captures,
selects one due or expired record in deterministic order, assigns a fresh token
and owner, increments its attempt, and returns the complete envelope. Renewal
extends only the current exact token's unexpired lease-expiry columns.
Completion requires the current unexpired token and retains a delivered tombstone. Retry
preserves the attempt count, clears ownership, and stores the exact next-attempt
time. Dead-letter settlement requires the same current token and records only a
stable reason, exact timestamp/offset, and incremented generation. Version-1
delivery databases migrate transactionally without changing pending, leased, or
completed state. Deleting the complete delivery schema intentionally resets
delivery history; partial or unversioned deletion is rejected rather than
repaired.

The operational capability provides bounded metadata-only keyset listing, exact
full-envelope lookup, and generation-protected one-record replay. Replay returns
a current dead letter to pending with an explicit schedule and attempt zero; it
does not call the handler.

The output tables use different names from durable input, so both providers can
share a database file. Initialization rejects an unversioned output table,
missing or incompatible columns, invalid version metadata, and newer schemas.
Corrupt persisted rows fail deterministically when read for duplicate/conflict
resolution.

Connections are pooled and receive the configured SQLite busy timeout. Store
disposal clears its pool. Missing directory/database behavior follows the
explicit creation flags.

## Adding Another Provider

A different backend implements the existing one-method `IDurableOutputStore`
and, only when delivery is required, the separate
`IDurableOutputDeliveryStore`. It may independently expose
`IDurableOutputDeadLetterStore` for operator access. It supplies its own options
and registration extension. A relational provider can use a unique composite
key and transaction; a document provider can use a deterministic identity plus
conditional creation or optimistic concurrency. No Engine, workflow, or
dispatcher change is required.

Every production provider must preserve the same `Enqueued`, `AlreadyExists`,
and `Conflict` semantics under concurrent calls. The reusable durable-output
capture, delivery, and dead-letter conformance suites define that behavioral
floor. A provider test project supplies fresh isolated contexts for the
capabilities it implements, then inherits the shared suites to verify:

- idempotent capture and no-overwrite conflict handling;
- deterministic leasing, expiry recovery, and one-winner ownership;
- exact key/token/expiry compare-and-set renewal, completion, retry, and
  dead-letter settlement;
- terminal-state ineligibility and retry/replay scheduling boundaries;
- bounded metadata-only listing, exact envelope lookup, generation-protected
  replay, and stable keyset ordering.

The suites use explicit factories and narrow interfaces. They do not discover
providers through reflection, require one concrete object to implement every
capability, or prescribe a database technology. Provider-specific tests remain
responsible for real backend schema and migration, registration ownership,
transactions, restart/persistence, locking and timeout translation, corruption
handling, deployment topology, and resource lifecycle. Passing shared
conformance is a behavioral floor, not a substitute for those backend risks.

## Guarantee Limits

This provider is intended for local single-file deployment. It is not a network
database, distributed lock service, external delivery system, or exactly-once
execution mechanism. Persistence begins at the Engine output-capture boundary;
it is not atomic with producer business state.

The optional delivery capability is leased, serial, fixed-retry, and
at-least-once. Unlimited retry is the default; a positive maximum enables final
failure dead-lettering. Inspection and replay are explicit and bounded. There is
no built-in transport, automatic replay or purge, batching, parallelism,
multi-destination routing, distributed coordination,
workflow-completion acknowledgement, component checkpoints, or exactly-once
guarantee. See [Optional Durable Output Delivery](29-durable-output-delivery.md)
and [Durable Output Dead-Letter Operations](30-durable-output-dead-letter-operations.md).
See [Durable Output Lease Renewal](37-durable-output-lease-renewal.md) for the
heartbeat, race, and cancellation contract.

The same provider-neutral capture, delivery, and dead-letter contracts have
also passed a real multi-connection networked relational feasibility spike.
That experiment is deliberately non-packable and unsupported; it does not
change the SQL-file provider or normal host setup. See
[Networked Relational Durable-Output Feasibility](31-networked-relational-durable-output-feasibility.md).
For a supported shared networked provider, see
[T-SQL Durable Outputs](32-tsql-durable-outputs.md).

The separate `IDurableOutputRetentionStore` can explicitly delete bounded old
completed or dead-lettered capture parents and their cascaded delivery rows.
It preserves unmaterialized, pending, and leased work and adds no schema
version. See [Durable Terminal Retention](36-durable-terminal-retention.md).
