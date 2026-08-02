# GOAL: Add explicit, bounded retention for terminal durable records

## Status

- State: complete
- Date: 2026-08-01
- Repository: FluxFlow
- Scope: durable input and durable output persistence only
- Compatibility posture: additive public API; no behavior change unless a host explicitly invokes retention

## Objective

Add a small, explicit retention capability that lets operators permanently remove old terminal durable records in bounded batches without turning FluxFlow into a scheduler, archival system, or heavyweight workflow platform.

The feature must preserve the engine's current lightweight in-process model. It must be optional, provider-owned, deterministic, cancellation-aware, and easy to operate. It must use direct provider SQL and existing persistence boundaries. It must not introduce reflection, an ORM, background workers, timers, hidden cleanup, broad abstractions, or new application-level configuration.

The implementation must follow KISS, SRP, and dependency-inversion principles:

- Retention is a separate optional capability.
- Existing store, dead-letter, lease-renewal, delivery, and status interfaces remain unchanged.
- Core packages define provider-neutral contracts only.
- Provider packages own physical deletion and transaction details.
- Hosts decide when and how often to invoke retention.
- The operation returns only the information needed to drive bounded cleanup.

## Why this is the next step

Durable input records intentionally remain after delivery as deduplication tombstones, and durable output captures remain after successful delivery. Dead-letter records also remain until an operator takes an explicit action. This is correct for reliability, diagnostics, replay, and idempotency, but unlimited retention causes storage growth.

Automatic cleanup is deliberately not appropriate for the core engine because retention duration is a business and operational policy. An explicit bounded API gives hosts control without adding scheduling or policy machinery to FluxFlow.

## Required public API

### Durable input contracts

Add the following immutable public records to `FluxFlow.Engine.DurableInput`:

```csharp
public sealed record DurableInputRetentionRequest
{
    public const int DefaultMaxCount = 100;
    public const int MaximumMaxCount = 1_000;

    public DurableInputRetentionRequest(
        DateTimeOffset terminalBefore,
        ApplicationAddress? address = null,
        int maxCount = DefaultMaxCount);

    public DateTimeOffset TerminalBefore { get; }
    public ApplicationAddress? Address { get; }
    public int MaxCount { get; }
}

public sealed record DurableInputRetentionResult
{
    public DurableInputRetentionResult(int deletedCount);

    public int DeletedCount { get; }
}
```

Add this separate optional capability:

```csharp
public interface IDurableInputRetentionStore
{
    ValueTask<DurableInputRetentionResult> PurgeDeliveredAsync(
        DurableInputRetentionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DurableInputRetentionResult> PurgeDeadLettersAsync(
        DurableInputRetentionRequest request,
        CancellationToken cancellationToken = default);
}
```

### Durable output contracts

Add the corresponding immutable records to `FluxFlow.Engine.DurableOutput`:

```csharp
public sealed record DurableOutputRetentionRequest
{
    public const int DefaultMaxCount = 100;
    public const int MaximumMaxCount = 1_000;

    public DurableOutputRetentionRequest(
        DateTimeOffset terminalBefore,
        ApplicationAddress? address = null,
        int maxCount = DefaultMaxCount);

    public DateTimeOffset TerminalBefore { get; }
    public ApplicationAddress? Address { get; }
    public int MaxCount { get; }
}

public sealed record DurableOutputRetentionResult
{
    public DurableOutputRetentionResult(int deletedCount);

    public int DeletedCount { get; }
}
```

Add this separate optional capability:

```csharp
public interface IDurableOutputRetentionStore
{
    ValueTask<DurableOutputRetentionResult> PurgeCompletedAsync(
        DurableOutputRetentionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DurableOutputRetentionResult> PurgeDeadLettersAsync(
        DurableOutputRetentionRequest request,
        CancellationToken cancellationToken = default);
}
```

### Contract validation

The records are immutable value-style request/result objects. Do not convert them to mutable options classes.

