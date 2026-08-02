# GOAL: Promote the proven networked SQL durable-output spike into a production T-SQL provider

## Status

Completed on 2026-08-01. This file remains the authoritative implementation
prompt and permanent engineering record for the round.

## Executive intent

Promote the successful direct-SQL networked relational feasibility spike into a supported, independently packaged, opt-in durable-output provider named `FluxFlow.Engine.DurableOutput.TSql`.

The provider must preserve FluxFlow's lightweight in-process default and every existing durable-output behavior. It must add no ORM, reflection, convention scanning, generic repository, hidden service graph, background worker, implicit remote connection, or automatic business-operation retry. Hosts that do not install and explicitly register this package must pay no dependency, startup, network, or runtime cost.

The resulting provider must be simple to register, explicit to operate, safe under concurrency, packageable, documented, and validated against a real SQL Server 2022 instance. It must reuse the already-proven SQL behavior rather than redesigning the durable-output contracts.

## Product decision

- Package, project, assembly, root namespace, and public type names use the neutral T-SQL dialect name rather than a vendor-branded project name.
- Package ID: `FluxFlow.Engine.DurableOutput.TSql`.
- Initial package version: `1.0.0`.
- Supported target frameworks: `net8.0;net10.0`, matching the durable-output production packages.
- The direct database dependency is the official `Microsoft.Data.SqlClient` package, centrally versioned at the current stable `7.0.2` release.
- The implementation uses direct parameterized ADO.NET commands only.
- Initial validated database target: SQL Server 2022, including the official Linux container image used by the explicit integration runner.
- The provider remains an adapter behind the existing `IDurableOutputStore`, `IDurableOutputDeliveryStore`, and `IDurableOutputDeadLetterStore` contracts. No core contract changes are permitted unless a test proves an unavoidable defect and the change is separately justified.

## Mandatory engineering principles

1. Preserve KISS, SRP, IoC, explicit dependencies, and feature-local cohesion.
2. Keep configuration flat. The public registration surface accepts one `Action<TSqlDurableOutputStoreOptionsBuilder>` callback and requires no nested callbacks.
3. Keep resolved runtime configuration immutable. A short-lived mutable builder is allowed only at registration time; it builds an immutable record.
4. Make all registration validation occur before mutating `IServiceCollection`.
5. Keep startup side-effect-free: registration and service-provider construction perform no network, schema, or credential operation.
6. Open pooled connections per operation. Do not own a process-wide connection or client-side pool.
7. Use explicit SQL transactions, isolation levels, row locks, compare-and-set predicates, parameters, and command timeouts.
8. Do not add EF Core, Dapper, a generic repository, reflection, assembly scanning, dynamic proxying, generated runtime SQL, ambient transactions, or provider-agnostic SQL abstraction.
9. Do not move connection or provider settings into `FluxFlowApplicationOptions`.
10. Do not add an application background delivery worker in this package. Existing orchestration owns delivery scheduling.
11. Do not add automatic retries around state-changing commands or transactions. A connection-open failure can be retried only through the SQL client's explicit bounded connection resiliency settings because replaying an ambiguous commit can violate delivery semantics.
12. Do not leak connection strings, credentials, payloads, headers, full SQL, or sensitive server details into stable outer exception messages, logs, documentation examples, or tests.
13. Preserve all existing local SQLite provider behavior and public APIs.
14. Do not add custom MSBuild hooks. Use the repository's ordinary project, central package, manifest, and release conventions.

## Public API

Create the following minimal public surface in `FluxFlow.Engine.DurableOutput.TSql`:

### Registration

```csharp
services.AddFluxFlowTSqlDurableOutput(options =>
{
    options.ConnectionString = configuration.GetConnectionString("FluxFlowDurableOutput");
    options.CommandTimeout = TimeSpan.FromSeconds(30);
    options.SchemaLockTimeout = TimeSpan.FromSeconds(30);
    options.ConnectRetryCount = 1;
    options.ConnectRetryInterval = TimeSpan.FromSeconds(1);
    options.SchemaManagement = TSqlDurableOutputSchemaManagement.CreateOrMigrate;
});
```

Required signature:

```csharp
public static IServiceCollection AddFluxFlowTSqlDurableOutput(
    this IServiceCollection services,
    Action<TSqlDurableOutputStoreOptionsBuilder> configure)
```

### Immutable options

