# Durable Output Dead-Letter Operations

Date: 2026-08-01

## Outcome

FluxFlow now supports an optional bounded durable-output failure cycle without
making the normal Engine or default delivery path heavier. A host may configure
a positive maximum delivery-attempt count; null remains the default and
preserves unlimited fixed retry. A non-cancellation handler failure on the final
configured attempt atomically moves the current unexpired lease to a durable
dead-letter state.

Operators can independently resolve a provider-supplied
`IDurableOutputDeadLetterStore` for bounded metadata-only keyset listing, exact
full-envelope lookup, and generation-protected one-record replay. Replay is
explicit, schedules pending work, resets attempts, and never invokes the handler
directly.

The accepted executable scope is recorded in
`goals/2026-08-01-durable-output-dead-letter-operations/README.md`. The goal
convention now stores every future accepted implementation prompt in its own
dated directory as a proper `README.md`; older standalone goal records remain
unchanged.

## Core Decisions

- `DurableOutputDeliveryOptions` remains an immutable record and gains nullable
  `MaxDeliveryAttempts`; its temporary flat registration builder gains the same
  scalar property. Null means unlimited and positive values are exact limits.
- Attempt numbers remain one-based. Failed attempts below the limit retry at
  the existing fixed delay; the failed limit attempt dead-letters. Success at
  the limit completes normally.
- `DurableOutputDeliveryDeadLetter` carries only key, lease token, exact time,
  and stable `DurableOutputDeadLetterReason.HandlerFailure`.
- `IDurableOutputDeliveryStore` owns `DeadLetterAsync(...)` beside completion
  and retry because all three are mutually exclusive settlements of one lease.
- `IDurableOutputDeadLetterStore` is separate because listing, lookup, and
  replay are operator capabilities rather than dispatcher responsibilities.
- Summaries deliberately exclude payloads, headers, structured error data,
  lineage, and exception data. Exact lookup is the only operation returning the
  complete envelope.
- Listing is bounded to 1..200 items and uses dead-letter time descending plus
  binary address/message ordering with a stable keyset cursor.
- Replay requires the current positive generation. It preserves the envelope
  and generation, resets attempts to zero, clears settlement data, and uses the
  caller's exact next-attempt time. A later dead-letter increments generation,
  making a stale operator view safe.
- Dispatcher logs use stable metadata and exception type rather than handler or
  provider exception messages, payloads, headers, or error details.
- The dispatcher stays serial and owns no queue, batch, policy graph,
  reflection, transport selection, or parallel execution setting.

Adding `DeadLetterAsync(...)` to `IDurableOutputDeliveryStore` is breaking for
custom 1.x delivery providers. `FluxFlow.Engine.DurableOutput` therefore moves
honestly to `2.0.0` rather than presenting the change as a compatible minor.

## SQL-File Schema And Operations

`SqlFileDurableOutputStore` remains one DI-owned singleton and now implements
all three capture, delivery, and operator interfaces. Registration aliases all
three to the same instance, rejects conflicting/tampered ownership before
mutation, and performs no file/database work.

The independent delivery schema moves from version 1 to version 2:

- state 4 is `DeadLettered`;
- stable reason, exact dead-letter UTC ticks/offset, and generation columns are
  added;
- row checks make pending, leased, completed, and dead-lettered fields mutually
  consistent;
- the eligibility index remains; and
- a partial dead-letter index matches the public keyset order.

The first delivery or operator operation transactionally migrates version 1.
The migration validates the known v1 shape/index/state, creates the exact v2
table, copies pending/leased/completed records losslessly, initializes empty
dead-letter metadata/generation zero, replaces the old table and indexes, and
updates the version only inside the same commit. Cancellation, corruption,
future/unversioned/partial schema, or SQL failure rolls back. Capture schema and
co-located durable-input tables remain untouched.

Dead-letter settlement is a key/state/token/unexpired-lease compare-and-set that
clears ownership/completion fields, stores stable metadata, and increments
generation. Listing selects metadata columns only. Exact lookup joins the
immutable capture. Replay is an exact state/generation compare-and-set. SQLite
transactions provide one winner under concurrent settlement or replay.