Constructors must validate immediately:

- `maxCount` must be between `1` and `MaximumMaxCount`, inclusive.
- An invalid `maxCount` throws `ArgumentOutOfRangeException` with the correct parameter name.
- `deletedCount` must be non-negative.
- A negative `deletedCount` throws `ArgumentOutOfRangeException` with the correct parameter name.
- `ApplicationAddress` validity remains owned by `ApplicationAddress`; do not duplicate its rules.
- `DateTimeOffset` is accepted as an instant. Providers compare its UTC ticks while the request retains the supplied value.
- Do not add mutable setters, `init` setters, normalization callbacks, validation frameworks, or options infrastructure.

Do not add `HasMore`, continuation tokens, deleted identifiers, deleted payloads, page objects, or total-count queries. A host repeats calls until `DeletedCount < MaxCount`. If the returned count equals the limit, another batch may or may not exist.

## Exact retention semantics

### Common rules

Every purge operation must:

- Delete only records in the terminal state named by the method.
- Use an exclusive cutoff: a record qualifies only when its relevant terminal timestamp is strictly earlier than `TerminalBefore`.
- Delete at most `MaxCount` records.
- Use one provider transaction for selecting and deleting the batch.
- Return the number actually deleted, from `0` through `MaxCount`.
- Support an optional exact application-address scope.
- When `Address` is null, consider all application addresses.
- When `Address` is supplied, match it with the provider's existing exact ordinal/binary address semantics.
- Select candidates deterministically by terminal timestamp ascending, then application address ascending, then message identifier ascending.
- Avoid reading, returning, logging, or deserializing message payloads.
- Honor cancellation while preparing and executing the operation.
- Roll back when cancellation or failure occurs before commit.
- Once commit begins successfully, complete commit with a non-cancelable token so the returned outcome is not ambiguous.
- Remain safe when multiple purge callers or normal store operations run concurrently.

There is no cross-call snapshot guarantee. New state changes can happen between bounded calls. The caller owns orchestration and repetition.

### Durable input delivered retention

`PurgeDeliveredAsync` must delete only input rows whose:

- state is `Delivered`; and
- `delivered_at_utc_ticks` is strictly less than the request cutoff.

It must never delete pending, leased, or dead-lettered input rows. A delivered row with no delivery timestamp is not eligible.

Deleting a delivered input tombstone ends the deduplication window for that key. After deletion, a later enqueue with the same durable input identity can be accepted as new work. This consequence must be prominent in API documentation and operational documentation.

### Durable input dead-letter retention

`PurgeDeadLettersAsync` must delete only input rows whose:

- state is `DeadLettered`; and
- `dead_lettered_at_utc_ticks` is strictly less than the request cutoff.

It must never delete pending, leased, delivered, or replayed input rows. A dead-letter row with no dead-letter timestamp is not eligible.

Purging a dead letter is irreversible and removes its replay source. If replay and purge race, exactly one state transition wins:

- If replay wins first, the row is no longer terminal dead-letter state and purge must not delete it.
- If purge wins first, replay observes the existing not-found behavior.

### Durable output completed retention

`PurgeCompletedAsync` must select only delivery rows whose:

- state is `Completed`; and
- `delivered_at_utc_ticks` is strictly less than the request cutoff.

For every selected delivery row, delete the parent durable output capture row in the same transaction. The existing foreign-key cascade then removes its associated delivery row. Do not delete only the delivery row: doing so would let delivery materialization recreate it and could cause unintended redelivery.

It must never delete:

- pending delivery rows;
- leased delivery rows;
- dead-lettered delivery rows;
- capture-only rows that have not been materialized into the delivery table; or
- completed rows without a completion timestamp.

Deleting the capture parent ends the output idempotency/history window for that identity. A later capture using the same identity can be accepted as new work. Document this prominently.

### Durable output dead-letter retention