Create a sealed immutable record `TSqlDurableOutputStoreOptions` with init-only properties and stable defaults:

- `string? ConnectionString`: required; whitespace is invalid; must parse through `SqlConnectionStringBuilder`; must specify both a server/data source and a database/initial catalog.
- `TimeSpan CommandTimeout`: default 30 seconds; must be positive, no greater than 10 minutes, and exactly representable as whole seconds because `SqlCommand.CommandTimeout` is integer seconds.
- `TimeSpan SchemaLockTimeout`: default 30 seconds; must be non-negative, no greater than 10 minutes, and exactly representable as whole milliseconds because `sp_getapplock` accepts integer milliseconds. Zero means do not wait.
- `int ConnectRetryCount`: default 1; range 0 through 5.
- `TimeSpan ConnectRetryInterval`: default 1 second; range 1 through 60 seconds; exactly representable as whole seconds.
- `TSqlDurableOutputSchemaManagement SchemaManagement`: default `CreateOrMigrate`; only defined enum values are accepted.

Connection-string normalization must preserve valid host configuration while overriding only `ConnectRetryCount` and `ConnectRetryInterval` with the explicitly resolved FluxFlow options. Do not expose the normalized connection string through `ToString()` or exception messages.

### Temporary builder

Create `TSqlDurableOutputStoreOptionsBuilder` with the same flat properties and defaults. Its internal `Build()` method constructs `TSqlDurableOutputStoreOptions`, validates it, and returns the immutable result. No nested option builders and no `IOptions<T>` graph are required.

### Schema mode

Create:

```csharp
public enum TSqlDurableOutputSchemaManagement
{
    CreateOrMigrate = 0,
    ValidateOnly = 1
}
```

- `CreateOrMigrate` creates a completely absent schema and applies known ordered migrations under an application lock.
- `ValidateOnly` performs read-only schema/version/shape validation and fails when the schema is absent or incompatible.

### Store

Create one public sealed `TSqlDurableOutputStore` implementing:

- `IDurableOutputStore`
- `IDurableOutputDeliveryStore`
- `IDurableOutputDeadLetterStore`
- `IAsyncDisposable`

The three service interfaces must resolve to the exact same singleton instance.

## Dependency-injection behavior

Registration must follow the established SQL-file provider behavior:

1. Reject null services and null configure callbacks.
2. Invoke the callback once.
3. Build, normalize, and validate immutable options before any collection mutation.
4. Add exactly one immutable options singleton, one concrete store singleton, and three interface aliases resolving the concrete store.
5. Repeating the call with equivalent normalized settings is idempotent and must not add duplicate descriptors.
6. Repeating it with different settings must fail with a clear `InvalidOperationException` and leave the collection unchanged.
7. A tampered or incomplete prior T-SQL registration must fail atomically.
8. Existing registrations for any of the three durable-output interfaces must cause a clear conflict before mutation; silently replacing or shadowing another provider is forbidden.
9. Unrelated services remain untouched.
10. Registration, provider construction, and store resolution must perform no database I/O.

## Schema and migration contract

Promote the spike's proven three-table version-1 schema and concurrency semantics. Rename relational-spike identifiers to stable T-SQL provider identifiers while retaining deterministic binary key comparison and all required indexes.

Required logical tables under `dbo`:

- schema/version table;
- captured output envelope table;
- delivery state table.

Required behavior:

1. Serialize schema initialization with `sp_getapplock` scoped to the initialization transaction and bounded by `SchemaLockTimeout`.
2. Use one explicit ordered migration pipeline. Version zero means all provider-owned objects are absent; migration 1 creates version 1.
3. Version 1 must be validated for exact required tables, columns, types, lengths, nullability, primary keys, foreign key, check constraints, and named indexes.
4. A completely absent schema is created only in `CreateOrMigrate` mode.
5. `ValidateOnly` never creates, alters, drops, or repairs objects.
6. A partial schema, an unversioned schema, an unsupported older version, a future version, or a shape mismatch must fail closed with a stable actionable error. Do not guess, delete, or auto-repair.
7. Keep migration execution transactional. Do not split a schema version across transactions.
8. Keep a small explicit migration representation so future version 2 can be added without reflection or a framework, but do not invent a version-2 migration in this round.
9. Reject `READ_COMMITTED_SNAPSHOT ON` during initialization because the proven `READPAST` leasing strategy is defined for locking read-committed semantics in this provider.
10. The runtime identity must be able to read schema metadata and acquire the application lock. `CreateOrMigrate` additionally requires the DDL permissions documented for first deployment.

