# Goal: Extract Reusable Durable-Output Provider Conformance Suites

Date: 2026-08-01
Status: completed

## Objective

Turn the existing durable-output delivery and dead-letter behavioral tests into
an explicit, reusable provider-conformance specification. The suite must make it
straightforward to verify a future SQL Server, PostgreSQL, document-database, or
other durable-output provider against FluxFlow's existing contract without
copying the SQLite behavioral tests or changing the runtime design.

This is a test-architecture, documentation, and maintenance round. It must not
change production behavior, public package APIs, database schemas, package
versions, workflow behavior, registrations, or ownership rules. The current
SQL-file provider remains the only production delivery/dead-letter provider in
the repository and is used as the first real implementation of the shared
suite.

The result must preserve all existing functional coverage. Provider-neutral
delivery and dead-letter semantics move to reusable abstract suites; SQLite
schema, migration, registration, concurrency implementation, locking,
corruption, persistence, and lifecycle risks remain visibly owned by the
SQL-file test project.

This README is the complete executable specification for the round. Test edits
begin only after this file exists.

## Why This Round Comes Next

Durable-output capture already has a reusable provider-neutral conformance
suite. Delivery and dead-letter behavior currently has strong SQLite coverage,
but much of that behavior is written directly against the SQL-file concrete
store. Adding another provider in that shape would encourage copied tests,
uneven semantics, and provider-specific interpretations of the contract.

The next provider should be able to supply a fresh explicit test context and
inherit the same executable behavioral specification. This reduces duplication
and provider drift without introducing a runtime provider framework, a generic
repository, reflection, or another production dependency.

## Architectural Principles

- Apply KISS, SRP, OCP, ISP, IoC, and explicit ownership pragmatically.
- Keep the work test-only except for documentation and memory records.
- Use ordinary C#, xUnit, Shouldly, and the repository's existing real SQLite
  test infrastructure.
- Prefer immutable test data, deterministic timestamps, explicit factories,
  and isolated provider contexts.
- Keep inheritance shallow: one reusable abstract suite and one concrete
  provider subclass. Do not build a hierarchy of test framework base classes.
- Keep setup observable and local. A provider subclass must explicitly create
  the store/context; there must be no reflection, assembly scanning, convention
  discovery, service location, global mutable registry, or hidden fixture.
- Keep capability boundaries narrow. The delivery suite tests delivery-store
  behavior; the dead-letter suite tests dead-letter inspection/replay plus the
  minimum delivery operations required to establish state.
- Reuse the existing capture conformance approach where it remains the simplest
  fit. Do not rewrite working capture tests solely for visual symmetry.
- Avoid a new production project, new public testing package, new NuGet package,
  or new package dependency. The existing core durable-output test project is
  already referenced by the SQL-file test project and is the home for the
  reusable test-only specification.
- Do not weaken, rename away, or silently remove existing behavioral evidence.
  Move a test only when the shared suite asserts the same or stronger contract.
- Do not duplicate a provider-neutral behavior in both shared and SQL-specific
  suites unless a distinct SQLite implementation risk is clearly documented.

## Immutable Scope And Compatibility Boundary

The following must remain unchanged:

- every production source file unless an unexpected compile-only issue proves
  a minimal change unavoidable;
- `IDurableOutputStore`, `IDurableOutputDeliveryStore`, and
  `IDurableOutputDeadLetterStore`;
- all immutable durable-output records, enums, result types, queries, cursors,
  options, builders, and extension methods;
- dispatcher behavior and logging;
- SQL-file capture schema version 1 and delivery schema version 2;
- SQL-file table, column, index, constraint, migration, busy-timeout, and
  transaction semantics;
- DI service lifetimes, singleton aliases, ownership checks, and no-I/O
  registration behavior;
- package versions, package descriptions, dependencies, public API baseline,
  and release manifest;
- the C# DSL, JSON/application definitions, components, durable input, normal
  output routing, and `FluxFlowApplicationOptions`.

This round introduces no backward-compatibility event and therefore must not
add a changelog version entry or alter package versions.

## Required Test Infrastructure Shape

### Shared delivery context

Add one small sealed test-only context in
`tests/FluxFlow.Engine.DurableOutput.Tests` that owns the capabilities required
by the provider-neutral delivery suite.

The context must:

