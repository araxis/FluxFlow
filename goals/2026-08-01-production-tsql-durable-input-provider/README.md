# GOAL: Add a production networked T-SQL durable-input provider

## Status

Completed on 2026-08-01, including the explicit real-server validation gate.
This file is the authoritative executable implementation prompt and permanent
engineering record for this round. The complete execution evidence is recorded
below.

## Executive intent

Add a supported, independently packaged, opt-in durable-input provider named
`FluxFlow.Engine.DurableInput.TSql`. The provider must make the established
durable-input contracts usable by multiple application processes against a
shared networked relational database while preserving FluxFlow's lightweight
in-process default.

The provider must implement the current store, dead-letter, and exact lease
renewal capabilities without altering their meaning. It must preserve the
existing workflow-completion acknowledgement mode and give that mode a
multi-host-safe persistence implementation. It must not introduce durable
internal workflow checkpoints, exactly-once claims, distributed transactions,
an ORM, reflection, convention scanning, generic repositories, hidden
background work, or a provider-agnostic SQL framework.

Hosts that do not reference and explicitly register this package must incur no
new dependency, startup, network, schema, or runtime cost.

## Product decision

- Package ID, project, assembly, root namespace, and public type prefix:
  `FluxFlow.Engine.DurableInput.TSql` and `TSqlDurableInput...`.
- Initial package version: `1.0.0`.
- Supported target frameworks: `net8.0;net10.0`, matching the durable-input
  packages and the existing networked durable-output provider.
- Direct database access uses the centrally managed `Microsoft.Data.SqlClient`
  dependency already proven by the durable-output T-SQL provider.
- Persistence uses direct, explicit, parameterized ADO.NET commands. No ORM or
  micro-ORM is permitted.
- The initial validated database target is the same T-SQL server version and
  explicit integration environment already used by the durable-output
  provider.
- The package remains an adapter behind the unchanged
  `IDurableInputStore`, `IDurableInputDeadLetterStore`, and
  `IDurableInputLeaseRenewalStore` contracts.
- The provider guarantee remains at-least-once. Consumers must remain
  idempotent by durable input key where duplicate processing matters.

## Mandatory engineering principles

1. Preserve KISS, SRP, IoC, explicit dependencies, and feature-local cohesion.
2. Keep the public configuration flat: one
   `Action<TSqlDurableInputStoreOptionsBuilder>` callback with no nested
   callback graph.
3. Use a short-lived mutable registration builder to produce one immutable,
   init-only options record. Runtime code receives resolved immutable settings.
4. Validate configuration and registration ownership before mutating
   `IServiceCollection`.
5. Keep registration, provider construction, concrete store resolution, and
   interface resolution side-effect-free. They must not open a connection or
   initialize schema.
6. Open pooled connections per operation. Do not own a process-wide connection
   and do not implement a client-side connection pool.
7. Use explicit transactions, isolation levels, row/key-range locks,
   compare-and-set predicates, parameters, cancellation, and bounded timeouts.
8. Do not add EF Core, Dapper, a generic repository, a universal store factory,
   reflection, assembly scanning, dynamic proxies, ambient transactions, or a
   provider-neutral relational abstraction.
9. Do not move provider configuration into `FluxFlowApplicationOptions`.
10. Do not change the durable-input dispatcher or add a provider-owned hosted
    service. The existing dispatcher owns scheduling and completion handling.
11. Do not add automatic retries around state-changing SQL commands,
    transactions, or commits. Only the SQL client's explicit bounded
    connection-open resiliency settings may be configured because retrying an
    ambiguous commit could violate state-transition semantics.
12. Do not expose connection strings, credentials, payloads, headers, SQL
    statements, or sensitive server details through `ToString()`, stable outer
    exception messages, logs, tests, examples, or generated artifacts.
13. Preserve every existing durable-input and SQL-file provider behavior and
    public API.
14. Share code with the output provider only when a small utility has exactly
    the same stable responsibility. Do not create a large common persistence
    framework merely to remove superficial SQL duplication.
15. Do not add custom build hooks. Use the repository's existing project,
    solution, central-package, release-manifest, API-baseline, documentation,
    and memory conventions.

## Required public API

### Registration

Provide the following flat usage:

