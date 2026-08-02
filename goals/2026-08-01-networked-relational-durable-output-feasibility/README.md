# Goal: Prove A Networked Relational Durable-Output Provider With Direct SQL

Date: 2026-08-01
Status: completed successfully

## Objective

Build and execute a bounded, non-production feasibility spike that implements
FluxFlow's existing durable-output capture, delivery, dead-letter inspection,
and replay contracts against a real networked relational database using direct,
parameterized SQL.

The spike must answer one architectural question with executable evidence:

> Can a multi-connection networked relational backend satisfy the complete
> provider-neutral durable-output contract cleanly, atomically, and without
> changing FluxFlow's Engine, runtime contracts, C# DSL, JSON model, dispatcher,
> or application configuration?

The target for this spike is SQL Server running in an isolated Linux container.
The client is the official `Microsoft.Data.SqlClient` package. Entity Framework
Core, Dapper, a generic repository, and a cross-database SQL abstraction are not
used.

This is deliberately not a production provider release. It produces an
internal non-packable integration project, a reproducible real-database runner,
provider-conformance evidence, provider-specific risk tests, documentation, and
a promote/stop decision. It must not add a production package, public API,
package manifest entry, default-solution Docker dependency, or public
registration method.

This README is the complete executable specification. No implementation or test
source may be added before this file exists.

## Why Direct SQL

Durable output is a small transactional state machine, not ordinary CRUD. Its
critical operations require explicit control over:

- unique capture and no-overwrite content conflict handling;
- deterministic one-record lease selection;
- update locks, locked-row skipping, and one-winner concurrency;
- exact key/state/token/expiry compare-and-set transitions;
- transactional lazy initialization and schema ownership;
- binary key ordering and bounded keyset pagination;
- cancellation immediately before commit and unambiguous ownership transfer;
- dead-letter generation compare-and-set replay; and
- provider error, connection, transaction, and resource ownership.

An ORM would still require raw SQL for those operations while adding model
conventions, tracking, design-time migration tooling, and a larger dependency
graph. Direct SQL makes every state predicate, lock, index, transaction, and
mapped field reviewable in one place.

## Architectural Principles

- Apply KISS, SRP, OCP, ISP, IoC, and explicit ownership pragmatically.
- Depend only on the existing provider-neutral durable-output contracts.
- Use the current stable official client package, version `7.0.2`, isolated to
  the spike through a local central-package import.
- Keep the spike non-packable and outside `FluxFlow.sln`; normal builds and test
  runs must not require Docker, a network port, license acceptance, or a running
  database.
- Give the spike one explicit project command and one explicit container runner.
- Keep the implementation ordinary C#: sealed store, immutable connection
  settings, parameterized commands, explicit transactions, small row readers,
  and idempotent disposal.
- Avoid reflection, assembly scanning, service location, dynamic provider
  discovery, generated proxies, runtime expression compilation, static mutable
  registries, hidden retries, and convention-based schema discovery.
- Avoid a generic repository, generic SQL dialect, universal persistence
  abstraction, shared database base class, or provider switch.
- Do not move backend configuration into `FluxFlowApplicationOptions`.
- Do not reuse SQLite SQL, assumptions, exception types, or lifecycle behavior.
  Reuse only the public contracts and shared conformance suites.
- Treat every real-database test as an integration test and make its external
  ownership explicit. Do not mock the database protocol.

## Scope And Repository Placement

Create one isolated directory:

```text
spikes/
  FluxFlow.Engine.DurableOutput.RelationalSpike/
    Directory.Packages.props
    FluxFlow.Engine.DurableOutput.RelationalSpike.Tests.csproj
    README.md
    run-integration.ps1
    RelationalDurableOutputStore.cs
    RelationalDurableOutputSchema.cs
    RelationalDurableOutputRows.cs
    RelationalTestDatabase.cs
    RelationalIntegrationEnvironment.cs
    RelationalDurableOutput*ConformanceTests.cs
    RelationalDurableOutputInfrastructureTests.cs
```

File boundaries may be adjusted when one file becomes too large, but preserve
these cohesive responsibilities:

- store orchestration and public contract methods;
- schema creation/validation;
- command parameters and row reconstruction;
- real-server/database lifecycle;
- shared conformance adapters; and
- provider-specific integration tests.

The project must:

- target `net10.0`;
- set `IsPackable` to `false`;
- reference `src/FluxFlow.Engine.DurableOutput`;
- reference `tests/FluxFlow.Engine.DurableOutput.Tests` for the reusable suites;
- use the repository's xUnit, Shouldly, and test-SDK versions;
- add only the official SQL client as a new spike-local dependency;
- not reference the SQL-file provider;
- not be added to `FluxFlow.sln`, `eng/packages.json`, the public API baseline,
  the changelog release inventory, or package release automation.

The local `Directory.Packages.props` must import the repository root central
versions and add only `Microsoft.Data.SqlClient` `7.0.2`. This keeps the
experimental dependency out of the default production and release graph.

## Real Database Runner

Add a small PowerShell runner that owns the disposable test server process.

Required behavior:

- require an explicit `-AcceptLicense` switch before starting the container;
- verify Docker is callable before making changes;
- use `mcr.microsoft.com/mssql/server:2022-latest` by default, while allowing an
  explicit image override for repeatability or future CI pinning;
- create a unique neutral container name;
- generate a strong ephemeral administrator password without printing it;
- bind the database port only to loopback using a Docker-assigned host port;
- set Developer edition and required container environment values;
- discover the assigned host port rather than assuming 1433 is free;
- pass the administrator connection string to the test process only through
  `FLUXFLOW_RELATIONAL_SPIKE_CONNECTION_STRING`;
- run the spike project in Release through `dotnet test`;
- always remove the container in `finally`, including failed build, readiness,
  or test execution;
- never write the password or connection string to a file, source, log, test
  result, or documentation;
- return the test process exit code; and
- perform no Docker or database work when `-AcceptLicense` is absent.

The runner may rely on Docker pulling a missing image. It must not install
Docker, change machine execution policy permanently, expose the server on every
interface, create a persistent volume, or retain test data after completion.

## Integration Environment And Database Isolation

The test project reads exactly one environment variable:

```text
FLUXFLOW_RELATIONAL_SPIKE_CONNECTION_STRING
```

The value identifies the server and an administration database. Tests must fail
clearly and without revealing the value when it is absent, malformed, or cannot
connect. Do not silently skip tests.

Use one small process-local readiness gate:

- attempt a bounded connection until the server is ready or a fixed overall
  timeout expires;
- use cancellation-aware delay only for server-start readiness, never for test
  synchronization or lease races;
- report a stable readiness failure without printing credentials;
- perform readiness once per test process; and
- keep the connection closed between attempts.

Every conformance test receives a fresh database:

- generate a unique validated database name;
- create it from the administration connection;
- explicitly keep read-committed snapshot disabled for the tested locking
  strategy;
- create a database-scoped connection string without rebuilding credentials by
  string concatenation;
- dispose all store instances before cleanup;
- clear only the tested connection pool where required for deterministic drop;
- force remaining test-database sessions out and drop the database; and
- make cleanup idempotent and best-effort without hiding the original test
  failure.

Shared conformance tests may run serially in this spike to keep local resource
usage predictable. Their own concurrency scenarios still create multiple real
connections/stores and must prove one-winner behavior.

## Store Shape And Lifetime

Add one internal sealed `RelationalDurableOutputStore` implementing:

- `IDurableOutputStore`;
- `IDurableOutputDeliveryStore`;
- `IDurableOutputDeadLetterStore`; and
- `IAsyncDisposable`.

The store receives one validated connection string. It must:

- create a new connection per public operation;
- pool through the official client defaults rather than owning a permanent
  connection;
- use one per-instance asynchronous schema-initialization gate;
- coordinate initialization across store instances through a transaction-owned
  database application lock;
- reject every public operation after disposal;
- make disposal idempotent;
- not clear a global pool or dispose resources owned by another store;
- not start background work or retain timers/tasks; and
- not log or persist credentials, connection strings, payload secrets, or
  exception text as durable state.

The store may expose one internal exact-key capture reader solely for adapting
the existing capture conformance context. It must not add a new production
contract.

## Schema Version 1

The spike owns three `dbo` tables:

- `fluxflow_relational_output_schema`;
- `fluxflow_relational_outputs`;
- `fluxflow_relational_output_deliveries`.

The metadata table contains exactly one singleton row and version `1`.

Use `Latin1_General_100_BIN2` explicitly for application-address and message-id
columns and comparisons. Do not depend on the database's default collation.

### Capture table

Use a composite primary key of application address and message id. Persist the
complete immutable envelope:

- canonical application address;
- message id;
- contract name;
- value/error discriminator;
- payload JSON;
- structured error code, message, category, transient flag, and details JSON;
- trace id;
- original timestamp UTC ticks and offset minutes;
- capture timestamp UTC ticks and offset minutes;
- optional correlation id;
- optional causation id;
- headers JSON; and
- envelope schema version.

Required checks include:

- non-empty bounded identifiers and names;
- schema version strictly positive;
- both offsets in `-840..840`;
- value rows have no structured error fields; and
- error rows have all required structured error fields.

JSON is stored as Unicode text and parsed with `System.Text.Json`. Parameters
must be explicitly typed and sized where bounded. Never interpolate values or
identifiers supplied by an envelope.

### Delivery table

Use the same binary composite primary key and a foreign key to the capture row.
Store:

- state (`1 Pending`, `2 Leased`, `3 Completed`, `4 DeadLettered`);
- exact next-attempt UTC ticks and offset;
- nullable lease token and owner;
- nullable leased-at and lease-until ticks/offsets;
- non-negative attempt;
- nullable delivered timestamp ticks/offset;
- nullable stable dead-letter reason;
- nullable dead-letter timestamp ticks/offset; and
- non-negative dead-letter generation.

Checks must enforce mutually consistent state shapes:

- Pending has no lease/completion/dead-letter data;
- Leased has a complete lease, positive attempt, and no terminal data;
- Completed has no lease/dead-letter data, positive attempt, and a complete
  delivered timestamp;
- DeadLettered has no lease/completion data, positive attempt, a defined reason,
  a complete dead-letter timestamp, and positive generation;
- every stored offset is in `-840..840`.

Add two indexes:

- eligibility by state, next-attempt time, lease-expiry time, address, and
  message id; and
- current dead letters by state, dead-letter UTC time descending, binary
  address, and binary message id.

### Initialization and validation

Initialization is lazy on the first operation. Under one explicit transaction:

1. acquire an exclusive transaction-owned application lock;
2. inspect all three owned objects;
3. if none exist, create the exact version-1 schema and indexes;
4. if only some exist, reject the partial schema without repair;
5. if all exist, require the exact singleton version and required columns,
   primary/foreign keys, and indexes;
6. perform a final cancellation check; and
7. commit with a non-cancelable token after ownership transfers to the database.

Reject missing metadata, duplicate metadata, version `0`, future versions,
missing required indexes, incompatible columns, or partially deleted objects.
Do not drop, downgrade, repair, or recreate a recognized-but-invalid schema.

There is no predecessor schema to migrate in this first spike. The decision
record must state that production promotion requires a maintained explicit
migration path beginning with the first future schema change; this round proves
fresh ownership and deterministic incompatibility rejection only.

## Capture Algorithm

`EnqueueAsync` must:

- reject a null envelope and honor pre-cancellation before schema/database work;
- initialize/validate the schema;
- begin an explicit transaction;
- read the composite key with update/range protection;
- insert an absent record and return `Enqueued`;
- compare an existing complete envelope with `HasSameContent(...)`;
- return `AlreadyExists` for equivalent content without overwrite;
- return `Conflict` for different content without overwrite;
- preserve the first capture timestamp and all first-winner fields;
- perform a final cancellation check before commit; and
- commit non-cancelably only after the result is fixed.

Concurrent same-key equivalent writes must produce exactly one `Enqueued` and
the remainder `AlreadyExists`. Concurrent different-content writes must retain
one complete winner and report conflicts without duplicate-key leakage.

## Lease And Backfill Algorithm

`TryLeaseAsync` must execute in one explicit read-committed transaction:

1. backfill missing delivery rows from immutable captures as Pending using
   range-protected, duplicate-safe SQL;
2. select at most one due Pending or exactly expired Leased row in capture-time,
   binary address, binary message-id order;
3. use update locking, locked-row skipping, and row-lock intent suitable for a
   work queue;
4. atomically update the chosen row to Leased with a fresh application-generated
   token, exact owner/timestamps, and attempt `+1`;
5. reconstruct the complete captured envelope inside the same transaction;
6. perform a final cancellation check; and
7. commit, then return the exact lease.

The implementation must account for the documented constraints of `READPAST`:
use a compatible isolation level, keep read-committed snapshot disabled for the
test database, and combine the queue hints explicitly rather than assuming
provider defaults.

No polling, internal retry loop, random delay, batch lease, prefetch, or worker
is part of the store.

## Settlement Algorithms

Completion, retry, and dead-letter settlement are mutually exclusive atomic
updates guarded by:

- exact composite key;
- state `Leased`;
- exact current lease token; and
- `lease_until > transition_time`.