## Data and concurrency semantics

Preserve the feasibility spike's verified semantics:

- Captures are idempotent by ordinal `ApplicationAddress` plus `MessageId`.
- Equivalent repeats return `AlreadyExists`; different content for the same key returns `Conflict`.
- Capture uses a serializable transaction with `UPDLOCK, HOLDLOCK` to protect the key range.
- Delivery rows are backfilled transactionally from captured outputs.
- Leasing uses read committed plus `UPDLOCK, READPAST, ROWLOCK`, deterministic ordering, expiry recovery, and a newly generated lease token.
- Complete, retry, and dead-letter transitions use atomic compare-and-set predicates on state, token, and lease expiry.
- Replay uses state plus dead-letter generation compare-and-set protection.
- Dead-letter listing preserves filters, descending timestamp order, ordinal key tie-breakers, exclusive cursor behavior, and bounded page size.
- Stored timestamps preserve UTC ticks and original offsets.
- All SQL values are parameters. Values must never be interpolated into executable SQL.
- Cancellation must flow to open, command, reader, transaction, and schema operations where the contract allows it.
- Commit uses a non-cancelable token only after the caller-visible operation has reached the commit point, preserving the existing contract against cancellation ambiguity.
- The store owns no server resource and does not clear global pools when disposed.

## Provider-specific validation

Reject invalid values before database execution with parameter-specific exceptions and stable messages. At minimum validate:

- application address is present and no longer than 300 characters;
- message identifier is present and no longer than 128 characters;
- contract name is present and no longer than 1,024 characters;
- lease owner is present and no longer than 512 characters;
- trace, correlation, and causation identifiers fit their 512-character columns when present;
- all core contract invariants already enforced by durable-output records remain intact;
- enum and page-size values use the existing contract validation;
- option time spans satisfy the representable bounds described above.

Do not impose undocumented payload/header size limits when the database columns are `nvarchar(max)`.

## Failure behavior

- Option/configuration failures throw `ArgumentException`, `ArgumentOutOfRangeException`, or `InvalidOperationException` as appropriate before DI mutation.
- Schema incompatibility and corrupted persisted rows use stable provider-owned messages, with inner provider exceptions retained where diagnostically valuable.
- Connection/open/command failures may retain the original `SqlException` but must not wrap it with a message containing secrets or SQL text.
- Do not catch and retry ambiguous commands or commits.
- Disposal is idempotent and operations after disposal fail predictably.

## Project and packaging integration

1. Add `Microsoft.Data.SqlClient` version `7.0.2` to central package management.
2. Create `src/FluxFlow.Engine.DurableOutput.TSql/FluxFlow.Engine.DurableOutput.TSql.csproj` using ordinary repository packaging metadata, README packing, deterministic CI settings, and a project reference to `FluxFlow.Engine.DurableOutput`.
3. Add the production project and its fast test project to `FluxFlow.sln` with all standard Debug/Release configurations.
4. Add `FluxFlow.Engine.DurableOutput.TSql` to `eng/packages.json` after the durable-output SQL-file package.
5. Update the public API baseline using the repository's acceptance mechanism only after reviewing the intended API.
6. Ensure package creation for both target frameworks succeeds and package contents include the README and expected assemblies/dependencies.
7. Do not ship test infrastructure, container scripts, credentials, or spike source in the NuGet package.

## Test architecture

Use two deliberately separate suites.

### Fast package tests

Create `tests/FluxFlow.Engine.DurableOutput.TSql.Tests` in the main solution. It must not need a container or network and must cover:

- every option default and boundary;
- normalization and invalid connection strings without opening them;
- exact whole-second/millisecond rules;
- builder-to-immutable-record behavior;
- null callbacks and callback invocation count;
- atomic registration failure;
- equivalent repeat idempotency;
- conflicting repeat rejection;
- tampered registration rejection;
- pre-existing interface conflict rejection;
- exact same-instance aliasing of all three interfaces;
- no database access during registration, service-provider build, or store resolution;
- safe disposal and post-disposal behavior that does not require a live server;
- provider-specific preflight length validation where it can be reached without I/O.

### Explicit real-server integration tests

Create `tests/FluxFlow.Engine.DurableOutput.TSql.IntegrationTests` as an explicit, non-default test project. It references the production provider and the shared durable-output conformance tests; it must contain no duplicate store implementation.