`PurgeDeadLettersAsync` must select only delivery rows whose:

- state is `DeadLettered`; and
- `dead_lettered_at_utc_ticks` is strictly less than the request cutoff.

For every selected row, delete the parent durable output capture row in the same transaction so the delivery row is removed through the existing cascade. Do not delete only the delivery row.

It must never delete pending, leased, completed, replayed, or unmaterialized capture-only rows. A dead-letter row without a dead-letter timestamp is not eligible.

Purging an output dead letter is irreversible. The same replay-versus-purge rule applies: replay first makes the row ineligible; purge first makes replay observe not found.

## Provider implementation

Implement the capability in all four existing persistence providers:

- durable input SQL-file provider;
- durable input T-SQL provider;
- durable output SQL-file provider;
- durable output T-SQL provider.

Each existing concrete provider must implement the corresponding retention interface directly. Keep retention in focused partial files so query and transaction details do not inflate the existing store files.

### SQL-file providers

- Use the provider's existing connection, schema lifecycle, write lock, and transaction conventions.
- Use parameterized SQL only.
- Use a bounded ordered subquery or CTE to select exact keys and delete them in the same write transaction.
- Let the output foreign-key cascade remove delivery rows after deleting capture parents.
- Preserve current busy/locking behavior and map errors through existing provider conventions.
- Do not load a candidate list into application memory merely to issue one delete per row.

### T-SQL providers

- Use the provider's existing connection, schema lifecycle, transaction, command, timeout, and error conventions.
- Use parameterized SQL only.
- Use a bounded ordered candidate set and a single set-based delete.
- Use suitable row/update locking consistent with current provider behavior so concurrent purge callers cannot both count the same deletion.
- Use `OUTPUT` or the affected-row result to return the exact deleted count without a separate count query.
- Delete output capture parents and rely on the current cascade for delivery rows.

### Schema lifecycle and migrations

This feature must not introduce a new table, column, trigger, stored procedure, schema version, or migration.

Retention methods are provider mutation operations and may invoke the provider's existing lazy schema initialization and migration path. In particular, invoking output retention opts into delivery-state storage and may initialize the already-existing delivery schema in the same way as other output delivery capabilities. Merely registering or resolving the retention alias must remain I/O-free. Capture-only hosts that never call an output retention method remain capture-only and untouched.

Do not add retention indexes in this round. Existing schemas and indexes are sufficient for the first bounded implementation. If production measurements later show that terminal-state scans need provider-specific indexes, handle that as a measured schema-migration round.

## Dependency injection and registration

Update each provider registration extension so the retention capability follows the repository's established alias rules:

- The concrete provider remains the one owner instance.
- The primary store interface, existing optional capabilities, and the new retention interface resolve to that same exact concrete singleton.
- Repeating the same provider registration remains idempotent.
- A conflicting provider registration remains an explicit failure.
- A pre-registered or tampered retention alias is rejected consistently with existing alias validation.
- Registering or resolving aliases must perform no database or file-system I/O.
- Do not add a new registrar, factory layer, service locator, reflection-based discovery, named-provider system, or application-options setting.

## Concurrency and reliability invariants

The implementation and tests must demonstrate:

- Two concurrent purge calls cannot both report deleting the same record.
- A row whose state changes out of the requested terminal state before deletion is not deleted.
- Pending, leased, retryable, and unmaterialized rows are always preserved.
- Output deletion is parent-and-child atomic through the existing foreign key.
- A failed or canceled batch leaves no partial committed batch.
- Normal at-least-once delivery and recovery guarantees remain unchanged for records that are not purged.
- Retention is permanently destructive by design and must never run implicitly.

Provider-level serialization may be used where already established. Do not introduce distributed locks or cross-process coordination services. Database transaction and locking semantics are the source of truth across processes.

## Fluent API and host usage

No new application DSL node or `FluxFlowApplicationOptions` property is required. Retention is an operational service, not workflow design metadata.