- expose `IDurableOutputStore` for capture setup;
- expose `IDurableOutputDeliveryStore` for lease and settlement operations;
- support one explicit asynchronous cleanup delegate;
- implement idempotent `IAsyncDisposable` cleanup;
- validate constructor/factory arguments;
- avoid database paths, SQL connections, SQLite types, provider options, or
  other provider-specific details;
- avoid a generic service bag, `IServiceProvider`, keyed lookup, casts hidden in
  test bodies, or mutable public properties.

If the same concrete provider object implements capture and delivery, the
provider adapter may pass that object through both interfaces. The shared suite
must not assume interface reference equality because a future provider may use
separate adapters over one backend.

### Shared dead-letter context

Add one small sealed test-only context for the provider-neutral dead-letter
suite.

The context must:

- expose `IDurableOutputStore` for capture setup;
- expose `IDurableOutputDeliveryStore` to create leased/dead-lettered states;
- expose `IDurableOutputDeadLetterStore` for list, exact lookup, and replay;
- support one explicit asynchronous cleanup delegate;
- implement idempotent `IAsyncDisposable` cleanup;
- contain no SQL-file knowledge or hidden provider discovery;
- not require all three capabilities to be the same object.

If keeping a single context for both suites is measurably simpler and preserves
the same narrow explicit API, that is acceptable. Do not combine contexts merely
to reduce the file count if it creates an overly broad fixture.

### Shared deterministic data

Extend or reuse `DurableOutputStoreConformanceData` for provider-neutral
envelopes, keys, timestamps, delivery requests, transitions, and replay data.
Helpers must be deterministic, side-effect free, and return fresh mutable
collections where mutation could otherwise leak between tests.

Do not move SQL schema rows, raw SQL, database file helpers, SQLite connections,
busy-timeout measurements, corruption helpers, or provider registration data
into the shared project.

### Abstract suite contract

Add two public abstract xUnit suites in the existing core test project:

- `DurableOutputDeliveryStoreConformanceTests`;
- `DurableOutputDeadLetterStoreConformanceTests`.

Each suite must have exactly one obvious provider extension point:

```csharp
protected abstract ValueTask<...TestContext> CreateStoreAsync();
```

Every test must request a fresh isolated context and dispose it with
`await using`. Do not share database state across tests, use collection-wide
fixtures, use static mutable state, add timing sleeps, or depend on execution
order.

### SQL-file adapters

Add thin sealed SQL-file subclasses that inherit the shared suites and do only
provider construction/cleanup:

- create a fresh `TemporarySqliteDatabase`;
- create the concrete SQL-file store;
- expose the three capabilities explicitly through the shared context;
- dispose the store before disposing its temporary database;
- contain no duplicated behavioral test methods.

Names should clearly connect each SQL-file class to the shared delivery or
dead-letter conformance suite.

## Provider-Neutral Delivery Specification

The reusable delivery suite must cover, at minimum, the existing public
behavior below. Tests may combine tightly related assertions when one scenario
gives stronger, clearer evidence; do not create one assertion per test merely
to inflate counts.

### Eligibility and deterministic leasing

- Captured outputs become delivery candidates when delivery is first used.
- One lease request selects at most one eligible record.
- Selection order is exact and deterministic by capture UTC time, binary
  application address, and message id.
- A not-yet-due pending record is not leased.
- At the exact due boundary the pending record is eligible.
- A currently leased record is not leased by another owner before expiry.
- At the exact expiry boundary the record is recoverable.
- Expired lease recovery uses a new token, the requesting owner, exact lease
  timestamps, and the next one-based attempt.
- Completed and dead-lettered terminal records are not eligible.

### Lease atomicity

- Concurrent lease attempts for one output have exactly one winner.
- Concurrent leasing of multiple outputs never returns one output to two
  owners and does not lose eligible outputs.
- Tests must assert keys/tokens/owners/attempts, not only result counts.
- Coordination must be deterministic and must not use `Thread.Sleep`,
  `Task.Delay` as synchronization, or random timing assumptions.

### Completion compare-and-set

- Completion applies only to the current leased key and exact current token
  before expiry.
- Success persists the exact completion timestamp and makes the record
  permanently ineligible.
- Wrong key returns not found without mutating the leased winner.
- Wrong/stale token and an expired lease return lease lost without mutation.
- Pending, already completed, and dead-lettered records return the exact
  defined non-applied state.