Promote and adapt the spike evidence to cover:

- all inherited capture, delivery, and dead-letter conformance cases;
- schema creation and exact version-1 validation;
- repeated and concurrent initialization;
- validate-only absent-schema failure and validate-only success;
- partial, unversioned, malformed, RCSI-enabled, and future-version rejection without mutation;
- restart persistence;
- ordinal key semantics;
- concurrent identical capture and conflicting capture;
- concurrent leasing with no duplicate active lease;
- lease expiry/recovery and settlement races;
- dead-letter filtering, paging, and replay-generation races;
- command cancellation and bounded lock timeout where deterministic;
- representative provider-specific length failures before SQL execution;
- successful use with non-default command, lock, and connection retry settings;
- redaction checks for representative configuration/schema errors.

The explicit PowerShell runner must:

1. require an affirmative license-acceptance switch;
2. use the official `mcr.microsoft.com/mssql/server:2022-latest` image by default;
3. create a unique container, port, database names, and strong ephemeral password;
4. wait with a bounded readiness timeout;
5. pass the connection string through an environment variable only for the test process;
6. run the integration project with zero skips;
7. always remove the created container in `finally` unless a documented diagnostic-retention switch is explicitly supplied;
8. print no password or full connection string;
9. support an externally managed connection string so CI can test without container ownership;
10. document the exact tested image tag and digest captured during validation.

The main solution must remain buildable and testable without Docker. The explicit integration project may remain outside `FluxFlow.sln`; its production project reference must still be validated by the dedicated runner.

## Spike retirement

After the production provider and integration suite pass:

- move reusable tests and runner behavior into the production integration-test project;
- remove duplicated implementation source from `spikes/FluxFlow.Engine.DurableOutput.RelationalSpike`;
- remove the old spike directory when no unique evidence remains there;
- retain the earlier goal, documentation, and memory records as historical evidence, updated with a clear promotion link rather than rewritten as if the spike never existed.

## Documentation

Create a focused production documentation page and update all affected navigation/surfaces:

- root `README.md` package table;
- package `README.md` with minimal registration example;
- `docs/README.md` navigation;
- public API overview;
- existing durable-output capture, delivery, dead-letter, SQL-file, and feasibility pages where provider choices are discussed;
- changelog with the new package's `1.0.0` entry;
- memory index, current state, progress log, and a new numbered memory record;
- previous spike memory/page with a promotion outcome link.

Documentation must explain:

- when to choose the local SQL-file provider versus the T-SQL provider;
- that the provider is opt-in and is not placed in `FluxFlowApplicationOptions`;
- immutable resolved options and the flat builder callback;
- supported/tested server scope and RCSI requirement;
- `CreateOrMigrate` versus `ValidateOnly` deployment models;
- least-privilege separation between migration and runtime identities;
- connection pooling and timeout ownership;
- host-owned credential sourcing and rotation;
- delivery remains at-least-once and handlers/sinks must be idempotent;
- no automatic command/transaction retry and why ambiguous commits are surfaced;
- schema/table/index ownership, backups, retention, monitoring, and capacity remain host responsibilities;
- how to run the real integration suite safely.

Examples must use placeholder configuration access, never literal credentials.

## Release and governance checks

- Package manifest and documentation boundary tests pass.
- Public API baseline contains the intentional new declarations only.
- Binary compatibility preflight succeeds for existing packages and handles the new initial package according to repository policy.
- Pack/release preflight succeeds for the new package.
- No existing package accidentally gains `Microsoft.Data.SqlClient` as a dependency.
- No default package or app starts a network connection because this package exists.

## Required validation sequence

1. Build the production provider for `net8.0` and `net10.0` in Debug and Release.
2. Run the fast provider tests for both target frameworks.
3. Run the mandatory test-quality workflow and remedy real gaps.
4. Run the explicit SQL Server 2022 integration runner; require every test to execute and pass with zero skips.
5. Build the full solution in Debug and Release with zero warnings.
6. Run the full default Release test suite with zero failures.
7. Run focused release/governance tests.
8. Accept and rerun the public API baseline test after manual API review.
9. Pack the new provider and inspect package contents/dependencies.
10. Run package release and binary compatibility preflight.
11. Search source and package output to confirm there is no ORM, reflection, generic repository, secret, duplicate spike implementation, or accidental dependency propagation.
12. Record exact counts, target frameworks, image tag/digest, and any environment caveats in documentation and memory.