Document simple host-owned usage through dependency injection, for example:

```csharp
var retention = services.GetRequiredService<IDurableInputRetentionStore>();
var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

DurableInputRetentionResult result;
do
{
    result = await retention.PurgeDeliveredAsync(
        new DurableInputRetentionRequest(
            terminalBefore: cutoff,
            maxCount: 250),
        cancellationToken);
}
while (result.DeletedCount == 250);
```

The example must make clear that:

- the host chooses the cutoff and schedule;
- cutoff is captured once for a multi-batch run;
- batches prevent an unbounded transaction;
- a production loop should observe cancellation and may add host-owned pacing;
- completing the loop does not establish a snapshot against concurrent writes;
- purging terminal identities changes their future deduplication behavior.

Do not add a timer, hosted service, cron abstraction, retention policy builder, automatic loop, or default retention duration.

## Testing requirements

Follow the repository's current xUnit style and test architecture. Use generated test records where required by the testing workflow, then independently review all generated tests for semantic quality.

### Contract tests

Cover input and output request/result records:

- default batch size;
- minimum and maximum accepted batch sizes;
- zero, negative, and above-maximum rejection;
- correct parameter names;
- retention of the supplied cutoff and optional address;
- non-negative result count validation;
- immutable public shape and public API baselines.

### Provider behavior tests

For every provider, cover at least:

- empty store returns zero;
- fewer than the limit;
- exactly the limit;
- more than the limit across repeated calls;
- exclusive cutoff boundary;
- UTC-equivalent cutoffs with non-zero offsets;
- optional exact address filtering;
- deterministic oldest-first selection;
- delivered/completed purge;
- dead-letter purge;
- pending preservation;
- leased preservation;
- opposite terminal-state preservation;
- missing terminal timestamp preservation where representable;
- replayed dead-letter preservation;
- cancellation before execution;
- result never exceeds requested maximum;
- same identity can be accepted again after its terminal tombstone/capture is purged;
- output capture parent and delivery child are removed together;
- output unmaterialized capture-only row is preserved;
- a new provider instance observes committed deletions.

### Concurrency tests

Cover meaningful races without timing sleeps where possible:

- concurrent purge calls do not double-count;
- replay versus dead-letter purge has one valid winner;
- normal terminal transition versus purge cannot delete a row in the wrong state;
- cancellation or forced failure before commit does not partially delete a batch.

### Registration tests

For every provider, verify:

- retention alias is resolvable;
- retention alias is the same exact concrete singleton as all existing aliases;
- same-provider repeated registration is idempotent;
- provider conflicts are rejected;
- alias tampering is rejected;
- registration and alias resolution remain I/O-free.

### Real backend tests

Run both real T-SQL integration runners with zero skipped tests. Add equivalent retention coverage to the real-backend suites, including bounded deletion, state preservation, address filtering, cutoff behavior, output cascade behavior, and persistence across provider instances.

Do not claim T-SQL success from in-memory doubles or SQL-file tests.

### Assertion quality

Tests must assert externally observable behavior and exact state transitions. Avoid assertions that only prove setup, copy implementation expressions, use broad exception types, or rely on tautological count checks. Cancellation and concurrency tests must prove the relevant postcondition by reopening or querying the store.

## Documentation requirements

Add `docs/36-durable-terminal-retention.md` and include it in documentation navigation.

Update all relevant documentation surfaces:

- root README and public API overview where appropriate;
- durable input and output documentation;
- SQL-file and T-SQL provider package READMEs;
- status, dead-letter, delivery, and operational guidance that describes terminal records;
- documentation-site navigation and cross-links;
- changelog;
- package release notes;
- examples showing DI resolution and bounded host-owned loops.

Documentation must clearly state:

- retention is optional and explicit;
- nothing runs automatically;
- exact terminal states and timestamps used;
- exclusive cutoff semantics;
- maximum batch size;
- address scoping;
- output capture-parent deletion and cascade behavior;
- replay/purge race semantics;
- no snapshot guarantee across calls;
- irreversible loss of dead-letter replay data;
- the deduplication/idempotency window ends when a terminal record is purged;
- the host owns schedule, pacing, monitoring, and policy;
- invoking output retention may initialize the existing delivery schema, while registration/resolution remains I/O-free.

Avoid claims of exactly-once processing, archival, legal data-lifecycle compliance, or cross-provider identical query plans.

## Versioning and compatibility

This is additive public API and provider functionality. Update package versions as follows:

- `FluxFlow.Engine.DurableInput`: `1.2.0` -> `1.3.0`
- `FluxFlow.Engine.DurableInput.SQLFile`: `1.2.0` -> `1.3.0`
- `FluxFlow.Engine.DurableInput.TSQL`: `1.1.0` -> `1.2.0`
- `FluxFlow.Engine.DurableOutput`: `2.1.0` -> `2.2.0`
- `FluxFlow.Engine.DurableOutput.SQLFile`: `2.1.0` -> `2.2.0`
- `FluxFlow.Engine.DurableOutput.TSQL`: `1.1.0` -> `1.2.0`

Update package metadata and release notes to describe the new capability accurately.

Update public API baseline files for both target frameworks. Run the baseline gate in check mode first and use accept mode only for the intended additive API changes. Review the diff so no unrelated public member is added or removed.

Run package preparation and binary-compatibility gates. If comparison packages are unavailable because these versions or their predecessors are not published, report that as an environmental limitation rather than a pass.

## Dependency and security hygiene

- Add no new runtime or test dependency unless there is a demonstrated necessity.
- Do not add an ORM or generic repository.
- Keep direct SQL localized in the provider partial files.
- Remove the obsolete SQL-file vulnerability suppressions only if the current resolved dependency version is confirmed outside the affected range by the repository's vulnerability scan.
- Run the repository dependency policy and vulnerability gates after any suppression cleanup.
- Do not expose connection strings, payloads, or sensitive message metadata in exceptions, logs, docs, or test output.

## Explicit non-goals

Do not implement any of the following in this round:

- automatic or scheduled cleanup;
- a hosted retention worker;
- retention periods in application options;
- a retention DSL or nested callback builder;
- archival, export, cold storage, or soft delete;
- legal-hold or compliance policy management;
- tombstone compaction beyond the explicit operations here;
- pending/leased/retryable record cleanup;
- abandoned-lease recovery changes;
- dead-letter bulk replay;
- deletion by message identifier;
- continuation tokens or cursor persistence;
- dashboard, administration UI, HTTP endpoint, or command-line tool;
- metrics or telemetry packages specific to retention;
- background checkpoints;
- distributed locks;
- reflection or convention-based provider discovery;
- a universal persistence abstraction;
- new schema objects, versions, migrations, or indexes;
- changes to workflow execution, Fluent DSL design, component registration, or `FluxFlowApplicationOptions`;
- FileSystem/SQL backend registration consolidation;
- MQTT lifecycle changes;
- session-helper registration changes.

## Implementation sequence

1. Save this goal before production or test-source edits.
2. Record the repository's current durability test pairing and test-gap baseline.
3. Add immutable input and output retention contracts and XML documentation.
4. Update public API baselines for the intended additive surface.
5. Add provider retention partials using bounded, set-based, transactional deletes.
6. Add the retention interfaces to the four concrete provider declarations.
7. Add same-instance DI aliases and preserve all registration invariants.
8. Generate the required test records/tests, then independently review and refine them.
9. Add contract, provider, registration, concurrency, persistence, and real-backend coverage.
10. Update package versions, release notes, docs, navigation, changelog, examples, goal evidence, and memory.
11. Remove obsolete vulnerability suppressions only after the resolved-version check proves they are unnecessary.
12. Run focused tests, full solution tests, real T-SQL runners, public API gates, package gates, dependency policy, format verification, vulnerability scan, and release build.
13. Record exact commands, pass/fail counts, skips, warnings, and any honest environmental limitations in this goal.