- Repeating completion cannot claim a second success.

### Retry compare-and-set

- Retry applies only to the current leased key and exact current token before
  expiry.
- Success stores the exact requested schedule, clears lease ownership, and
  preserves the existing attempt until the next lease increments it.
- The record is ineligible before and eligible at the exact retry boundary.
- Wrong key, wrong/stale token, expired lease, pending, completed, and
  dead-lettered states return exact documented outcomes without mutation.
- A successful retry does not duplicate, recapture, or alter the complete
  envelope.

### Cancellation and validation

- Pre-cancelled delivery operations surface cancellation and do not create or
  mutate delivery state when cancellation ownership belongs to the provider
  contract.
- Null request/transition arguments and contract value validation remain in
  contract tests unless executing them through a provider adds meaningful
  provider evidence.
- Shared tests must not assume SQLite's lazy file creation, transaction mode,
  schema initialization timing, or exception translation.

## Provider-Neutral Dead-Letter Specification

### Dead-letter transition

- The current unexpired final lease can move atomically to dead-letter state.
- The transition preserves the complete captured envelope, exact attempt, exact
  dead-letter timestamp, stable reason, and increments generation from zero to
  one.
- A dead-lettered record is not lease eligible.
- Wrong key reports not found without changing the actual leased record.
- Wrong/stale token and exact-expiry transition report lease lost without
  mutation.
- Pending, completed, and already dead-lettered records return the exact
  non-applied state.
- Concurrent dead-letter settlements have exactly one applied winner.

### Metadata-only listing

- Listing returns only dead-letter summaries and never makes payload, headers,
  error details, or other full-envelope content part of the summary contract.
- Summary fields exactly preserve key, contract name, envelope schema version,
  value/error case, capture time, attempt, reason, dead-letter time, and
  generation.
- Exact address and reason filters work independently and together.
- `DeadLetteredFrom` is inclusive and `DeadLetteredBefore` is exclusive.
- Default and maximum page sizes work.
- Page-size validation remains a contract test unless provider execution adds
  evidence.
- Pagination is stable, non-overlapping keyset pagination.
- Ordering is dead-letter UTC time descending, then binary application address,
  then message id.
- Equal timestamps preserve the exact binary key order across page boundaries.
- `HasMore`, `NextCursor`, and the final page are exact.

### Exact lookup

- Missing and non-dead-letter keys return `null`.
- A current dead letter returns complete envelope fidelity: payload, error,
  headers, trace/lineage, timestamps/offsets, contract, key, and schema version.
- Lookup returns exact attempt, reason, dead-letter timestamp, and generation.

### Explicit replay

- Replay succeeds only for the exact key, current dead-letter state, and
  expected generation.
- Success removes the record from list/get dead-letter views.
- Success preserves the complete captured envelope and capture timestamp.
- Success retains the generation, clears terminal/lease state, writes the exact
  requested next-attempt schedule, and resets attempt so the next lease is
  attempt one.
- The record is ineligible before and eligible at the exact replay schedule.
- Missing, non-dead-letter, and generation-mismatch requests return the exact
  statuses and perform no mutation.
- A later dead-letter cycle increments generation; an older generation can no
  longer replay it.
- Concurrent replay attempts have exactly one replayed winner.

### Cancellation and isolation

- Pre-cancelled list, get, replay, and dead-letter operations do not mutate
  state where the public provider contract owns cancellation.
- Failed status outcomes must leave all observable fields unchanged.
- Shared tests must not assert SQLite SQL text, row shapes, indexes, transaction
  implementation, exception types, busy-wait duration, or file-system effects.

## SQL-File-Specific Tests That Must Remain Local

The SQL-file project must continue to own tests for:

- capture-only schema remaining version 1 and delivery schema being created
  lazily at version 2;
- exact SQLite tables, columns, checks, foreign keys, indexes, and owned object
  names;
- complete version-1-to-version-2 migration, including pending, leased, and
  completed rows, timestamps, tokens, attempts, and offsets;
- migration cancellation/rollback and rejection of future, unversioned,
  malformed, corrupt, or partially upgraded schemas;
- SQL-file registration, same-singleton aliases, conflict/tamper detection,
  idempotency, service lifetimes, and registration without file/schema I/O;