## Mandatory test-quality workflow

Before creating or editing test source:

1. Send the complete testing task to the existing `code_testing_generator` agent.
2. Run `find-untested-sources` against the new production project and appropriate test projects.
3. Run `test-gap-analysis` and address meaningful public behavior, edge, failure, integration, and concurrency gaps.
4. Run assertion-quality analysis after the tests pass, including the .NET assertion extension.
5. Inspect test anti-patterns/smells if the focused suite reveals suspicious setup or duplicated assertions.

The final report must include a `Requirement | Evidence` table and explicitly state:

- test frameworks and target frameworks;
- focused and full test counts;
- real-server test count and skip count;
- assertion-quality outcome;
- remaining exclusions or risks;
- package and compatibility results.

## Explicit non-goals

- No change to the core in-memory workflow runtime.
- No exactly-once distributed delivery claim.
- No distributed transaction coordinator.
- No built-in remote sink or broker delivery worker.
- No provider-agnostic relational abstraction or universal storage factory.
- No EF Core, Dapper, migrations framework, or generic repository.
- No server discovery, database creation outside the configured catalog, secret manager, health-check package, dashboard, telemetry exporter, or automatic pruning.
- No additional database engines in this round.
- No compatibility shim for the internal spike namespace.

## Completion criteria

The goal is complete only when all of the following are true:

- the gold prompt exists before production source changes and matches the delivered design;
- the new provider is independently installable and explicitly registered;
- all three established durable-output contracts are preserved;
- registration is flat, immutable after resolution, atomic, idempotent for equivalent settings, and side-effect-free;
- schema creation/validation is explicit, locked, versioned, transactional, and fail-closed;
- concurrency and compare-and-set semantics pass the shared and provider-specific real-server tests;
- default builds/tests remain free of container and network requirements;
- the spike implementation is retired without losing its evidence;
- docs, docs navigation, changelog, public API baseline, package manifest, and memory are current;
- Debug and Release builds complete with zero warnings;
- all default tests and explicit real-server tests pass with zero failures and zero skipped real-server cases;
- package creation and release/compatibility governance checks pass;
- no requested feature was silently removed and no prohibited magic or dependency graph was introduced.

## Execution evidence

- Added and packaged `FluxFlow.Engine.DurableOutput.TSql` 1.0.0 for `net8.0`
  and `net10.0` behind the three unchanged durable-output store contracts.
- Added the flat `AddFluxFlowTSqlDurableOutput(...)` registration callback,
  immutable redacting options, atomic normalized-equivalent idempotency, exact
  singleton aliases, and side-effect-free registration/resolution.
- Added direct parameterized SQL, operation-scoped pooled connections, bounded
  connection-open retry, command/schema-lock timeouts, transaction-owned schema
  locking, explicit `CreateOrMigrate`/`ValidateOnly` modes, and fail-closed
  version-1 validation without an ORM, reflection, generic repository, or
  hidden runtime mechanism.
- The fast suite passed 118/118 executions (59 cases on each of `net8.0` and
  `net10.0`) with zero warnings.
- The explicit Release `net10.0` real-server suite passed 73/73 with zero skips
  in 4 minutes 54 seconds against
  `mcr.microsoft.com/mssql/server:2022-latest`, digest
  `sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`.
- Assertion-quality review found 63 locally declared tests, at least 303 direct
  assertion expressions plus disposal helpers, and zero assertion-free,
  trivial-only, or self-referential tests.
- Serialized non-incremental Debug and Release solution builds covered 131
  projects with zero warnings/errors. The default Release suite passed
  2,086/2,086 tests across 64 test projects.
- Package manifest tests passed 4/4, the accepted public API contains 34
  intentional declarations, and release preflight plus a fresh-cache archive,
  consumer-smoke, and feed-verification dry-run passed.
- `FluxFlow.Engine.DurableOutput.TSql.1.0.0.nupkg` and its symbols package were
  created and inspected. They contain only the intended README, icon,
  `net8.0`/`net10.0` assemblies, symbols, and exact dependencies.
- Binary compatibility preparation passed. A prior-package comparison is not
  applicable until the initial 1.0.0 package is published.
- Documentation, navigation, changelog, package manifest, API baseline, and
  memory were updated. The duplicate executable spike was removed while its
  historical evidence was retained.