Capture-only hosts still do not initialize or touch delivery schema.

## Test Workflow And Evidence

The mandatory independent test workflow read the accepted goal, verified the
existing xUnit/Shouldly/real-SQLite conventions and project references, and ran
the Roslyn static source-to-test pairing analyzer exactly once before test
edits. The narrow snapshot found 15 source files, 24 test files, 13 statically
paired sources, and two lexically unpaired schema helpers. This is a parsing
heuristic, not line or branch coverage. Research, requirement mapping, exact
test inventory, progress, pseudo-mutation review, and assertion-quality review
are retained under `.testagent/`.

The round added 45 test methods producing 51 cases. New tests cover immutable
contracts and guards, flat option/registration behavior, unlimited/final-attempt
dispatcher branches, cancellation/log privacy/wrong-key/store-failure behavior,
same-singleton aliases, exact schema v2 and v1 migration/rollback, token and
state CAS boundaries, metadata filters/keyset ordering, full-envelope lookup,
generation replay cycles, concurrency, busy timeout, corruption, persistence,
and shared-file coexistence.

Final evidence:

- provider-neutral focused suite: 117/117 passed;
- SQL-file focused real-SQLite suite: 104/104 passed;
- focused Debug and Release runs: zero warnings;
- focused production/test formatting verification: passed;
- final pseudo-mutation review: no unresolved scoped survivor;
- final assertion-quality review: no zero/trivial/timing-only/success-only or
  mocked-SQLite gap;
- release governance: 111/111 passed;
- public source-declaration baseline: reviewed, regenerated, and revalidated;
- package release preflight: passed for both 2.0.0 package aliases;
- package/archive inspection: both `.nupkg` and `.snupkg` artifacts passed for
  both target frameworks and expected README/assembly/symbol metadata;
- serialized non-incremental Debug solution build: 129 projects, zero errors,
  zero warnings;
- serialized non-incremental Release solution build: 129 projects, zero errors,
  zero warnings;
- final serialized Release solution suite: 1,954/1,954 passed across 62 test
  projects with zero warnings.

No production defect remained after the focused tests and static audits. An
earlier independent full-suite run observed one unrelated RequestReply timeout;
that test passed immediately in isolation, and the final authoritative
serialized suite passed all 1,954 tests.

## Documentation And Governance

- Both packages are versioned `2.0.0` with updated description, tags, release
  notes, package README, and changelog entries.
- Root package guidance, public API overview, runtime architecture, reliable
  delivery, capture, SQL-file, and delivery documentation describe the new
  boundary and unchanged limits.
- `docs/30-durable-output-dead-letter-operations.md` is the focused operator
  guide and is linked from the documentation index.
- The public API baseline includes the reviewed immutable contracts and the
  intentional breaking store member.
- Goal, memory index, current-state, and progress records use the same package,
  state, guarantee, and version terminology.

## Preserved Boundaries And Remaining Limits

- Normal Engine outputs and unselected capture paths are unchanged.
- Capture-only and unlimited-retry hosts preserve existing behavior.
- Workflow/application JSON, the C# DSL, components, durable input, and
  `FluxFlowApplicationOptions` are unchanged.
- Delivery remains serial and at-least-once; destination idempotency by
  `DurableOutputEnvelope.Key` remains a host responsibility.
- No transport adapter, automatic/bulk replay, retention/purge/archive,
  operator endpoint/UI/CLI, variable backoff, batching, parallelism,
  multi-destination routing, distributed coordination, producer/business-state
  transaction, workflow checkpoint, or exactly-once guarantee was added.

## Next Recommended Independent Step

Before implementing another database, extract a reusable provider-conformance
suite for the complete delivery/dead-letter capability. It should exercise
lease eligibility, expiry, completion/retry/dead-letter CAS, ordering,
generation replay, and one-winner concurrency against any provider factory.
Then a second concrete backend can implement the existing provider-neutral
contracts with much lower semantic drift. Keep that future provider in its own
package with its own settings, migration/deployment model, and flat registration;
do not introduce a universal repository or move backend settings into
`FluxFlowApplicationOptions`.