- SQLite write-lock/busy-timeout bounds and action-specific error translation;
- corrupt row detection without repair;
- database disposal, reopen, persistence, connection-pool/file lifecycle, and
  configured creation behavior;
- durable input/output coexistence in one database file;
- SQL-specific atomicity/concurrency tests whose purpose is the SQLite
  implementation mechanism rather than the provider contract.

After moving behavior into the shared suites, split or rename the remaining SQL
test classes if that makes ownership clearer. Do not leave a large
`SqlFileDurableOutputDeliveryTests` or `SqlFileDurableOutputDeadLetterTests`
whose remaining methods mix unrelated schema, locking, corruption, migration,
and lifecycle concerns. Prefer cohesive existing SQL-specific classes when an
appropriate home already exists; avoid unnecessary new files.

## Duplicate-Removal Rules

- Build a before/after test-ownership matrix in `.testagent/plan.md`.
- For each moved test, record its original SQL-file test name and its resulting
  shared conformance test name.
- Remove the original behavioral method only after the SQL-file conformance
  subclass discovers and passes the shared replacement.
- Keep a SQL-local test when it verifies an additional provider-specific risk,
  even if its setup happens to exercise a shared behavior.
- Where one mixed test contains both provider-neutral behavior and SQLite
  schema assertions, extract the behavior to the shared suite and retain a
  narrower schema-focused SQL test.
- Preserve or strengthen exact assertions for keys, tokens, attempt counts,
  timestamps and offsets, transition statuses, generations, ordering, cursor
  boundaries, complete-envelope fidelity, and absence of mutation.
- Do not reduce total meaningful behavior merely to reduce test count or lines
  of code.

## Test-Agent Workflow And Evidence

Use the repository's mandatory testing workflow.

Before editing tests:

1. Verify xUnit, Shouldly, target framework, project references, and solution
   discovery conventions.
2. Run the Roslyn static source-to-test pairing analyzer exactly once at the
   narrowest relevant durable-output scope.
3. Record the command, heuristic output, limitations, current test inventory,
   and architectural observations in `.testagent/research.md`.
4. Record a requirement-to-test and old-to-new ownership map in
   `.testagent/plan.md`.
5. Maintain `.testagent/status.md` while implementing and validating.

The static pairing result is a source-to-test heuristic, not line/branch
coverage and not proof of adequacy.

Implementation and verification requirements:

- use xUnit and Shouldly consistently with the repository;
- use real temporary SQLite for the concrete provider;
- use deterministic fixed times and explicit concurrency coordination;
- add no sleeps, network calls, external services, mocked database behavior, or
  probabilistic assertions;
- run focused builds and tests for both durable-output test projects;
- prove shared inherited tests are discovered through the SQL-file test
  project and the solution-level test harness;
- run a final requirement-gap review, pseudo-mutation review, and assertion
  quality audit over touched tests;
- report exact generated/shared test names in the final completion evidence.

## Documentation And Memory

Update documentation only where the new contributor/testing architecture is
material. Runtime/user behavior has not changed, so avoid rewriting public
usage pages or implying a new product capability.

Required records:

- add a concise provider-conformance section to the most relevant
  durable-output provider README or extension documentation;
- explain that a new provider should implement the three narrow production
  capabilities and run capture, delivery, and dead-letter conformance suites;
- distinguish shared behavioral conformance from provider-specific schema,
  migration, locking, corruption, registration, and lifecycle tests;
- update documentation-site navigation only if a new page is genuinely more
  cohesive than a small addition to an existing provider page;
- update `memory/00-index.md`, `memory/01-current-state.md`, and
  `memory/07-progress-log.md`;
- add a new detailed memory record documenting decisions, file ownership,
  exact validation evidence, limitations, and the next recommended step;
- keep this goal README's final status and execution summary accurate.

Do not change `CHANGELOG.md`, `eng/packages.json`, public API baselines, or
package versions unless validation proves an accidental difference. If such a
difference appears, stop and resolve it rather than accepting it as part of
this round.

## Explicit Exclusions

Do not add or change:

- another production storage provider;
- a public test SDK/package or shared test NuGet package;
- runtime provider discovery, a provider registry, reflection, assembly
  scanning, code generation, source generation, or dynamic proxies;
