# Durable Output Provider Conformance Suite

Date: 2026-08-01

## Outcome

The durable-output provider contract now has reusable executable specifications
for all three independent capabilities:

- capture through `DurableOutputStoreConformanceTests`;
- delivery through `DurableOutputDeliveryStoreConformanceTests`; and
- dead-letter inspection/replay through
  `DurableOutputDeadLetterStoreConformanceTests`.

The SQL-file provider is the first concrete adapter for the complete suite. A
future provider can supply fresh explicit contexts and inherit the same public
behavior tests without copying SQLite tests or changing Engine, workflows, the
C# DSL, JSON, dispatcher behavior, or production interfaces.

This round changed tests, documentation, goal records, and memory only. Runtime
behavior, public API, capture schema version 1, delivery schema version 2,
package versions 2.0.0, package dependencies, registration ownership, and
deployment semantics are unchanged.

The accepted executable scope is recorded in
`goals/2026-08-01-durable-output-provider-conformance-suite/README.md`.

## Design

Two small sealed test contexts expose only the capabilities required by their
suite:

- `DurableOutputDeliveryStoreTestContext` exposes capture and delivery;
- `DurableOutputDeadLetterStoreTestContext` exposes capture, delivery, and
  dead-letter operations.

Each context validates its dependencies, owns one asynchronous cleanup delegate,
and is idempotently disposable. The interfaces may be backed by different
objects; the shared tests do not assume reference equality or resolve services
through a container.

Each abstract suite has one provider extension point:

```csharp
protected abstract ValueTask<...TestContext> CreateStoreAsync();
```

Every test creates a fresh context with `await using`. There is no reflection,
provider discovery, service location, global registry, static mutable fixture,
database-specific base class, sleep, or test-order dependency.

The two SQL-file subclasses contain construction and cleanup only. They create
one fresh temporary database and concrete store; cleanup disposes the store
before the database in `try/finally`.

## Shared Behavioral Floor

The 12 delivery conformance methods cover:

- deterministic captured-output ordering and exact due boundary;
- active-lease exclusion and exact-expiry recovery;
- new token/owner/timestamps and one-based attempt progression;
- completed/dead-lettered terminal ineligibility;
- one-output and many-output one-winner leasing;
- completion statuses, key/token/expiry compare-and-set, exact timestamp, and
  permanent ineligibility;
- retry statuses, exact schedule, envelope fidelity, attempt progression, and
  single eligibility boundary; and
- pre-cancelled lease/settlement non-mutation.

The 13 dead-letter conformance methods cover:

- exact transition status, reason, timestamp/offset, attempt, envelope, and
  generation one;
- wrong key/token, stale/expired lease, invalid state, and concurrent
  one-winner settlement;
- metadata-only summaries, independent/combined filters, inclusive lower and
  exclusive upper time bounds;
- default/maximum page execution, stable non-overlapping keyset pages, and
  equal-time binary address/message ordering;
- null lookup and complete value/error envelope fidelity;
- replay statuses, exact scheduling boundary, envelope preservation, attempt
  reset, generation retention/cycles, stale-generation rejection, and
  concurrent one-winner replay; and
- pre-cancelled list/get/replay/settlement non-mutation.

The shared suites initialize lazy delivery state through public delivery
operations before asserting pending-state outcomes. They do not require capture
itself to materialize provider-specific delivery rows.

## SQL-Specific Ownership

The old mixed `SqlFileDurableOutputDeliveryTests` and
`SqlFileDurableOutputDeadLetterTests` were removed only after inherited
replacements were discovered and passed. Their provider-specific evidence now
lives in cohesive infrastructure suites:

- seven delivery tests retain lazy schema/version/column behavior, exact
  persisted completion, single-row retry reuse, multiple-connection lease
  atomicity, busy-lock recovery, no-I/O cancellation, and disposal behavior;
- six dead-letter tests retain exact row encoding, multiple-connection
  settlement/replay atomicity, reopen persistence, busy-lock translation, and
  corrupt-row rejection without repair.

Existing SQL schema, v1-to-v2 migration, registration, coexistence,
persistence, corruption, and lifecycle suites remain intact. The two internal
schema helpers that the static analyzer did not pair lexically remain exercised
through those real SQLite tests. Passing shared conformance is a behavioral
floor, not a substitute for provider schema, migration, locking, corruption,
restart, registration, deployment, or resource-lifecycle tests.

## Test Workflow And Evidence

The required test workflow verified .NET 10, xUnit, Shouldly, project
references, and real SQLite conventions before edits. Its single corrected
Roslyn inventory ran before test changes and reported:

- 17 production source files;
- 26 focused test files;
- 15 lexical source/test pairings; and
- two lexically unpaired internal schema helpers.

The inventory is an identifier/namespace heuristic, not line or branch
coverage. Research, the old-to-new ownership matrix, requirement mapping,
progress, pseudo-mutation review, gap analysis, and assertion-quality audit are
recorded under `.testagent/`.

Focused final evidence:

- inherited delivery conformance: 12/12 passed;
- inherited dead-letter conformance: 13/13 passed;
- Debug core: 117/117 passed;
- Debug SQL-file: 118/118 passed;
- Release core: 117/117 passed;
- Release SQL-file: 118/118 passed;
- focused Debug and Release build: 10 projects, zero errors/warnings;
- focused formatting verification: passed;
- pseudo-mutation, gap, and assertion-quality reviews: no unresolved scoped
  survivor, gap, trivial assertion, timing-only assertion, success-only group,
  mocked SQLite path, or sleep-based synchronization.

Repository-wide evidence:

- release governance and public API/package conventions: 111/111 passed;
- both 2.0.0 package release preflights: passed;
- serialized non-incremental Debug solution build: 129 projects, zero
  errors/warnings;
- serialized non-incremental Release solution build: 129 projects, zero
  errors/warnings;
- serialized full Release suite: 1,968/1,968 passed across 62 projects with zero
  warnings;
- both 2.0.0 `.nupkg` and `.snupkg` archives passed inspection.

The first preflight invocation was blocked by the machine's PowerShell execution
policy before the repository script ran. Repeating it with process-scoped
`ExecutionPolicy Bypass` succeeded for both packages; no source or system policy
was changed.

## Documentation And Governance

The existing SQL-file durable-output documentation page and both package
READMEs now explain the three reusable conformance suites, explicit provider
contexts, and the separate backend-specific test responsibilities. No new
documentation-site page or navigation entry was added because this is a
contributor/testing refinement of the existing provider page, not a new runtime
feature.

`CHANGELOG.md`, `eng/packages.json`, the public API baseline, production source,
schemas, package versions, and package dependencies received no current-round
semantic change.

## Remaining Limits

- SQL-file remains the only production durable-output provider.
- The shared suites are test infrastructure in the repository, not a public
  test SDK or NuGet package.
- They define public behavior but do not prescribe backend schema, migration,
  transaction implementation, deployment topology, or operational tooling.
- Delivery remains serial and at-least-once; no transport, automatic replay,
  retention, batching, parallelism, distributed coordination, workflow
  checkpoint, or exactly-once guarantee was added.

## Next Recommended Independent Step

Use the complete conformance floor for a bounded second-provider feasibility
spike driven by a real deployment requirement. Choose the backend only after
documenting its consistency model, transaction/conditional-write primitives,
schema or index lifecycle, migration/deployment process, locking/concurrency,
and operational ownership. Keep the provider in its own package with flat
registration and immutable settings; do not add a universal repository or a
runtime provider registry. If no concrete shared-database requirement exists,
stop here—the test boundary is ready and the lightweight SQL-file path remains
the simplest supported option.