```csharp
services.AddFluxFlowTSqlDurableInput(options =>
{
    options.ConnectionString =
        configuration.GetConnectionString("FluxFlowDurableInput");
    options.CommandTimeout = TimeSpan.FromSeconds(30);
    options.SchemaLockTimeout = TimeSpan.FromSeconds(30);
    options.ConnectRetryCount = 1;
    options.ConnectRetryInterval = TimeSpan.FromSeconds(1);
    options.SchemaManagement =
        TSqlDurableInputSchemaManagement.CreateOrMigrate;
});
```

Required signature:

```csharp
public static IServiceCollection AddFluxFlowTSqlDurableInput(
    this IServiceCollection services,
    Action<TSqlDurableInputStoreOptionsBuilder> configure)
```

The method name follows the established T-SQL durable-output naming and must
not add aliases or overload families in this round.

### Immutable options

Create a sealed immutable record `TSqlDurableInputStoreOptions` with init-only
properties and these stable defaults and rules:

- `string? ConnectionString`: required; whitespace is invalid; it must parse
  through `SqlConnectionStringBuilder` and specify both a server/data source
  and an initial catalog/database.
- `TimeSpan CommandTimeout`: default 30 seconds; positive; at most 10 minutes;
  exactly representable as whole seconds.
- `TimeSpan SchemaLockTimeout`: default 30 seconds; non-negative; at most 10
  minutes; exactly representable as whole milliseconds. Zero means no wait.
- `int ConnectRetryCount`: default 1; range 0 through 5.
- `TimeSpan ConnectRetryInterval`: default 1 second; range 1 through 60
  seconds; exactly representable as whole seconds.
- `TSqlDurableInputSchemaManagement SchemaManagement`: default
  `CreateOrMigrate`; undefined enum values are rejected.

Connection-string normalization must preserve valid host configuration while
overriding only `ConnectRetryCount` and `ConnectRetryInterval` with the
explicitly resolved FluxFlow values. `ToString()` must redact the connection
string.

Create an internal immutable resolved-settings record containing only the
normalized connection string, integer command timeout, integer schema-lock
timeout, and schema mode required at runtime.

### Temporary builder

Create `TSqlDurableInputStoreOptionsBuilder` with the same flat properties and
defaults. Its internal `Build()` method must construct and validate the
immutable options record, normalize the connection string, and return the
immutable result. It must not retain delegates, configuration objects, or a
nested builder hierarchy.

### Schema mode

Create:

```csharp
public enum TSqlDurableInputSchemaManagement
{
    CreateOrMigrate = 0,
    ValidateOnly = 1
}
```

- `CreateOrMigrate` may create a completely absent provider schema and apply
  known ordered migrations under a bounded application lock.
- `ValidateOnly` performs read-only version and shape validation and fails
  when the schema is missing, partial, unsupported, or incompatible.

### Store

Create one public sealed `TSqlDurableInputStore` implementing:

- `IDurableInputStore`;
- `IDurableInputDeadLetterStore`;
- `IDurableInputLeaseRenewalStore`;
- `IAsyncDisposable`.

All three interfaces must resolve to the exact same singleton instance.

## Dependency-injection behavior

Registration must be deterministic and atomic:

1. Reject null services and null configure callbacks.
2. Invoke the configure callback exactly once.
3. Build, normalize, and validate immutable options before collection
   mutation.
4. Add exactly one immutable options singleton, one concrete store singleton,
   and three singleton aliases resolving the concrete store.
5. Repeating registration with equivalent normalized settings is idempotent
   and adds no descriptors.
6. Repeating registration with different settings throws a clear
   `InvalidOperationException` without changing the collection.
7. A tampered, partial, or lifetime-incompatible prior provider registration
   fails atomically.
8. Existing ownership of any of the three durable-input interfaces causes a
   clear conflict before mutation. Do not replace or shadow another provider.
9. Unrelated services remain untouched.
10. Service-provider construction and store resolution perform no database
    operation.

## Schema contract

Create an explicit version-1 provider schema under `dbo`. Use stable,
provider-owned object names and ordinal/binary key comparison for durable
identity fields.

The logical schema must persist:

- one schema/version row;
- application address and message identifier as the composite durable key;
- contract name and envelope schema version;
- success/error discriminator;
- payload or serialized error;
- trace identifier, event timestamp ticks and original offset;
- enqueue timestamp ticks and original offset;
- optional correlation and causation identifiers;
- serialized headers;
- current state (`Pending`, `Leased`, `Delivered`, or `DeadLettered`);
- next-attempt timestamp;
- current attempt number;
- lease owner, exact lease token, leased-at timestamp, and lease expiry;
- failure kind and sanitized failure message;
- dead-letter timestamp and monotonically increasing dead-letter generation.

Required schema behavior:

1. Use a schema metadata table and one durable-input table unless a demonstrable
   correctness requirement demands a second data table. Prefer the smallest
   schema that preserves the complete contract.
2. Use exact column lengths based on current contract/provider limits and
   `nvarchar(max)` for payload/error/header material where no smaller public
   contract limit exists.
3. Preserve UTC ticks plus original offsets exactly for envelope and
   operational timestamps.
4. Add named check constraints that enforce legal state-dependent combinations
   of attempt, lease, failure, and dead-letter fields.
5. Add named indexes supporting deterministic due-message leasing and current
   dead-letter filtering/keyset ordering.
6. Serialize initialization with `sp_getapplock` scoped to the initialization
   transaction and bounded by `SchemaLockTimeout`.
7. Use a small explicit ordered migration representation. Version zero means
   every provider-owned object is absent; migration 1 creates version 1. Do not
   invent a version-2 migration.
8. Validate exact required tables, columns, data types, lengths, nullability,
   primary keys, constraints, and indexes.
9. Create a missing schema only in `CreateOrMigrate` mode.
10. `ValidateOnly` performs no create, alter, drop, repair, or version write.
11. Fail closed for partial, unversioned, malformed, unsupported older, or
    future schemas. Never delete, infer, or silently repair unknown state.
12. Keep each schema migration transactional.
13. Use the same explicit locking-read-committed prerequisite as the proven
    durable-output provider. Reject `READ_COMMITTED_SNAPSHOT ON` during schema
    initialization because the `READPAST` lease protocol is defined for
    locking read committed.
14. Document the permissions required for runtime validation/operations and
    the additional DDL permissions required by `CreateOrMigrate`.

## Store semantics

Implement the existing contracts exactly; do not reinterpret them.

### Enqueue

- Normalize and validate the durable key through existing domain types.
- Enqueue is idempotent by ordinal application address plus message ID.
- Equivalent content returns `AlreadyExists` without overwriting persisted
  data or operational state.
- Different content under the same key returns `Conflict`.
- A new envelope returns the existing successful enqueue result.
- Protect concurrent key insertion/content comparison with a serializable
  transaction and `UPDLOCK, HOLDLOCK` key-range protection.

### Lease

- Lease only due `Pending` records and expired `Leased` records.
- Use locking read committed with `UPDLOCK, READPAST, ROWLOCK` and one atomic
  statement/transaction that selects and updates the batch.
- Order deterministically by effective due time, enqueue time, application
  address, and message ID according to the shared conformance contract.
- Respect the requested maximum count.
- Generate a new unpredictable exact lease token for each acquired/reacquired
  lease.
- Increment the attempt exactly once when a lease is acquired, including
  expired-lease recovery.
- Never return the same active lease to two concurrent owners.

### Complete, release, and dead-letter

- Implement each transition as an atomic compare-and-set on key, `Leased`
  state, exact token, and unexpired lease at the operation timestamp.
- `Complete` moves the record to the terminal delivered tombstone.
- `Release` returns it to pending with the exact requested next-attempt time
  and failure metadata while clearing all lease fields.
- `DeadLetter` moves it to the terminal dead-letter state, records the exact
  failure/time metadata, clears lease fields, and increments dead-letter
  generation exactly once.
- Return the existing `Succeeded`, `NotFound`, or `LeaseRejected` results
  without ambiguous provider-specific statuses.
- Delivered and dead-lettered rows remain idempotency tombstones.

### Exact lease renewal

- Renewal atomically matches key, `Leased` state, exact token, and a lease that
  is unexpired at `RenewedAt`.
- Set the exact requested `LeaseUntil`, including a valid value earlier than
  the previous expiry when the contract allows it.