On one updated row:

- completion stores the exact delivered timestamp, clears the lease, and keeps
  a completed tombstone;
- retry stores the exact next-attempt timestamp, clears the lease, and preserves
  the existing attempt until the next lease increments it;
- dead-letter stores the exact stable reason/time, clears lease/completion data,
  and increments generation by one.

When no row updates, inspect the current row in the same transaction and return:

- `NotFound` when the delivery key is absent;
- `InvalidState` when the row is not currently Leased; or
- `LeaseLost` for a wrong token or expired lease.

Do not mutate the actual row when reporting a non-applied result. A matching
predicate that reports no update despite a matching inspected state is a
corruption/contract failure, not a silent status.

## Dead-Letter Inspection And Replay

### List

`ListAsync` must:

- select metadata columns only;
- use bound parameters for every filter;
- apply exact address and reason filters independently and together;
- apply inclusive `DeadLetteredFrom` and exclusive `DeadLetteredBefore` bounds
  by UTC instant;
- order by dead-letter UTC ticks descending, then binary address and message id
  ascending;
- implement the same mixed-direction keyset predicate as the public cursor;
- request `PageSize + 1` rows;
- return exact `HasMore` and last-returned-item cursor behavior; and
- never select payload, headers, structured error details, or credentials.

### Get

`GetAsync` joins the current dead-letter row to its immutable capture and returns
the complete exact envelope plus attempt, reason, time/offset, and generation.
Missing and non-dead-letter keys return `null`.

### Replay

`ReplayAsync` performs one exact state/generation compare-and-set:

- require key, state DeadLettered, and expected generation;
- return the row to Pending;
- store the exact requested next-attempt time;
- reset attempt to zero;
- clear lease, completion, reason, and dead-letter time;
- preserve the complete envelope and generation; and
- commit once.

When no row updates, return exact `NotFound`, `NotDeadLettered`, or
`GenerationMismatch` without mutation. Two concurrent replay requests for one
generation must have one `Replayed` winner.

## Cancellation, Transactions, And Errors

- Pass caller cancellation through connection open, command execution, reads,
  and pre-commit work.
- Check cancellation immediately before every commit.
- After the final check, commit with `CancellationToken.None` so a committed
  state is not reported ambiguously as canceled.
- Roll back through disposal on pre-commit cancellation or failure.
- Do not catch `OperationCanceledException` as a database failure.
- Wrap provider failures only where a stable operation name materially helps;
  retain the original exception as `InnerException` and do not include SQL,
  credentials, payloads, headers, connection strings, or provider exception
  messages in the stable outer message.
- Do not add automatic retry or transient-fault policy in the spike. Record the
  exact provider error observations for the promotion decision.

## Required Shared Conformance Tests

Create three thin sealed subclasses in the spike project:

- capture conformance;
- delivery conformance; and
- dead-letter conformance.

Each subclass must contain construction/cleanup only and supply one fresh real
database/store context. It must not copy or override behavioral tests.

All inherited tests must be discovered and pass:

- capture idempotency, address scoping, content conflict, cancellation, and null
  guard;
- 12 delivery methods for eligibility, exact boundaries, lease recovery,
  terminal ineligibility, completion/retry CAS, concurrency, and cancellation;
- 13 dead-letter methods for settlement, filters, pagination, exact lookup,
  replay/generation, concurrency, and cancellation.

## Required Provider-Specific Tests

Use real databases and multiple real connections. Add focused tests for risks
not owned by the shared contract:

### Environment and isolation

- missing environment variable fails clearly without revealing a value;
- one fresh database is created and dropped per context;
- cleanup removes databases and is idempotent;
- credentials never appear in test display names, assertion messages, or
  runner output.

### Schema

- first operation creates exact version-1 tables, binary key collation,
  constraints, foreign key, and both indexes;
- concurrent first use by multiple stores initializes once;
- future version is rejected without downgrade;
- partial schema and missing required index are rejected without repair;
- capture and delivery rows exhibit the exact persisted state encoding;
- schema ownership remains inside the test database.

### Transactions and concurrency

- multiple stores/connections capture one same-key winner;
- multiple stores lease one output once;
- multiple eligible outputs are leased without duplicate ownership or loss;
- completion/retry/dead-letter/replay produce one persisted winner under
  concurrent calls;
- an external transaction lock demonstrates bounded command timeout/failure and
  recovery after release without corrupting state;
- database drop/recreate demonstrates no static state or connection leak.

