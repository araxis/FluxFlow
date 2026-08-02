# Networked Relational Durable-Output Feasibility

FluxFlow's existing durable-output contracts have now been exercised unchanged
against a real multi-connection networked relational server. The result is a
successful feasibility spike. Its implementation has since been promoted into
the supported `FluxFlow.Engine.DurableOutput.TSql` production provider; this
page remains the historical feasibility evidence.

## Result

The isolated spike passed 65 of 65 real-database tests with no skips. The suite
inherits the complete provider-neutral capture, delivery, and dead-letter
conformance specifications and adds backend-specific schema, lifecycle,
persistence, ordering, concurrency, and lock-timeout tests.

The tested environment was:

- SQL Server 2022 Linux container image
  `mcr.microsoft.com/mssql/server:2022-latest`;
- `Microsoft.Data.SqlClient` 7.0.2;
- .NET 10;
- read-committed snapshot disabled for the tested queue-locking strategy; and
- one fresh disposable database per context.

No Engine, workflow, C# DSL, JSON model, dispatcher, application options, or
public durable-output contract changed. This is the strongest result of the
spike: the existing provider boundary is sufficient for a shared networked
database.

## Why Direct SQL

The provider is a small transactional state machine rather than ordinary CRUD.
Capture needs a unique composite key and range-protected insert-or-compare.
Delivery needs deterministic one-row selection, update locks, locked-row
skipping, lease expiry, and exact compare-and-set settlement. Replay needs a
state-and-generation compare-and-set.

Direct parameterized SQL keeps those locks, predicates, transaction boundaries,
indexes, and projections visible. Entity Framework Core and Dapper were not
added. An ORM would not remove the provider-specific locking SQL and would add
model conventions, tracking/migration machinery, or another dependency layer.

## Proven Shape

The non-packable store implements the existing:

- `IDurableOutputStore`;
- `IDurableOutputDeliveryStore`; and
- `IDurableOutputDeadLetterStore`.

It opens one pooled connection per operation, owns no background work, and uses
one per-instance initialization gate plus a transaction-owned database
application lock across instances. Schema version 1 owns exactly three `dbo`
tables for metadata, immutable captures, and delivery state.

Application address and message ID use explicit
`Latin1_General_100_BIN2` collation. Initialization creates an entirely absent
schema or validates the complete known shape. Partial objects, future/corrupt
metadata, incompatible columns, and missing indexes are rejected without
repair, downgrade, or data loss.

Leasing runs in an explicit read-committed transaction. Missing delivery rows
are backfilled from immutable captures, and the queue query uses update locks,
locked-row skipping, and row-lock intent. The test database keeps
read-committed snapshot off because that is an explicit assumption of this
strategy. Completion, retry, dead-lettering, and replay use exact atomic
predicates. Cancellation is checked immediately before commit; commit then uses
a non-cancelable token so accepted ownership is not reported ambiguously.

Dead-letter listing requests one bounded keyset page and selects metadata only.
Exact lookup reconstructs the full envelope. Replay preserves the envelope and
generation, clears terminal state, resets attempt to zero, and schedules the
next lease explicitly.

## Run The Spike

The project is intentionally outside `FluxFlow.sln` and the package/release
graph. Normal restore, build, and test commands do not require Docker, a port,
a database, or license acceptance.

Run it explicitly from the repository root:

```powershell
.\spikes\FluxFlow.Engine.DurableOutput.RelationalSpike\run-integration.ps1 -AcceptLicense
```

The switch is mandatory before Docker work begins. The runner creates a strong
ephemeral administrator password, binds a Docker-assigned port to loopback,
passes the connection only through the test-process environment, and removes
the container in `finally`. It creates no volume and retains no test data.

## What This Does Not Add

The spike adds no production package, registration extension, public option,
package version, release-manifest entry, API-baseline entry, deployment
manifest, credential system, automatic migration from an older relational
schema, transient retry policy, health check, telemetry package, retention,
purge, bulk replay, or operator UI/endpoint.

It also makes no exactly-once, producer/business-state atomicity, workflow
checkpoint, distributed transaction, batching, or parallel-dispatch claim.
Delivery remains leased and at-least-once; destinations still own idempotency.

## Promotion Recommendation

The result supports promotion to a separately planned production provider: all
three conformance suites passed unchanged, multiple real connections produced
one-winner capture/lease/settlement/replay behavior, schema rejection was
deterministic, binary ordering and keyset paging passed, state survived reopen,
and no broad persistence abstraction was required.

The production result adds immutable public options and flat registration, an
explicit migration policy, a supported/tested server scope, bounded client
connection resiliency, deployment and credential guidance, operational tests,
and package/release governance. See
[T-SQL Durable Outputs](32-tsql-durable-outputs.md) for the supported surface.

The complete executable specification and evidence live in
`goals/2026-08-01-networked-relational-durable-output-feasibility/README.md` and
`memory/278-networked-relational-durable-output-feasibility.md`.
