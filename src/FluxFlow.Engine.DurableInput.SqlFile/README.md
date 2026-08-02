# FluxFlow.Engine.DurableInput.SqlFile

Production SQLite single-file storage for `FluxFlow.Engine.DurableInput`.
Choose this package when inputs must survive host restarts but the deployment
should remain local and self-contained. Continue using
`ApplicationPorts.SendAsync(...)` for the smallest in-process path, or provide
another `IDurableInputStore` when different persistence is required. Choose
`FluxFlow.Engine.DurableInput.TSql` for the built-in shared networked relational
provider.

## Registration

```csharp
services
    .AddFluxFlow(configuration)
    .AddFluxFlowDurableInput(input =>
    {
        input.BatchSize = 64;
        input.LeaseDuration = TimeSpan.FromSeconds(30);
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

To wait for an explicit workflow terminal signal, register one host-owned
completion source and add three flat settings:

```csharp
services.AddSingleton<IDurableInputCompletionSource, OrderCompletionSource>();

services.AddFluxFlowDurableInput(input =>
{
    input.AcknowledgementMode = DurableInputAcknowledgementMode.WorkflowCompleted;
    input.WorkflowCompletionTimeout = TimeSpan.FromMinutes(10);
    input.LeaseDuration = TimeSpan.FromSeconds(30);
    input.LeaseRenewalInterval = TimeSpan.FromSeconds(10);
});
```

The SQL-file provider already registers the required
`IDurableInputLeaseRenewalStore` capability. It is the same singleton as
`IDurableInputStore` and `IDurableInputDeadLetterStore`; there is no provider
callback or schema setting to add.

The SQL-file callback runs once during registration and becomes an immutable
`SqlFileDurableInputStoreOptions` snapshot. Registration only adds service
descriptors: it does not inspect the filesystem, create a directory, open a
connection, or initialize the schema. Equivalent repeated registration is
idempotent; conflicting options or another `IDurableInputStore` fail
immediately. The same singleton is also exposed as
`IDurableInputDeadLetterStore` and `IDurableInputLeaseRenewalStore`; no second
callback is required. The DI container owns the singleton store it creates.

## Options

| Option | Default | Purpose |
|--------|---------|---------|
| `DatabasePath` | required | Relative or explicitly allowed absolute database path. |
| `CreateDatabase` | `true` | Allows first use to create a missing database. |
| `CreateDirectory` | `true` | Allows first use to create a missing parent directory. |
| `AllowAbsoluteDatabasePath` | `false` | Opts into absolute paths. |
| `BusyTimeout` | 30 seconds | Maximum SQLite lock wait; must be positive and fit SQLite's millisecond range. |

The provider resolves and freezes the full database path when its singleton is
constructed. Filesystem and schema work remains deferred until the first store
operation.

## Persistence Contract

The provider stores the complete immutable durable envelope, retry state,
lease token and owner, attempt count, transition times, and structured failure.
The primary key is `(ApplicationAddress, MessageId)`. Equivalent duplicate
enqueue is accepted idempotently; different content under the same key is a
conflict. Delivered and dead-lettered rows remain tombstones in this version.

Leasing is a short SQLite write transaction. It selects due pending records and
expired leases in deterministic due/enqueue/key order, assigns new tokens, and
increments attempts before committing. Delivered, release, and dead-letter
changes are token-and-expiry compare-and-set operations. Independent store
instances coordinate through SQLite locking; no process-local lock is the
correctness boundary.

Lease renewal uses the same short transactional compare-and-set pattern. It
matches leased state, the exact token, and strict unexpired time, then changes
only `lease_until_utc_ticks` to the requested value. It never revives a missing,
expired, delivered, released, or dead-lettered record.

Schema initialization is lazy, versioned, transactional, and safe to repeat.
New databases use schema version 2. Existing version-1 databases are migrated
transactionally on first use by adding the dead-letter generation and bounded
listing index; all envelopes and Pending, Leased, Delivered, and DeadLettered
states are preserved. The schema version changes only when migration succeeds.
Databases written by a newer unsupported provider version are rejected rather
than recreated or downgraded.

## Inspect And Replay Dead Letters

```csharp
var deadLetters = provider.GetRequiredService<IDurableInputDeadLetterStore>();
var page = await deadLetters.ListAsync(new DurableInputDeadLetterQuery(
    address: ApplicationAddress.Parse("Orders.Submit.Input"),
    deadLetteredFrom: timeProvider.GetUtcNow().AddDays(-1),
    pageSize: 50));

var summary = page.Items[0];
var details = await deadLetters.GetAsync(summary.Key);
var now = timeProvider.GetUtcNow();
var result = await deadLetters.ReplayAsync(new DurableInputReplay(
    summary.Key,
    summary.Generation,
    replayedAt: now,
    nextAttemptAt: now));
```

The list query is bounded to 200 items, returns payload-free summaries, and
orders by dead-letter time descending followed by ordinal address and message
identity. Continue with `NextCursor`; the provider uses keyset rather than
offset pagination. Exact lookup returns full message content only while the row
is currently dead-lettered.

Replay is a single short write transaction. It succeeds only for the current
generation, resets `Attempt` to zero, clears active failure and lease state,
and schedules the unchanged envelope. A later dead-letter increments the
generation, so a stale operator request cannot replay a newer failure.

## Operations And Security

SQLite is a local, single-writer database. It is a good fit for one machine and
moderate ingress volume, not a substitute for a shared distributed store. Keep
transactions short, select a busy timeout appropriate to the host, and include
the database plus its active SQLite journal files in a coordinated backup.
Stop writers or use a SQLite-aware online-backup process; copying only the main
file during a write is not a valid backup strategy.

The database intentionally contains message payloads, error details, and
headers. Protect the directory with operating-system permissions and storage
encryption appropriate to the data. The provider does not put those values or
its connection string into logs.

Delivery remains at-least-once. A crash after Engine accepts a message but
before the provider marks it delivered can redeliver the same `MessageId`.
With a host-owned `IDurableInputCompletionSource`, this package supports the
optional workflow-completion acknowledgement mode. The source—not SQLite or
FluxFlow—defines and correlates the terminal business signal to the exact lease
token. The guarantee remains at-least-once: this package does not provide
exactly-once processing, durable workflow checkpoints, rollback, distributed
transactions, output durability, automatic or bulk replay, delivered-record
replay, automatic retention, dead-letter audit history, runtime revision
persistence, or distributed SQL coordination.

## Read-Only Status

The same singleton is available as `IDurableInputStatusStore`. It reports
pending/ready, leased/expired, delivered, and dead-letter counts at a
caller-supplied observation time. The call opens SQLite read-only, selects only
aggregate state/time data, and never creates a missing database, initializes
schema, changes a row, or returns message content.

## Explicit Terminal Retention

`IDurableInputRetentionStore` resolves to this same singleton. It permanently
deletes an address-scoped, deterministic batch of old delivered tombstones or
dead letters inside one immediate write transaction. Pending, leased,
replayed, and opposite-terminal rows are preserved. The capability adds no
schema version, table, index, worker, or retention setting.

Deleting a delivered tombstone lets the same durable identity be accepted as
new later. Deleting a dead letter removes its replay source. The host must
choose the exclusive cutoff and invoke bounded batches explicitly.