### Persistence and fidelity

- value and error envelopes survive store disposal/reopen with every JSON,
  header, lineage, timestamp, and offset field exact;
- completion tombstone survives reopen;
- dead-letter generation and replay schedule survive reopen;
- binary address/message ordering is independent of database default collation;
- list summaries remain metadata-only at the actual SQL projection boundary.

Do not add tests whose only assertion is non-null or no exception. Assert exact
keys, statuses, tokens, attempts, timestamps/offsets, state, generation, row
counts, index/constraint metadata, and absence of forbidden mutation.

## Mandatory Test Workflow

Before test-source edits:

1. verify the spike project references, target framework, xUnit, Shouldly, and
   real-client dependency;
2. run the Roslyn static source-to-test pairing analyzer exactly once at the
   narrowest spike root after the implementation sources exist but before test
   methods are written;
3. record its JSON counts, pairings, suggested paths, and static-heuristic caveat
   in `.testagent/research.md`;
4. record the complete requirement-to-test map in `.testagent/plan.md`; and
5. maintain final results, setup observations, pseudo-mutation review, gap
   analysis, and assertion-quality audit in `.testagent/status.md`.

The implementation and tests may be coordinated so production spike sources
land before the analyzer, but no test method may be written before the analyzer
and plan gates.

Required verification:

- restore/build the spike project independently;
- prove all inherited test names are discovered in the spike harness;
- run the real-database suite through `run-integration.ps1 -AcceptLicense`;
- repeat the highest-risk concurrency group enough to expose obvious race
  assumptions without turning the suite into an unbounded soak test;
- run formatting verification;
- perform final pseudo-mutation analysis over capture, schema, lease,
  transition, list, lookup, and replay branches;
- perform assertion-quality analysis and strengthen every zero/trivial,
  timing-only, success-only, or self-referential test; and
- report exact generated test names for every requirement group.

The pairing analyzer is a source-to-test naming/reference heuristic, not line or
branch coverage.

## Documentation And Memory

Add a focused documentation-site page using the next appropriate number and add
it to `docs/README.md`. Update only the existing durable-output pages and package
READMEs where the feasibility result materially changes provider guidance.

Documentation must state:

- the spike passed or failed, based on actual evidence;
- direct SQL was chosen over EF Core/Dapper and why;
- the spike is non-packable and outside the default solution;
- how to run it explicitly and that license acceptance is required;
- the tested server/image and client version;
- which public contracts were satisfied unchanged;
- exact locking/isolation/schema assumptions;
- no production support or compatibility promise is created;
- no production registration, package, migrations-from-older-relational-schema,
  transient retry policy, credential system, or deployment automation exists;
- what must be completed before production promotion; and
- the promote/stop recommendation.

Update:

- `memory/00-index.md`;
- `memory/01-current-state.md`;
- `memory/07-progress-log.md`;
- one new detailed memory record with decisions, implementation shape, exact
  evidence, dependency and container details, limitations, and next step; and
- this goal README with final status and execution result.

Do not add a release changelog entry or package version because the spike is not
part of the supported product/release surface.

## Explicit Exclusions

Do not add:

- Entity Framework Core, Dapper, LINQ-to-database, a generic repository, or an
  ORM abstraction;
- a production provider project/package, release manifest entry, public API
  baseline entry, package version, or registration extension;
- a second database backend or dialect;
- a shared input provider in this round;
- runtime provider selection, reflection, assembly scanning, plugin loading, or
  service location;
- application-level database settings or changes to
  `FluxFlowApplicationOptions`;
- stored procedures, triggers, background cleanup, retention, purge, archive,
  compaction, automatic replay, or operator endpoint/UI/CLI;
- exponential backoff, retry package, circuit breaker, health-check package,
  telemetry package, or connection-secret package;
- batching, prefetch, parallel dispatcher work, leader election, sharding,
  partitioning, distributed transactions, exactly-once claims, workflow
  checkpoints, or producer/business-state atomicity;
- container persistence, deployment manifests, production credentials, or
  automatic license acceptance.

## Promotion Decision Criteria

Recommend production promotion only if all of these are true:

- all three shared conformance suites pass unchanged;
- real multiple-connection tests prove one-winner capture, lease, settlement,
  and replay behavior;
- exact binary ordering and keyset pages pass;
- schema initialization is concurrent-safe and invalid schemas are rejected
  without repair;
- envelope/state fidelity survives reopen;
- cancellation and commit ownership remain unambiguous;
- the implementation remains small, explicit, and reviewable without an ORM or
  generic abstraction;