## Acceptance criteria

The goal is complete only when all of the following are true:

- The input and output retention contracts exist with the exact bounded semantics above.
- All four providers implement the appropriate capability using direct set-based SQL.
- Existing public interfaces remain unchanged.
- All aliases resolve to the same concrete singleton and registration invariants are preserved.
- Delivered input, input dead-letter, completed output, and output dead-letter records can be purged independently.
- No pending, leased, retryable, replayed, opposite-terminal, or unmaterialized record is accidentally removed.
- Cutoff, address, deterministic ordering, maximum count, transaction, cancellation, and concurrency behavior are tested.
- Output capture parents and delivery children are removed atomically.
- Documentation prominently explains destructive and deduplication consequences.
- No automatic cleanup, new schema version, new dependency, reflection, or application-level policy is introduced.
- All package versions, release notes, API baselines, docs, changelog, memory, and this goal evidence are current.
- Focused and full tests pass on both target frameworks.
- Both real T-SQL integration runners pass with zero skips.
- Public API, dependency policy, formatting, vulnerability, package, and release-build gates pass, except that unavailable published comparison artifacts are reported honestly.

## Verification matrix

Record final evidence for these gates:

| Gate | Required evidence |
|---|---|
| Contract and provider tests | command, framework(s), passed/failed/skipped |
| Registration tests | same-instance, idempotency, conflict, tamper, I/O-free cases |
| Full solution | command, framework(s), passed/failed/skipped |
| Real T-SQL input | command, passed/failed/skipped |
| Real T-SQL output | command, passed/failed/skipped |
| Public API | check result, accepted intentional additions, re-check result |
| Package preparation | command and produced artifacts |
| Binary compatibility | result or precise unavailable-artifact limitation |
| Dependency policy | command and result |
| Vulnerability scan | command, affected packages, suppression disposition |
| Formatting | command and result |
| Release build | command and result |

## Completion evidence

### Delivered surface

- Added immutable, provider-neutral input and output retention requests,
  results, and separate optional retention-store interfaces. Requests preserve
  the supplied offset, use an exclusive UTC-instant cutoff, optionally scope
  by exact application address, default to 100 records, and reject batch sizes
  outside 1 through 1,000. Results reject negative delete counts.
- Implemented the capability on the existing SQL-file and T-SQL input/output
  singleton stores in four focused partial files. Each call performs one
  deterministic, parameterized, set-based, bounded delete in one transaction.
- Added exact same-instance DI aliases while preserving idempotent registration,
  provider-conflict rejection, alias-tamper rejection, and I/O-free
  registration/resolution.
- Added no worker, timer, automatic policy, ORM, generic repository, reflection,
  schema object/version/index, application option, DSL surface, or dependency.
- Updated the six package versions and release notes, public API baseline,
  package READMEs, root and site documentation, navigation, changelog, goal,
  and memory. Current package-graph scans allowed the two obsolete SQL-file
  vulnerability suppressions to be removed.

### Test-generation and review evidence

- The mandatory pre-edit pairing inventory ran once over 1,048 C# files: 753
  source files, 295 test files, 520 paired sources, and 233 heuristic unpaired
  sources. The result was used for discovery, not as a coverage claim.
- Independent generated tests cover contract shape and validation, shared
  provider conformance, provider-specific physical behavior, registration,
  concurrency, cancellation, rollback/recovery, persistence, schema
  preservation, payload non-hydration, and the destructive/idempotency effects.
- The final gap, pseudo-mutation, and assertion audit found no effective
  assertion-free retention tests, no trivial-only tests, no self-referential
  assertions, and no weakened assertion needed to obtain a pass. Full details
  and exact test names are recorded in `.testagent/status.md`.

