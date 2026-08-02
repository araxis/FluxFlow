# Networked Relational Durable-Output Feasibility

Date: 2026-08-01

## Outcome

The existing FluxFlow durable-output boundary is sufficient for a shared
multi-connection networked relational database. A bounded direct-SQL spike
implemented `IDurableOutputStore`, `IDurableOutputDeliveryStore`, and
`IDurableOutputDeadLetterStore` without changing Engine, workflows, the C# DSL,
JSON, dispatcher behavior, application options, or any public contract.

The final real-server run passed 65/65 tests with no failures or skips. The
result recommends a separately planned production provider, but this spike is
not itself supported or packable.

The accepted executable specification is
`goals/2026-08-01-networked-relational-durable-output-feasibility/README.md`.

## Scope And Dependency Boundary

The spike lives in
`spikes/FluxFlow.Engine.DurableOutput.RelationalSpike` as one `net10.0` test
project with `IsPackable=false`. It is deliberately absent from:

- `FluxFlow.sln`;
- `eng/packages.json`;
- public API baselines;
- release/package automation; and
- production registration and application configuration.

Its local `Directory.Packages.props` imports the repository versions and adds
only the official `Microsoft.Data.SqlClient` 7.0.2 dependency. Entity Framework
Core, Dapper, a generic repository, a SQL dialect abstraction, runtime provider
selection, reflection, assembly scanning, service location, and hidden retry
were not added.

Direct SQL was selected because durable output is a compact transactional state
machine. The critical provider work is explicit unique insert-or-compare,
range/update locking, deterministic locked-row skipping, exact lease-token and
expiry compare-and-set settlement, binary keyset paging, and generation CAS
replay. An ORM would not remove those provider-specific operations.

## Store And Schema

`RelationalDurableOutputStore` is internal, sealed, operation-scoped, and
idempotently disposable. It validates one immutable connection string, opens a
pooled connection per public operation, owns no background work, and rejects
operations after disposal.

Initialization uses a per-instance asynchronous gate plus an exclusive
transaction-owned database application lock. Version 1 owns exactly:

- `dbo.fluxflow_relational_output_schema`;
- `dbo.fluxflow_relational_outputs`; and
- `dbo.fluxflow_relational_output_deliveries`.

The immutable capture row stores the complete value/error envelope, lineage,
timestamps with offsets, headers, and envelope schema version. The delivery row
stores Pending, Leased, Completed, or DeadLettered state with exact schedule,
lease, attempt, completion, reason, dead-letter time, and generation metadata.

Application address and message ID use explicit
`Latin1_General_100_BIN2` collation. Schema validation checks exact ordered
columns, types, sizes, nullability, binary collations, primary keys, the
composite cascade foreign key, trusted check counts, and both index key shapes.
Entirely absent objects are created; partial, future, corrupt, incompatible, or
missing-index schemas are rejected without downgrade or repair.

## Transaction Protocol

- Capture uses an explicit serializable transaction and
  `UPDLOCK,HOLDLOCK` range protection. An absent key inserts once; an existing
  complete envelope returns equivalent `AlreadyExists` or no-overwrite
  `Conflict` through `HasSameContent(...)`.
- Leasing uses an explicit read-committed transaction. Missing delivery rows
  are backfilled under range protection. One eligible Pending or exactly
  expired Leased row is selected in capture-time/binary-key order with
  `UPDLOCK,READPAST,ROWLOCK`, then updated with a fresh token and returned with
  its immutable envelope. The tested database keeps read-committed snapshot
  disabled because that is an explicit `READPAST` assumption.
- Completion, retry, and dead-letter settlement atomically require exact key,
  Leased state, token, and unexpired lease. Non-applied results distinguish
  NotFound, InvalidState, and LeaseLost without mutation.
- Dead-letter listing selects bounded metadata only, applies independent
  filters, and uses mixed-direction keyset paging. Exact lookup joins the full
  envelope. Replay requires exact state and generation, preserves generation,
  resets attempt to zero, clears terminal metadata, and applies the explicit
  next schedule.
- Every mutating operation passes cancellation through open/commands, checks it
  immediately before commit, and commits with `CancellationToken.None` after
  ownership is fixed.

## Real Environment And Runner