- Preserve envelope, attempt, owner, token, leased-at time, retry metadata, and
  generation.
- Never revive an expired, pending, delivered, or dead-lettered record.
- A concurrent terminal/release transition must have one winner; renewal must
  never overwrite the winning state.

### Dead-letter operations

- List only current dead letters.
- Preserve all existing optional filters, inclusive-from/exclusive-before time
  semantics, maximum page size, newest-first ordering, ordinal key tie-breaks,
  exclusive keyset cursor behavior, and payload-free summaries.
- Get returns the complete original envelope plus exact current dead-letter
  metadata only for a current dead letter.
- Replay atomically matches key, `DeadLettered` state, and expected generation.
- Successful replay preserves the envelope, returns the row to pending at the
  exact requested availability time, clears failure/lease/dead-letter fields,
  resets attempt according to the existing contract, and advances generation
  exactly once.
- Missing, non-dead-letter, and generation-mismatch cases return their existing
  results without mutation.
- Concurrent replays for one generation have exactly one winner.

## Data mapping and validation

1. Preserve exact envelope content comparison, including JSON payload/error,
   headers, timestamp offsets, IDs, schema version, and success/error shape.
2. Serialize JSON using explicit repository conventions; do not add polymorphic
   reflection or type-name metadata.
3. Validate provider column-length limits before opening a connection. At
   minimum cover application address, message ID, contract name, trace ID,
   correlation/causation IDs, lease owner, lease token, and failure message.
4. Do not add undocumented size limits for material stored as
   `nvarchar(max)`.
5. Treat corrupted persisted rows, invalid enum values, malformed JSON, and
   impossible state combinations as explicit provider-owned failures with
   stable actionable messages.
6. Use parameters for every runtime value. Never interpolate caller-controlled
   values into executable SQL.
7. Flow cancellation into connection open, commands, readers, transactions,
   and schema operations where the contract permits it.
8. Once a caller-visible operation reaches its commit point, use a
   non-cancelable commit token to avoid converting an already committed result
   into a misleading cancellation result.

## Failure and lifecycle behavior

- Configuration failures throw `ArgumentException`,
  `ArgumentOutOfRangeException`, or `InvalidOperationException` as appropriate
  before DI mutation.
- Schema incompatibility and corrupted-row errors use stable provider-owned
  outer messages. Preserve an inner provider exception only where useful and
  safe.
- Connection/open/command failures may surface the original `SqlException` but
  must not add an outer message containing credentials, payloads, or SQL text.
- Do not retry state-changing commands or ambiguous commits.
- `DisposeAsync` is idempotent. The store owns no remote server resource and
  must not clear shared/global pools.
- Operations after disposal fail predictably before database access.

## Project and package integration

1. Reuse the existing centrally managed `Microsoft.Data.SqlClient` version; do
   not introduce a second version.
2. Create
   `src/FluxFlow.Engine.DurableInput.TSql/FluxFlow.Engine.DurableInput.TSql.csproj`
   with ordinary packaging metadata, README packing, deterministic CI
   settings, and a project reference only to
   `FluxFlow.Engine.DurableInput` plus the minimal DI abstraction package.
3. Add the production project and fast test project to `FluxFlow.sln` with all
   normal Debug/Release configurations.
4. Keep the explicit real-server integration project outside the default
   solution if that matches the existing T-SQL output testing convention.
5. Add the package to `eng/packages.json` immediately after the SQL-file
   durable-input package.
6. Update the public API baseline only through the repository's acceptance
   mechanism and manually review every added declaration.
7. Pack both target frameworks and verify the README, assemblies, symbols, and
   exact dependencies.
8. Do not ship integration infrastructure, credentials, generated test state,
   or output-provider implementation source in the package.

## Test architecture

Use two separate suites so the default repository remains network-free.

### Fast package tests

Create `tests/FluxFlow.Engine.DurableInput.TSql.Tests` in the main solution. It
must require no live server and cover:

- every options default and boundary;
- connection-string normalization and malformed/missing server/database cases
  without opening a connection;