### Verification matrix

| Gate | Final evidence |
|---|---|
| Focused durability matrix | `dotnet test` in Release with no restore over the six final test projects passed 844/844 executions with zero failures, skips, or warnings: input core 144, output core 149, input SQL-file 127, output SQL-file 152, input T-SQL 138, output T-SQL 134. The T-SQL fast totals include both target-framework runs. |
| Contract/provider/registration filters | Input/output contract filters passed 12/12 each; shared SQL-file conformance passed 11/11 and 12/12; provider-specific SQL-file retention passed 3/3 and 4/4; registration suites passed 14/14 and 16/16. |
| Full solution | `dotnet test FluxFlow.sln --configuration Release --no-restore --no-build --maxcpucount:1` passed 2,424/2,424 tests across 66 projects with zero failures, skips, or warnings. A prior parallel wrapper reached its five-minute process limit; the exact surviving test process and result directory were removed, its remaining Resilience project passed 11/11 independently, and the complete serialized rerun is authoritative. |
| Real T-SQL input | The Release runner with explicit license acceptance passed 89/89 tests with zero failures/skips in 5m09s. Retention-only conformance had independently passed 11/11 and its provider-specific case 1/1. |
| Real T-SQL output | The Release runner with explicit license acceptance passed 100/100 tests with zero failures/skips in 5m54s. Shared retention conformance had independently passed 12/12 and the corrected provider-specific case 1/1. |
| Real-server identity/cleanup | Both runners used `mcr.microsoft.com/mssql/server:2022-latest` at digest `sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`; runner-owned containers were removed. Two earlier command-wrapper timeouts were diagnosed and their exact disposable containers/processes/temp directories were cleaned before the full successful runs. |
| Public API | The check-first baseline gate reported the expected additions. `FLUXFLOW_ACCEPT_PUBLIC_API_BASELINE=1` accepted the current manifest, and the immediate baseline check passed 1/1. A final `FullyQualifiedName~PublicApiBaselineTests` check-mode filter passed 2/2. The accepted baseline also incorporates earlier unaccepted dirty-tree API/manifest work already present before this round; it was reviewed rather than discarded. |
| Package versions/governance | The exact six-case retention version guard passed 6/6. Package release governance passed 117/117, and the filtered documentation/package-convention checks passed 20/20. All six release preflights passed at the intended versions and changelog entries. |
| Package preparation/consumers | All six package and symbol archives passed archive inspection, feed verification, and isolated fresh-cache consumer smoke tests on both `net8.0` and `net10.0` (12 provider/framework package-consumer passes). Current dependency-closure packages were rebuilt first to avoid stale same-version global-cache artifacts; the global package cache was not deleted. |
| Binary compatibility | Preparation succeeded for all six lines. Actual validation passed for input core 1.3.0 versus 1.2.0. The five other predecessor packages were unavailable from the configured feeds: input SQL-file 1.2.0, input T-SQL 1.1.0, output core 2.1.0, output SQL-file 2.1.0, and output T-SQL 1.1.0. Output-core restore selected 2.2.0 because 2.1.0 was missing, so that is recorded as unavailable rather than a compatibility pass. |
| Dependency/vulnerability policy | Release package-convention/governance tests passed. `dotnet list ... package --vulnerable --include-transitive` on both SQL-file provider projects reported no vulnerable packages from the configured sources; obsolete suppressions were removed and no dependency was added. |
| Formatting/whitespace | `dotnet format FluxFlow.sln --verify-no-changes --no-restore` passed. `git diff --check` passed after the final records were updated. |
| Release build | `dotnet build FluxFlow.sln --configuration Release --no-restore` completed 133 projects with zero errors and zero warnings. |

The goal is complete. The only unavailable evidence is binary comparison
against five predecessor packages that do not exist on the configured feeds;
this does not conceal or convert the environmental limitation into a pass.
