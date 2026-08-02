# SQL-File Durable Inputs

`FluxFlow.Engine.DurableInput.SqlFile` is the built-in production provider for
local durable ingress. It implements the provider-neutral lease protocol with a
versioned SQLite database and leaves `FluxFlow.Engine` unchanged.

## Choose The Input Boundary

| Requirement | Use |
|-------------|-----|
| Lowest-overhead delivery inside the current process | `ApplicationPorts.SendAsync(...)` |
| Durable ingress with a host-specific/shared store | `FluxFlow.Engine.DurableInput` plus a custom `IDurableInputStore` |
| Durable ingress on one machine with no database server | Add `FluxFlow.Engine.DurableInput.SqlFile` |
| Durable ingress shared by multiple processes or machines | Add `FluxFlow.Engine.DurableInput.TSql` |
| Keep the inbox lease until an explicit terminal signal | Add `WorkflowCompleted`, one completion source, and a renewal-capable provider |

## Setup

```csharp
services
    .AddFluxFlow(configuration)
    .AddFluxFlowDurableInput(input =>
    {
        input.BatchSize = 64;
        input.LeaseDuration = TimeSpan.FromSeconds(30);
        input.PollInterval = TimeSpan.FromMilliseconds(250);
        input.RetryDelay = TimeSpan.FromSeconds(1);
        input.StoreFailureDelay = TimeSpan.FromSeconds(2);
        input.MaxDeliveryAttempts = 10;
    })
    .AddFluxFlowSqlFileDurableInput(store =>
    {
        store.DatabasePath = "data/fluxflow-inputs.db";
        store.CreateDatabase = true;
        store.CreateDirectory = true;
        store.AllowAbsoluteDatabasePath = false;
        store.BusyTimeout = TimeSpan.FromSeconds(30);
    })
    .AddFluxFlowDurableInputContract<OrderSubmitted>("orders.submitted.v1");
```

These are three flat registration callbacks. Durable dispatcher behavior,
provider storage settings, and typed serialization contracts remain separate.
Registration freezes each builder and performs no filesystem or database I/O.
The provider store is a singleton owned and asynchronously disposed by DI.
It is exposed through `IDurableInputStore`, `IDurableInputDeadLetterStore`,
`IDurableInputLeaseRenewalStore`, and `IDurableInputStatusStore` as the exact
same instance.

Choose this provider when one application process owns a local file and the
smallest deployment footprint matters most. Choose
[`FluxFlow.Engine.DurableInput.TSql`](34-tsql-durable-inputs.md) when several
processes must lease from one shared database. The providers implement the same
three contracts but retain independent configuration, schema, and operational
ownership.

## Filesystem And Schema Lifecycle

`DatabasePath` is required and trimmed. Relative paths are allowed by default;
absolute paths require explicit opt-in. Missing parent directories and database
files are created on first use only when their corresponding flags permit it.

The provider creates a dedicated schema metadata table and durable-input table
inside one transaction. New databases use schema version 2. First use upgrades
version 1 transactionally by adding a nonnegative dead-letter generation,
backfilling existing dead letters to generation 1, and creating the bounded
dead-letter listing index. Every existing envelope and state is preserved, and
the metadata version changes last. Initialization and migration are safe when
multiple provider instances reach the same database together. An unsupported
newer or incompatible schema fails clearly; the provider never deletes,
recreates, downgrades, or silently repairs data.

Before upgrading, take a coordinated SQLite backup and retain the previous
application package until the new host has opened the database successfully.
Do not copy only the main database while writes may be in progress; stop the
host or use a SQLite-aware backup process that includes the active journal.

## Concurrency And Recovery

SQLite permits one writer at a time. Every enqueue, lease batch, and transition
uses a short transaction, while `BusyTimeout` controls how long lock contention
is allowed to wait. Separate store instances using the same file cannot hold an
active lease for the same row. An expired lease is eligible for another owner
and receives a new token and incremented attempt.

Lease renewal is another short write transaction. It updates only the exact
requested expiry when state is Leased, the token matches, and the prior expiry
is strictly later than the operation time. Wrong, expired, missing, or terminal
records are not revived. Renewal uses the existing schema-version-2 columns;
this capability adds no migration or database object.

Cancellation before an operation commits rolls it back. Once commit succeeds,
the durable row owns the operation even if caller interest disappears. A crash
after Engine input acceptance but before the delivered transition commits can
redeliver the same message identity. Consumers that perform non-idempotent side
effects must deduplicate by the preserved `MessageId` or use an appropriate
transactional boundary of their own.

Delivered rows remain durable idempotency tombstones. Dead-lettered rows remain
tombstones unless an operator explicitly replays the exact current generation.
Replay resets the attempt budget and schedules the preserved envelope; a later
dead-letter increments its generation.

## Operational Dead Letters

The existing `AddFluxFlowSqlFileDurableInput(...)` registration exposes the
same singleton through `IDurableInputStore` and
`IDurableInputDeadLetterStore`. There is no operational callback or additional
storage option.

`ListAsync` returns at most 200 metadata summaries and never selects payload,
header, structured-error detail, or tracing columns. Results are ordered by
dead-letter time descending and ordinal address/message identity. A typed
cursor continues from the last returned item using stable keyset pagination.
`GetAsync` restores the complete envelope for one exact current dead letter.

`ReplayAsync` is an atomic compare-and-set on key, current state, and expected
generation. One concurrent caller can succeed. Missing, non-dead-letter, and
stale-generation outcomes are explicit and non-mutating. Cancellation before
commit rolls back; a committed replay belongs to storage.

## Data Protection And Limits

Payloads, headers, structured errors, identities, and retry state are stored in
the database. Restrict filesystem access and use platform storage encryption
where required. Dispatcher and provider diagnostics never include payloads,
headers, error details, database paths, or connection strings.

The provider targets local/single-machine deployment. It is not a network
database, distributed lock service, or exactly-once execution system. It does
not persist Engine revisions, internal links, workflow state, outputs, broker
acknowledgements, or business completion.

Business completion can be supplied separately through a host-owned
`IDurableInputCompletionSource`; the SQL-file provider supplies only exact
lease renewal. See
[Durable-Input Workflow Completion](33-durable-input-workflow-completion.md).

The provider-neutral output-capture foundation and its first local provider are
documented in [Optional Durable Output Capture](27-durable-output-capture.md)
and [SQL-File Durable Outputs](28-sql-file-durable-outputs.md). Captured outputs
can now be leased serially to one host-owned handler through the separate
[Optional Durable Output Delivery](29-durable-output-delivery.md) capability.
That capability supplies fixed retry and lease-expiry recovery, but deliberately
does not include transports, durable workflow checkpoints, or exactly-once
completion semantics.

That explicit, opt-in boundary is now established by
`FluxFlow.Engine.DurableOutput`. The existing
`ApplicationPorts.ReceiveAsync(...)` and `ObserveAsync(...)` APIs remain live
taps rather than persistence contracts, so they are not a valid substitute for
durable store acceptance. Outputs without durable capture configured stay on
the existing lightweight path.

Old delivered tombstones and dead letters can be permanently removed through
the same provider singleton's separate `IDurableInputRetentionStore` alias.
Deletion is address-scoped, bounded, and transactional; it is never automatic
and does not add a schema version. See
[Durable Terminal Retention](36-durable-terminal-retention.md).
