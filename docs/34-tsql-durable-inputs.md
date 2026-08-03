# T-SQL Durable Inputs

`FluxFlow.Engine.DurableInput.TSql` is the opt-in production provider for
durable ingress shared by multiple application processes. It implements the
existing provider-neutral input store, dead-letter, and exact lease-renewal
capabilities with direct parameterized SQL. It does not change Engine,
workflow definitions, the JSON or C# authoring surfaces, components, or
`FluxFlowApplicationOptions`.

## Choose A Provider

| Requirement | Provider |
|-------------|----------|
| Lowest-overhead current-process delivery | `ApplicationPorts.SendAsync(...)` |
| One local host and one portable database file | `FluxFlow.Engine.DurableInput.SqlFile` |
| Multiple processes or machines sharing one durable inbox | `FluxFlow.Engine.DurableInput.TSql` |
| A different persistence engine or operational model | Implement the provider-neutral contracts in a separate adapter |

Both built-in providers expose `IDurableInputStore`,
`IDurableInputDeadLetterStore`, `IDurableInputLeaseRenewalStore`, and
`IDurableInputStatusStore` through one
provider singleton. The T-SQL package is an optional adapter: applications that
do not reference and register it load no SQL client and perform no network or
schema work.

## Flat Registration

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
    .AddFluxFlowTSqlDurableInput(store =>
    {
        store.ConnectionString =
            configuration.GetConnectionString("FluxFlowDurableInput");
        store.CommandTimeout = TimeSpan.FromSeconds(30);
        store.SchemaLockTimeout = TimeSpan.FromSeconds(30);
        store.ConnectRetryCount = 1;
        store.ConnectRetryInterval = TimeSpan.FromSeconds(1);
        store.SchemaManagement =
            TSqlDurableInputSchemaManagement.ValidateOnly;
    })
    .AddFluxFlowDurableInputContract<OrderSubmitted>("orders.submitted.v1");
```

Each callback is flat and owns one concern. The temporary mutable provider
builder is discarded during registration; the registered options and resolved
runtime settings are immutable. Registration validates ownership and settings
before changing DI, is idempotent for normalized-equivalent settings, and does
not open a connection. All three provider interfaces resolve to the exact same
singleton.

## Schema Management

The provider owns two versioned objects under `dbo`: one schema metadata table
and one complete envelope/operational-state table. Version 1 contains ordinal
address/message identity, the exact persisted value-or-error envelope, retry
and attempt state, lease owner/token/times, delivery and failure state, and
dead-letter generation. Named constraints enforce legal state shapes. Named
indexes support due leasing and newest-first keyset dead-letter listing.

`CreateOrMigrate` creates a completely absent known schema. Initialization
holds a transaction-owned application lock with the configured bounded wait,
then creates and validates the schema in one transaction. It never creates a
database outside the configured catalog.

`ValidateOnly` is intended for deployments where migration tooling or a
separate privileged identity owns DDL. It performs no create, alter, drop,
repair, or version write. Missing, partial, malformed, unversioned, future, and
otherwise incompatible schemas fail closed in either mode; the provider never
guesses or deletes persisted state.

The initialization identity must be able to read catalog metadata and acquire
the application lock. `CreateOrMigrate` additionally requires permission to
create the two tables, constraints, and indexes. A least-privilege deployment
can run creation separately and give the application identity only the data,
catalog-read, and application-lock permissions needed by `ValidateOnly` and
normal operations.

## Concurrency And Recovery

Idempotent enqueue uses a serializable transaction and ordinal composite key.
Equivalent repeated content returns `AlreadyExists`; different content for the
same key returns `Conflict` without replacing the first envelope.

Batch leasing uses locking read committed with `UPDLOCK`, `READPAST`, and
`ROWLOCK`. Due pending rows and expired leases are selected in deterministic
effective-due, enqueue-time, and ordinal-key order. One atomic update assigns a
new exact token and increments attempt. Concurrent hosts skip locked work and
cannot receive the same active lease. `MaxCount` is an upper bound, not a
fairness or minimum-count guarantee: a caller can receive fewer rows, including
zero, while another lease transaction holds eligible row locks. Those skipped
rows remain eligible and are available to the next lease call after the lock
owner commits or rolls back.

Complete, release, dead-letter, and renewal operations atomically match key,
leased state, exact token, and an expiry later than the operation time. Renewal
changes only the requested expiry and cannot revive an expired or settled row.
Replay matches the exact current dead-letter generation, preserving the
envelope and preventing a stale operator action from reopening a later
occurrence.

The configured database must use locking read committed.
`READ_COMMITTED_SNAPSHOT` is rejected because it changes the semantics of the
provider's `READPAST` cooperative-leasing protocol.

## Connections, Timeouts, And Failures

The store opens an operation-scoped pooled connection. It owns no server
resource or process-wide connection and does not clear shared pools on
disposal. `CommandTimeout` applies to provider commands;
`SchemaLockTimeout` bounds schema-lock acquisition. `ConnectRetryCount` and
`ConnectRetryInterval` configure only official-client connection-open
resiliency.

FluxFlow deliberately does not catch and retry state-changing commands,
transactions, or commits. A connection break around commit can be ambiguous;
blind replay could violate lease or settlement semantics. The original failure
is surfaced and the caller or dispatcher recovers through persisted state and
lease expiry.

Connection strings and credentials are host-owned. Source them from the host's
normal secure configuration, rotate them outside FluxFlow, and never include
them in logs. Provider option text redacts the connection string. The host also
owns backups, restore testing, capacity, index maintenance, monitoring,
retention, archival, and deletion policy.

## Delivery Guarantee

Delivery remains at-least-once. A process may fail after Engine acceptance or
an explicit workflow completion but before the delivered tombstone commits.
After expiry another host can lease the preserved message again. Consumers
performing non-idempotent effects must deduplicate using the stable message
identity or use an application-specific transactional boundary.

The provider does not persist internal workflow/node state, checkpoints,
revisions, broker acknowledgements, or application side effects. It does not
provide exactly-once processing or a distributed transaction.

## Workflow Completion

`WorkflowCompleted` acknowledgement works without provider-specific dispatcher
logic. The T-SQL singleton already exposes `IDurableInputLeaseRenewalStore`.
While the dispatcher waits for the host-owned exact-attempt completion source,
renewal updates only the current token's expiry. See
[Durable-Input Workflow Completion](33-durable-input-workflow-completion.md).

## Explicit Real-Server Validation

The default solution remains network- and container-free. The separate
`tests/FluxFlow.Engine.DurableInput.TSql.IntegrationTests` project inherits all
three provider-neutral conformance suites and adds schema, restart,
multi-instance, locking, corruption, cancellation, and concurrency coverage.
Its explicit runner requires affirmative image-license acceptance, creates an
isolated disposable server/database by default, supports an externally managed
connection string, requires zero skipped tests, never prints credentials, and
always removes owned infrastructure unless diagnostic retention is explicitly
requested.

## Explicit Terminal Retention

The same singleton implements `IDurableInputRetentionStore`. A host can
permanently delete an address-scoped bounded batch of old delivered tombstones
or dead letters in one locking transaction. No policy or schedule is inferred,
and the existing schema is unchanged. See
[Durable Terminal Retention](36-durable-terminal-retention.md) for cutoff,
deduplication, replay, and repetition semantics.