- no core contract or Engine change is required; and
- the remaining production work is clearly bounded to options/registration,
  migration policy, supported server matrix, transient error policy,
  credentials/deployment documentation, packaging, and operational tests.

Recommend stopping or revisiting the boundary if conformance requires
provider-specific exceptions, hidden retries, changes to public contracts,
relaxed atomicity, ambiguous ordering, or a broad generic persistence layer.

## Validation And Completion Gates

The goal is complete only when:

1. This README exists before implementation edits.
2. The spike directory contains only non-packable, explicitly invoked artifacts.
3. The official client is the only new dependency and remains spike-local.
4. No EF Core, Dapper, reflection, service locator, generic repository, or
   dynamic provider code exists.
5. The store implements all three existing interfaces without production API
   changes.
6. The exact fresh schema, binary collation, state constraints, indexes, and
   application-lock initialization are verified on the real server.
7. Capture, delivery, dead-letter, replay, cancellation, ordering, and
   concurrency semantics pass all inherited suites unchanged.
8. Provider-specific real-database tests pass deterministically.
9. The runner requires explicit license acceptance, binds loopback only, hides
   credentials, and removes the container on success/failure.
10. The one-time analyzer, requirement map, gap analysis, pseudo-mutation audit,
    and assertion-quality audit are complete.
11. Spike build/test and formatting verification pass in Debug and Release where
    applicable.
12. The default `FluxFlow.sln` build/test behavior remains independent of Docker
    and the spike dependency.
13. Release governance, package manifest, public API baseline, and archive
    checks confirm no supported package leakage or version change.
14. Serialized non-incremental Debug and Release full-solution builds pass with
    zero warnings.
15. The serialized default Release test suite passes unchanged apart from no
    spike discovery, because the spike has its own explicit harness.
16. Documentation, documentation-site navigation, memory, and goal records are
    consistent with the evidence.
17. Final status/diff review confirms only intended spike/docs/memory/goal files
    were added or changed and all unrelated dirty-worktree changes were
    preserved.

## Required Final Report

Report:

- the saved goal README;
- exact spike files and dependency versions;
- direct-SQL architecture and schema/locking choices;
- inherited and provider-specific exact test names/counts;
- real container image/runtime evidence;
- concurrency, persistence, cancellation, and schema results;
- focused/default build and test counts;
- governance and no-package-leakage evidence;
- documentation and memory paths;
- a compact `Requirement | Evidence` table;
- confirmation that no EF Core/Dapper, runtime API, schema, package version,
  default-solution Docker dependency, or production registration was added;
- the promote/stop decision and bounded production follow-up; and
- any remaining limitation or environmental caveat.

Do not claim completion from compilation, mocked tests, or a container that only
started. Completion requires the full provider-neutral protocol and
provider-specific risks to pass against the real database.

## Execution Result

Completed successfully on 2026-08-01.

- The non-packable direct-SQL spike was created outside `FluxFlow.sln` and the
  release/package graph.
- The official client 7.0.2 is the only new direct dependency and is isolated
  by the spike-local central package import.
- All three existing durable-output interfaces were implemented without public
  API, Engine, workflow, C# DSL, JSON, dispatcher, or application-option
  changes.
- The license guard was proven to fail before Docker work when
  `-AcceptLicense` is absent.
- The real SQL Server 2022 container suite passed 65/65 tests with zero skips in
  1 minute 9 seconds. It covered all inherited conformance cases plus exact
  schema, lifecycle, persistence, binary ordering, multi-store concurrency,
  and external-lock failure/recovery.
- Focused Debug and Release builds covered seven projects with zero
  errors/warnings, and formatting verification passed.
- Serialized non-incremental Debug and Release solution builds covered 129
  projects with zero errors/warnings. The unchanged default Release suite
  passed 1,968/1,968 tests across 62 projects without discovering the spike.
- Inventory scans found no spike/client entry in the solution or package
  manifest, no forbidden ORM/reflection/service-location pattern in spike C#
  sources, and no retained disposable container.

The feasibility decision is **promote in a separate bounded production-provider
round**. This goal does not itself add a supported provider, registration,
migration history, transient retry policy, credentials/deployment system,
package/version, or compatibility promise. Detailed evidence and limitations
are recorded in `docs/31-networked-relational-durable-output-feasibility.md`,
`memory/278-networked-relational-durable-output-feasibility.md`, and
`.testagent/status.md`.