The runner requires explicit `-AcceptLicense`, verifies Docker, generates a
strong ephemeral administrator password without printing it, binds a
Docker-assigned port to loopback only, and passes the connection string through
`FLUXFLOW_RELATIONAL_SPIKE_CONNECTION_STRING` only. It always removes its
container in `finally` during normal success/failure execution and creates no
volume.

The final successful environment used:

- `mcr.microsoft.com/mssql/server:2022-latest`;
- pulled digest
  `sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`;
- `Microsoft.Data.SqlClient` 7.0.2; and
- one fresh uniquely named database per conformance/provider context.

Cleanup disposes all stores, clears only the tested connection pool, forces
remaining sessions out of the generated database, and drops it idempotently.
The final Docker inventory confirmed zero retained spike containers.

## Test Workflow And Evidence

The mandated static pairing analyzer ran exactly once before test methods at
the narrow spike root and completed in 116 ms. Because implementation and tests
intentionally share a test project, the lexical heuristic classified all eight
colocated source files as tests: zero production sources, eight test files,
zero pairings, and zero suggestions. This limitation is recorded explicitly;
the analyzer is not coverage evidence.

The final 65 real-database cases comprise 38 inherited conformance cases and 27
provider-specific cases:

- inherited capture idempotency, address scope, conflict, cancellation, and
  null-guard cases;
- all 12 delivery methods, including deterministic ordering, exact due/expiry
  boundaries, one/many-worker leasing, settlement statuses, retry, and
  cancellation;
- all 13 dead-letter methods, including settlement, filters, bounded keyset
  pages, exact lookup, generation cycles, replay races, and cancellation;
- environment validation and fresh database lifecycle;
- exact schema, concurrent initialization, future/partial/missing-index/
  corrupt/incompatible rejection;
- raw state encoding, reopen fidelity, binary ordering, and proof that list
  projection does not parse excluded sensitive columns; and
- multi-store capture, many-row lease, completion/replay winner persistence,
  and external-lock timeout/recovery.

The first real run passed 59/65 and exposed six provider-test assumptions, not
shared contract failures: JSON-bearing record equality, textual GUID casing,
replay attempt expectations, and deliberate corruption blocked by the schema
check. After test-only corrections, the next run passed 64/65 and exposed one
remaining post-replay attempt expectation. The corrected final run passed all
65 in 1 minute 9 seconds (104.9 seconds total command wall time). These
intermediate failures were retained in `.testagent/status.md` rather than
hidden.

Final validation:

- focused Debug build: 7 projects, zero errors/warnings;
- focused Release build: 7 projects, zero errors/warnings;
- focused formatting verification: passed;
- real integration suite: 65/65 passed, zero skips;
- serialized non-incremental Debug solution build: 129 projects, zero
  errors/warnings;
- serialized non-incremental Release solution build: 129 projects, zero
  errors/warnings;
- serialized default Release suite: 1,968/1,968 passed across 62 projects;
- release/solution inventory references to the spike/client: zero;
- forbidden ORM/reflection/service-location patterns in spike C# sources: zero;
- retained disposable containers after final execution: zero; and
- `git diff --check`: passed.

Research, requirement mapping, progress, exact run history, gap analysis,
pseudo-mutation review, and assertion-quality audit are retained under
`.testagent/`.

## Limits And Decision

The result is a promote recommendation for a separate production-provider
round. It does not create production support now. The spike has no public
options or registration, prior relational schema/migration line, supported
server matrix, transient error policy, credential/deployment documentation,
health/telemetry integration, package/version/release entry, or operational
administration surface.

It also adds no input provider, transport, retention, purge, bulk replay,
batching, parallel dispatcher, distributed coordination, producer/business
transaction, workflow checkpoint, or exactly-once claim.

A production round should preserve this direct, small provider boundary and add
only immutable provider options, flat DI registration, explicit schema
migrations beginning with the first future version, supported server/version
and deployment guidance, bounded transient-fault semantics, production
operational tests, and package governance. No universal persistence framework
is justified by this evidence.

## Promotion Outcome

The recommendation was implemented on 2026-08-01 as the supported,
independently packaged `FluxFlow.Engine.DurableOutput.TSql` 1.0.0 provider. The
obsolete executable spike directory was retired after its production code and
tests were promoted without duplication. See
[[279-production-tsql-durable-output-provider]] and
`docs/32-tsql-durable-outputs.md` for the current contract and operating model.