- exact whole-second and whole-millisecond timeout rules;
- builder-to-immutable-record behavior and redacted `ToString()`;
- null services/configure arguments and callback invocation count;
- validation before service collection mutation;
- equivalent normalized repeat idempotency;
- conflicting repeat and tampered registration rejection;
- pre-existing ownership conflict for each of the three interfaces;
- exact same-instance singleton aliases;
- absence of database access during registration/provider/store resolution;
- safe repeated disposal and post-disposal failure without a live server;
- provider-specific length/preflight validation reachable before database I/O;
- package/dependency and core-boundary rules where existing release tests do not
  already provide equivalent evidence.

### Provider-neutral conformance

Create thin inherited adapters for all three existing suites:

- `DurableInputStoreConformanceTests`;
- `DurableInputDeadLetterStoreConformanceTests`;
- `DurableInputLeaseRenewalStoreConformanceTests`.

Do not duplicate those tests or create a second fake store. The real-server
context must construct the production provider against an isolated database
and expose the three interfaces from that one store.

### Explicit real-server integration tests

Create `tests/FluxFlow.Engine.DurableInput.TSql.IntegrationTests` as an
explicit, non-default test project. Reuse the already-proven environment and
disposable-database conventions from durable output without coupling the two
production packages.

The suite must cover:

- all inherited store, dead-letter, and renewal conformance cases;
- schema creation and exact version-1 validation;
- repeated and concurrent initialization;
- validate-only success and absent-schema failure;
- partial, unversioned, malformed, future-version, and RCSI-enabled rejection
  without mutation;
- restart persistence and terminal idempotency tombstones;
- ordinal key and content-comparison semantics;
- concurrent identical and conflicting enqueue;
- multi-owner concurrent batch leasing without duplicate active leases;
- lease expiry/recovery and attempt/token behavior;
- completion, release, dead-letter, and renewal races;
- current-token exact renewal and expired-token rejection;
- dead-letter filtering, paging, get, replay, and replay-generation races;
- cancellation and bounded schema-lock behavior where deterministic;
- representative preflight length failures before command execution;
- non-default command, schema-lock, and connection-open resiliency settings;
- representative redaction and corruption failures;
- disposal/restart behavior and multiple independent store instances.

The explicit runner must follow the existing production T-SQL test convention:

1. require an affirmative license-acceptance switch;
2. use the established official server image by default;
3. create a unique container, host port, database names, and strong ephemeral
   password;
4. wait with a bounded readiness timeout;
5. pass the connection string through a process-scoped environment variable;
6. support an externally managed connection string;
7. run the integration project in Release with zero skipped tests;
8. always remove the owned container in `finally` unless a diagnostic-retention
   switch is explicitly supplied;
9. never print the password or full connection string;
10. capture and report the exact validated image tag and digest.

The main solution must build and test without Docker or network access.

## Documentation and memory

Create a focused production documentation page and update every affected
navigation or durable-input surface:

- root `README.md` package/provider table;
- package `README.md` with the minimal flat registration example;
- `docs/README.md` navigation;
- durable-input overview;
- SQL-file durable-input page with a provider-choice link;
- workflow-completion acknowledgement page;
- public API overview and package/coverage matrix where applicable;
- changelog with the new `1.0.0` package entry;
- memory index, current state, architecture decisions, progress log, and a new
  numbered memory record for this round;
- this goal file with exact execution evidence after completion.

Documentation must explain:

- when to choose local SQL-file persistence versus shared T-SQL persistence;
- opt-in package isolation and zero default network cost;
- flat registration and immutable resolved options;
- `CreateOrMigrate` versus `ValidateOnly` deployment responsibilities;
- least-privilege separation for migration and runtime identities;
- host-owned credentials, rotation, backups, capacity, retention, monitoring,
  and schema ownership;
- connection pooling, timeout, and connection-open resiliency ownership;
- at-least-once behavior and consumer idempotency;
- why commands/transactions are not automatically retried;
- how workflow-completion acknowledgement uses exact renewal with this provider;
- how to run the explicit real-server suite safely.

Examples must use placeholder configuration access and never literal secrets.

## Release and governance checks

- Package-manifest and documentation-boundary tests pass.
- The accepted public API baseline contains only intentional new declarations.
- Existing packages do not accidentally acquire the SQL client dependency.
- Binary compatibility checks pass for existing packages; the new initial
  package follows initial-release policy when no prior artifact exists.
