# Production T-SQL Durable-Output Provider

Date: 2026-08-01

## Outcome

The successful networked relational feasibility spike is promoted into the
supported, independently packaged `FluxFlow.Engine.DurableOutput.TSql` 1.0.0
provider. It implements the existing `IDurableOutputStore`,
`IDurableOutputDeliveryStore`, and `IDurableOutputDeadLetterStore` contracts
without changing Engine, workflows, the C# DSL, JSON, dispatcher behavior, or
`FluxFlowApplicationOptions`.

The provider is strictly opt-in. Applications that do not reference and call
its registration extension gain no SQL client dependency, remote connection,
schema operation, hosted service, or runtime cost. The obsolete executable
spike was removed after its implementation and tests were promoted; its goal,
documentation, memory, and recorded test evidence remain historical records.

The accepted executable specification is
`goals/2026-08-01-production-tsql-durable-output-provider/README.md`.

## Public Boundary And Registration

The one flat registration surface is:

```csharp
services.AddFluxFlowTSqlDurableOutput(options =>
{
    options.ConnectionString = configuration.GetConnectionString("FluxFlow")!;
    options.CommandTimeout = TimeSpan.FromSeconds(30);
    options.SchemaLockTimeout = TimeSpan.FromSeconds(30);
    options.ConnectRetryCount = 1;
    options.ConnectRetryInterval = TimeSpan.FromSeconds(1);
    options.SchemaManagement = TSqlDurableOutputSchemaManagement.CreateOrMigrate;
});
```

The callback configures one short-lived builder. Resolution produces one
immutable `TSqlDurableOutputStoreOptions` record and one concrete singleton
aliased to all three store interfaces. Equivalent repeated registration is
idempotent after connection-string normalization; conflicting, tampered, or
pre-existing interface registration fails before mutating the service
collection. Registration, provider construction, and service resolution
perform no database I/O.

Defaults and bounds are explicit: a 30-second command timeout, a 30-second
schema-lock timeout, one open retry after a one-second interval,
`CreateOrMigrate` schema management, and bounded whole-unit values. The
official connection-string builder normalizes the connection string and
overrides only the provider-owned connection retry fields. `ToString()` never
exposes the connection string.

## Runtime And Schema

`TSqlDurableOutputStore` is public, sealed, idempotently disposable, and owns
no background work. It opens one pooled connection per operation and uses
direct parameterized `Microsoft.Data.SqlClient` commands only. There is no
Entity Framework Core, Dapper, generic repository, reflection, assembly
scanning, dynamic proxy, ambient transaction, or provider-selection layer.

The version-1 schema retains the proven table names and exact layouts from the
spike. Initialization is guarded per store and by a transaction-owned exclusive
database application lock. `CreateOrMigrate` creates an entirely absent schema
or runs an explicit known migration; `ValidateOnly` never creates objects.
Partial, unversioned, future, older-without-a-known-migration, corrupt, or
incompatible schemas fail closed. Exact columns, sizes, nullability, binary
collations, primary/foreign keys, trusted checks, and index key shapes are
validated. Read-committed snapshot isolation is rejected because the leasing
protocol intentionally depends on `READPAST` work-queue semantics.

Capture remains unique insert-or-compare with no overwrite. Leasing remains
due-time ordered with binary-key tie breaking, exclusive expiring tokens, and
locked-row skipping. Completion, retry, dead-letter settlement, lookup,
metadata-only keyset listing, and generation-protected replay retain the shared
provider semantics. Provider-specific field-length validation happens before
I/O. Cancellation is propagated to connection open and commands; mutations
check immediately before commit, then commit without a cancellable ambiguous
ownership handoff.

Connection-open retry is deliberately small and bounded. Commands and
transactions are never automatically retried because an interrupted commit can
be ambiguous; callers reconcile through stable keys and idempotent capture or
settlement semantics.

## Test And Release Evidence

The mandatory static pairing pass ran once before test editing. The fast suite
contains 59 logical cases executed on both `net8.0` and `net10.0`: 118/118
passed with zero warnings. It covers option defaults/bounds/normalization/
redaction, immutable snapshots, atomic and idempotent registration, exact
interface aliases, side-effect-free resolution, preflight limits, and disposal.

The explicit production integration project is outside the default solution
and references both the production provider and shared conformance project. It
duplicates no provider implementation. Its license-gated runner passed 73/73
Release `net10.0` cases with zero failures and zero skips in 4 minutes 54
seconds against:

- `mcr.microsoft.com/mssql/server:2022-latest`;
- digest
  `sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`;
- `Microsoft.Data.SqlClient` 7.0.2; and
- fresh isolated databases with no retained container after cleanup.

The 73 cases include all capture, delivery, and dead-letter conformance cases;
exact schema and schema-mode behavior; concurrent initialization; incompatible
and corrupt schema rejection; RCSI rejection; persistence/reopen fidelity;
binary ordering; metadata-only projection; cancellation; configured timeout
and lock recovery; multi-store capture and leasing; and completion/replay
races. The assertion audit found 63 locally declared tests, at least 303 direct
assertion expressions plus disposal helpers, zero assertion-free/trivial-only/
self-referential tests, and 11 of 12 meaningful assertion categories.

Repository validation completed with:

- Debug and Release solution builds: 131 projects, zero warnings/errors;
- default Release suite: 2,086/2,086 passed across 64 test projects;
- package manifest tests: 4/4 passed across 42 package entries;
- public API acceptance: 34 intentional declarations, accepted and rechecked;
- package creation: `FluxFlow.Engine.DurableOutput.TSql.1.0.0.nupkg` plus symbols;
- package contents: README, icon, `net8.0`/`net10.0` assemblies, and manifest only;
- exact package dependencies: DurableOutput 2.0.0, SqlClient 7.0.2, and DI
  abstractions 10.0.7 for both frameworks;
- package release preflight and a fresh-cache archive/consumer/feed dry-run:
  passed; and
- binary compatibility preparation: passed. A comparison baseline is not
  applicable until an initial 1.0.0 package has been published.

## Operational Boundary

The host owns credential sourcing/rotation, least-privilege grants, deployment
ordering, backups, capacity, retention, monitoring, and destination
idempotency. Production deployments should normally migrate with a privileged
identity and run with `ValidateOnly` under a narrower runtime identity.

Delivery is at-least-once. The provider adds no input persistence, transport,
retention/purge worker, bulk replay, batching, parallel dispatcher, distributed
transaction, business-state atomicity, workflow checkpoint, exactly-once
claim, database discovery, or additional database engine. Other engines remain
separate adapters behind the same provider-neutral durable-output contracts.