- a universal storage abstraction or generic repository;
- SQL Server, PostgreSQL, MongoDB, RavenDB, LiteDB, or cloud-specific code;
- workflow checkpoints, orchestration history, exactly-once delivery,
  distributed transactions, leader election, or distributed locks;
- dispatcher parallelism, batching, backoff policy, transport adapters,
  automatic replay, retention, archival, purge, operator API/UI/CLI, or
  authorization;
- changes to the Fluent C# DSL, JSON schema, component registration, normal
  workflow execution, durable input, or `FluxFlowApplicationOptions`;
- new package dependencies, package version changes, or public API changes.

## Validation And Completion Gates

The goal is complete only when all applicable gates pass:

1. This full goal exists at
   `goals/2026-08-01-durable-output-provider-conformance-suite/README.md` before
   any test-source edit.
2. The mandatory test-agent research, plan, and status records are complete and
   identify exact requirement/test ownership.
3. Shared delivery and dead-letter contexts/suites compile without references
   to the SQL-file provider or SQLite.
4. Thin SQL-file subclasses instantiate the shared suites with fresh isolated
   real databases.
5. All moved provider-neutral behavior is discovered and passes from the
   SQL-file test project.
6. Remaining SQL-file tests are cohesive and cover schema, migration,
   registration, locking, corruption, lifecycle, coexistence, persistence, or
   distinct implementation atomicity risks.
7. No existing feature or meaningful test evidence is lost; duplicate
   behavioral tests are removed only after verified replacement.
8. Focused core and SQL-file test projects build in Debug and Release with zero
   warnings.
9. Focused tests pass deterministically and report exact discovered/passed
   counts.
10. Solution-level test discovery includes the inherited conformance tests.
11. Final gap, pseudo-mutation, and assertion-quality audits find no unresolved
    high-risk contract gap.
12. Formatting verification passes for every touched project/file.
13. Release governance and public API/package manifest checks confirm no
    production surface, package version, dependency, or package-content change.
14. Serialized non-incremental full-solution Debug and Release builds pass with
    zero warnings.
15. The serialized full Release test suite passes.
16. Existing package/archive inspection passes without test infrastructure
    leaking into production packages.
17. Documentation, documentation-site content where relevant, memory index,
    current state, progress log, detailed memory, and this goal record are
    internally consistent.
18. Final status/diff review confirms the round touched only intended files and
    preserved every unrelated dirty-worktree change.

## Required Final Report

Report:

- the saved goal README path;
- the reusable suite/context design and why it stays simple;
- which behaviors moved to shared conformance and which remained SQL-specific;
- any duplicate tests/files removed or narrowed;
- focused and full build/test counts;
- solution-discovery, format, governance, API/package, and archive results;
- documentation and memory paths;
- a compact `Requirement | Evidence` table with exact generated/shared test
  names;
- confirmation that runtime behavior, public API, schemas, versions, and
  package dependencies did not change;
- remaining limitations and the next recommended independent step.

Do not claim completion from compilation alone. Mark the execution goal complete
only after the shared suites, provider adapter, test cleanup, documentation,
memory, governance, packaging checks, and final validation are genuinely done.

## Execution Result

Completed on 2026-08-01.

- Added two narrow sealed contexts and two public abstract suites with exactly
  one provider factory each: 12 delivery and 13 dead-letter conformance tests.
- Added two thin SQL-file adapters and replaced the old mixed provider classes
  with seven delivery and six dead-letter infrastructure tests.
- Preserved every provider-specific schema, migration, registration, locking,
  corruption, persistence, reopen, coexistence, concurrency, and lifecycle
  responsibility.
- Focused Debug and Release runs passed 117 core and 118 SQL-file tests with zero
  warnings; inherited suites passed 12 delivery and 13 dead-letter tests.
- Formatting, one-time static inventory, ownership mapping, gap,
  pseudo-mutation, and assertion-quality gates passed.
- Release governance passed 111 tests. Serialized non-incremental Debug and
  Release builds passed across 129 projects with zero warnings. The serialized
  full Release suite passed 1,968 tests across 62 projects.
- Both 2.0.0 package preflights and both package/symbol archive inspections
  passed.
- Updated the existing SQL-file documentation page, both durable-output package
  READMEs, memory index/current state/progress, and detailed memory record 277.
- Confirmed no current-round runtime behavior, production API, schema, package
  version, package dependency, workflow, DSL, JSON, component, durable-input,
  or application-options change.