- Release preflight, pack, archive inspection, fresh-cache consumer smoke, and
  feed verification pass for the new package.
- Merely installing unrelated/default packages never opens a network
  connection or initializes this schema.

## Mandatory test-quality workflow

Before editing test source:

1. Send the complete testing scope to the existing
   `code_testing_generator` agent.
2. Record bounded test research and a requirement-mapped plan in
   `.testagent/research.md` and `.testagent/plan.md`.
3. Use the existing shared conformance suites rather than regenerating their
   behavior cases.
4. Build and run the narrow provider suites during implementation.
5. Run test-gap and assertion-quality review after the tests pass, record the
   result in `.testagent/status.md`, and repair meaningful gaps.
6. Re-open final tests and map every behavioral requirement to exact test names
   or an explicit environmental blocker.

The final report must include a compact `Requirement | Evidence` table.

## Required validation sequence

1. Build the production project for `net8.0` and `net10.0` in Debug and Release.
2. Run the fast provider tests for every target framework.
3. Run the mandatory test-quality workflow and address real findings.
4. Run the explicit real-server integration runner with zero failures and zero
   skipped cases.
5. Build the complete solution in Debug and Release with zero warnings.
6. Run the complete default Release test suite with zero failures.
7. Run focused release, package-manifest, documentation-boundary, and public API
   governance tests.
8. Review and accept the intended public API additions, then rerun the
   baseline test.
9. Pack the new provider and inspect both ordinary and symbol packages.
10. Run release, archive, fresh-cache consumer-smoke, feed-verification, and
    binary-compatibility preflight appropriate to a new initial package.
11. Search source and package outputs for forbidden ORM/reflection/generic
    repository code, leaked secrets, duplicated conformance logic, and
    accidental dependency propagation.
12. Record exact project/test counts, target frameworks, integration image
    tag/digest, package evidence, compatibility status, and remaining caveats
    in documentation, memory, and this goal.

## Explicit non-goals

- No change to the in-memory workflow runtime.
- No change to `FluxFlowApplicationOptions`.
- No change to the durable-input dispatcher, completion source, or completion
  protocol.
- No durable internal workflow/node state or checkpoints.
- No exactly-once processing or distributed transaction claim.
- No outbox/inbox coupling across business databases.
- No broker, transport, trigger, or component-specific integration.
- No provider-agnostic relational framework or universal storage factory.
- No EF Core, Dapper, migration framework, or generic repository.
- No reflection, assembly scanning, service locator, or hidden retry system.
- No server discovery, database creation outside the configured catalog,
  secret-management package, dashboard, telemetry exporter, health-check
  package, automatic pruning, archival, or retention service.
- No additional database dialect in this round.
- No redesign of the SQL-file durable-input provider.

## Completion criteria

The round is complete only when:

- this full goal existed before production source changes and matches the
  delivered design;
- the provider is independently installable and explicitly registered;
- all three existing durable-input interfaces are implemented by one singleton;
- registration is flat, immutable after resolution, atomic, normalized,
  equivalent-idempotent, and free of database side effects;
- schema management is explicit, bounded, locked, versioned, transactional,
  validated, and fail-closed;
- enqueue, lease, transition, renewal, dead-letter, and replay semantics pass
  shared and provider-specific real-server tests under concurrency;
- the existing SQL-file provider and lightweight engine remain unchanged;
- the default build/test path remains container- and network-free;
- documentation, navigation, changelog, public API, package manifest, goal, and
  memory records are current;
- Debug and Release builds complete with zero warnings;
- focused, default, and real-server suites have zero failures, with zero skipped
  real-server tests;
- package/archive/smoke/feed governance succeeds;
- no requested behavior was removed and no prohibited magic, ORM, persistence
  framework, or expanded dependency graph was introduced.

## Execution evidence

### Delivered implementation

- Added the independent `FluxFlow.Engine.DurableInput.TSql` 1.0.0 package for
  `net8.0;net10.0`, its fast multi-target test project, and an explicit
  `net10.0` real-server integration project outside the default solution.
- Added the one-level `AddFluxFlowTSqlDurableInput(...)` registration callback,
  immutable redacted resolved options, explicit schema management, and one
  singleton implementing the store, dead-letter, and exact-renewal contracts.
- Implemented the version-1 two-table schema, transaction-owned application
  lock, exact fail-closed validation, serializable idempotent enqueue, atomic
  ordered batch leasing, token/expiry compare-and-set transitions and renewal,
  bounded dead-letter operations, and generation-protected replay using direct
  parameterized `Microsoft.Data.SqlClient` commands.
- Registered the package in the 59-entry release manifest and main solution,
  accepted its 34 intentional public declarations through the documented
  baseline mechanism, and updated root/package documentation, documentation
  navigation, changelog, provider-choice guidance, and memory.

### Test-quality evidence

- The mandatory source-pairing analysis ran once before test-source editing.
- Fast tests pass 63/63 on `net8.0` and 63/63 on `net10.0` with zero warnings.
- The explicit integration project builds across eight projects with zero
  warnings and runs 64 executions: 27 inherited provider-neutral conformance
  cases and 37 provider/environment executions. The complete real-server run
  passes 64/64 with zero failures and zero skips.
- The assertion audit reviewed 70 locally declared fact/theory methods and 317
  direct/helper assertion patterns. It found no effective assertion-free,
  trivial-only, or self-referential test. Equality, exception, type, singleton
  identity, immutable snapshot, collection/order, deep envelope, persisted SQL
  state, negative side-effect, redaction, and concurrency-outcome assertions
  are represented.
- The pseudo-mutation/gap audit strengthened schema constraint-count/version/
  index checks, binary ordering, reordered-JSON idempotency, replay generation
  and schedule persistence, corrupt-state diagnostics, and settlement/renewal/
  replay races. No assertion was weakened and no skip, mock database, random
  timing, or production test seam was introduced.

### Repository and package evidence

- Debug and Release solution builds pass across 133 projects with zero errors
  and zero warnings.
- The serialized default Release matrix passes 2,267/2,267 tests across 66 test
  projects with zero warnings. Two parallel attempts each exposed a different
  pre-existing asynchronous timeout after 2,266 passes; both exact tests passed
  immediately in isolation, and serialization removed cross-project pressure.
- Release governance passes 111/111; the focused package-manifest group passes
  4/4; the accepted public API baseline recheck passes.
- `FluxFlow.Engine.DurableInput.TSql.1.0.0.nupkg` and its symbols package were
  created. Archive inspection confirms only the README, icon, manifest, and
  `net8.0`/`net10.0` assemblies in the ordinary package.
- Both frameworks declare exactly DurableInput 1.1.0, SqlClient 7.0.2, and DI
  abstractions 10.0.7. Release preflight, archive inspection, clean-cache
  consumer smoke for both target frameworks, feed-verification preparation,
  and initial-package binary-compatibility preparation pass.
- Formatting, diff whitespace checks, and static searches confirm no EF Core,
  Dapper, reflection, dynamic proxy, generic repository, hosted worker, hidden
  retry loop, or accidental provider dependency in Engine/default packages.

### Real-server validation

- Docker Desktop became available through the `desktop-linux` context with
  Docker Engine 29.6.2.
- The first complete run executed all 64 tests and reported 59 passes and five
  failures. Review against the shared durable-input conformance suite, the
  existing SQL-file provider, and the proven T-SQL durable-output provider
  showed that production behavior was correct and the new provider-specific
  tests had five incorrect expectations.
- Test expectations were corrected without changing production code or
  weakening coverage: partial schema is asserted as the established
  fail-closed `InvalidDataException` with its exact safe message, and a losing
  transition after another transition has already changed the row out of
  `Leased` is asserted as `InvalidState`, not `LeaseLost`.
- The complete runner was then repeated successfully: 64 passed, 0 failed,
  0 skipped, with a reported test duration of 4 minutes 47 seconds.
- The tested image tag was
  `mcr.microsoft.com/mssql/server:2022-latest`; the immutable tested digest was
  `mcr.microsoft.com/mssql/server@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`.
- The runner cleaned up its owned disposable container. The schema,
  persistence, concurrency, locking, replay, disposal, configuration, and
  provider-neutral contract behavior are therefore runtime-proven for this
  round, and no validation gate remains open.
