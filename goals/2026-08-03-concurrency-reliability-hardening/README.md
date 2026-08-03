# Goal: Harden Post-Release Concurrency Reliability

## Status

- State: complete
- Date: 2026-08-03
- Repository: `C:\Projects\FluxFlow`
- Accepted base branch: `main`
- Accepted base commit: `f06345021d2547120f8fc2f60031abb85cb6ca53`
- Working branch: `work/concurrency-reliability`
- Runtime scope: T-SQL durable-input leasing only if real-provider evidence
  proves a production defect
- Test scope: the repeated real-provider lease anomaly and the two
  load-sensitive timing tests exposed during the coordinated release train

## Objective

Determine why concurrent T-SQL durable-input batch leasing twice returned a
`5 + 0` split for ten due rows instead of two disjoint batches of five during
the coordinated package train, then eliminate the proven root cause without
weakening the behavioral contract. Separately remove the causal timing
assumptions from the two unrelated tests that timed out under the same broad
hosted workload.

The completed round must leave one of two honest outcomes:

1. production defect: correct the smallest T-SQL query/index/transaction
   boundary, prove the fix under repeated real-server contention, advance only
   `FluxFlow.Engine.DurableInput.TSql` to the required compatible patch version,
   and publish only that package after normal review and all gates; or
2. test defect: preserve production bytes and versions, replace the flawed
   scheduling assumption with deterministic causal synchronization, and do not
   publish a package.

Do not decide between these outcomes before evidence exists.

## Release Evidence That Opens This Goal

The canonical release train completed successfully, but four workflows needed
safe pre-publication recovery:

- `components-validation`, run `30786674537`, timed out in
  `ComponentEventTests.Registered_factories_expose_traced_addressable_component_events`;
- `components-timers`, run `30786663467`, timed out in
  `RequestReplyCoordinatorTests.Fault_FailsInFlightCallers_AndFaultsCompletion`;
- `components-http`, run `30786539863`, completed the real durable-input suite
  88/89 because the multi-owner lease test observed one empty batch; and
- `components-resilience-composition`, run `30794438388`, reproduced the same
  88/89 provider result and later passed when rerun alone.

All four failures occurred before publication. The exact versions and releases
were absent before isolated reruns. This goal does not reopen or move any
published package tag.

## Current Behavioral Contract

`IDurableInputStore.LeaseAsync(...)` must atomically claim at most the requested
count of eligible rows. Concurrent owners must never receive the same row. For
ten due rows and two concurrent requests of five:

- each owner receives no more than five leases;
- at least one bounded batch is claimed while both calls compete;
- the two key sets are disjoint;
- every returned lease owns a unique nonempty token and the requested owner;
- work skipped because another lease transaction held a lock remains eligible
  immediately after that transaction commits; and
- one subsequent bounded lease drains the remainder so the combined key set
  contains all ten due rows exactly once and persisted state agrees.

The current provider uses one read-committed transaction and an updateable
candidate query with `UPDLOCK`, `READPAST`, `ROWLOCK`, and the eligibility
index. These hints are part of the hypothesis surface, not a predetermined fix.

## Non-Negotiable Principles

1. Diagnose before editing production SQL.
2. Do not make a flaky test green with a larger timeout, arbitrary delay,
   retry loop, test skip, reduced assertion, or serialized global execution.
3. Use causal barriers and observable state when test synchronization is
   required. Avoid scheduler luck.
4. Preserve KISS, SRP, explicit dependencies, and operation-scoped provider
   ownership. Add no ORM, reflection, service locator, background coordinator,
   generic repository, or new dependency.
5. Preserve all public APIs, registration callbacks, immutable options,
   provider-neutral contracts, schema versions, table shapes, and Engine
   behavior unless a concrete production correction absolutely requires a
   schema change. A schema change is a stop-and-replan condition.
6. Preserve atomic leasing, disjoint ownership, exact-token transitions,
   cancellation semantics, deterministic ordering, and bounded batch sizes.
7. Keep ordinary CI server-free. Real network-provider stress remains in the
   explicit provider/release boundary.
8. Never log connection strings, generated credentials, payloads, lease
   tokens, message identities, or full exception details from shared systems.
9. Update the goal, relevant documentation, `memory/00-index.md`,
   `memory/01-current-state.md`, and one new numbered memory record.
10. Use normal branch, commit, pull-request, check, and merge behavior. No
    force push, tag movement, administrative bypass, or duplicate publication.

## Explicit Non-Goals

- no new workflow feature or component;
- no DSL, JSON, registration, or configuration redesign;
- no new durability provider or provider abstraction;
- no broad persistence rewrite or speculative index redesign;
- no change to durable-output behavior;
- no automatic retry that hides a failed lease contract;
- no binary-compatibility automation in this round; that remains the next
  independent release-hardening opportunity after concurrency is trustworthy;
- no unrelated cleanup.

## Phase 1: Baseline And Reproduction

1. Require clean synchronized `main`, record the base commit, and create the
   neutral working branch.
2. Record the existing query, eligibility index, transaction isolation,
   current test synchronization, provider runner, image identity, and cleanup
   behavior.
3. Run the single multi-owner test repeatedly against a real isolated server.
   Use a bounded repetition count high enough to expose nondeterminism without
   turning the test into an unbounded soak.
4. Repeat with small explicit matrices such as:
   - two owners, ten rows, five each;
   - two owners, uneven row counts;
   - four owners with bounded batches; and
   - a mix of pending and expired leased rows.
5. On any mismatch, capture only safe evidence:
   - returned counts and owner labels;
   - distinct/duplicate key counts without values;
   - aggregate persisted state counts;
   - transaction result/timeout/deadlock category;
   - query plan or lock category only when it can be collected without
     credentials or sensitive identifiers.
6. Do not change production code merely because the release environment was
   busy. Require a causal explanation consistent with the observed result.

## Phase 2: Root-Cause Classification

Evaluate these possibilities without assuming any is correct:

- the test starts tasks together but does not prove both transactions reached
  the candidate query concurrently;
- `READPAST` skips locks needed to reach otherwise eligible rows under a
  particular plan or lock granularity;
- the forced eligibility index requires noncovering lookups whose locks affect
  the second reader;
- a page/table lock appears despite the row-lock request;
- the fixed timestamp or seeded rows do not have the eligibility state the
  test assumes;
- initialization/schema work overlaps the first lease operation;
- command timeout, cancellation, or runner resource pressure is converted into
  a misleading empty result; or
- the expected `5 + 5` fairness contract is stronger than the provider's
  documented contract. If so, stop and resolve the product contract explicitly
  rather than weakening the test silently.

Classification evidence must identify whether the defect belongs to
production behavior, test orchestration, or the stated contract.

## Contract Resolution (Completed During Diagnosis)

The release assertion was stronger than the public and provider-specific
contracts. `DurableInputLeaseRequest.MaxCount` is an upper bound, while the
documented T-SQL protocol deliberately uses `READPAST` so competing queue
workers skip locked work instead of blocking. SQL Server documents that
`READPAST` is intended to reduce work-queue contention and skips row-level
locks. Therefore an empty result for one simultaneous caller is a valid
transient outcome when the other transaction owns the candidate locks; it is
not evidence that work was lost.

Removing `READPAST` to force `5 + 5` would change the provider from cooperative
skip-locked leasing to blocking leasing and would contradict its published
behavior. Production SQL, schema, and package bytes therefore remain unchanged.
The regression must instead prove the actual safety and progress properties:
bounded batches, exclusive keys/tokens, correct owner attribution, and immediate
availability of any skipped remainder after the competing transactions commit.

## Phase 3A: Production Correction, Only If Proven

If production leasing can leave eligible rows unclaimed during concurrent
bounded leasing:

1. Change the smallest query/index/transaction detail that guarantees the
   existing contract.
2. Prefer one atomic statement and the existing operation-scoped connection.
3. Do not introduce application locks, a global process semaphore, polling,
   retry-until-full behavior, or cross-process coordination outside the
   database transaction.
4. Preserve deterministic return ordering after commit.
5. Verify execution-plan/index behavior for both pending and expired rows.
6. Add a regression that fails against the prior behavior under the proven
   causal setup.
7. Keep the schema version unchanged unless a schema migration is demonstrably
   required; any required migration pauses this goal for explicit replanning.

## Phase 3B: Test Correction, Only If Proven

If production behavior is sound and the test is nondeterministic:

1. Replace task-scheduler simultaneity assumptions with the smallest causal
   synchronization seam.
2. Synchronize at an observable operation boundary, not with a sleep.
3. Preserve boundedness, disjointness, owner/token correctness, and exact
   ten-row coverage after one post-contention drain. Do not assert scheduler- or
   lock-plan-dependent `5 + 5` fairness for simultaneous `READPAST` callers.
4. Keep production classes free of test-only public APIs. Prefer test-owned
   orchestration, a provider-local internal seam only when essential, or a
   database-side deterministic barrier owned by the test fixture.
5. Prove repeated success under parallel repository load.

## Phase 4: Secondary Timing Tests

For the component-event and request/reply tests:

1. identify the exact event/fault/task transition each test must observe;
2. replace race-prone timeout-only coordination with a test-owned causal gate;
3. retain a short bounded timeout only as a deadlock guard;
4. preserve or strengthen assertions for trace/address/event identity and
   in-flight caller fault/completion behavior;
5. make no production behavior change unless a real defect is independently
   demonstrated; and
6. run each affected test project repeatedly and under parallel test load.

The mandatory test-generation pipeline owns `.testagent/research.md`,
`.testagent/plan.md`, and `.testagent/status.md`, and must map every behavioral
requirement to exact test names and assertions.

## Phase 5: Verification

Run the narrowest gate after each change, then the decisive governing gates:

1. repeated single-test real-server execution for the multi-owner lease case;
2. the complete durable-input real-provider suite with zero failures and zero
   skips, repeated enough to establish stability;
3. focused secondary test projects, including repeated and parallel runs;
4. all durable-input fast/provider-neutral tests;
5. the complete durable-output real-provider suite as cross-provider
   regression evidence;
6. Release governance;
7. a serialized warning-free Release build;
8. the complete Release solution tests;
9. formatting and direct/transitive dependency vulnerability gates;
10. package preflight, archive inspection, public API baseline, and binary
    compatibility for any changed packable project; and
11. cleanup of every test-owned server, database, result directory, package
    archive, cache, diagnostic, and temporary worktree.

Any failure is a stop condition. Fix its root cause, rerun the narrow affected
gate, then repeat the decisive governing gate. Never convert an environment
failure into a pass.

## Version And Publication Decision

After implementation and before review:

- test-only correction: no package version, changelog, tag, release, or public
  package mutation;
- production correction without public API/schema change: advance only
  `FluxFlow.Engine.DurableInput.TSql` from 1.2.0 to 1.2.1, add its changelog and
  provider documentation note, and prove binary compatibility against 1.2.0;
- public API or schema change: stop and request explicit replanning rather than
  selecting a version automatically.

If 1.2.1 is required, publish it only after the correction merges normally.
Use the existing guarded release helper, require public absence before the tag,
run the full release workflow, and independently verify the exact tag target,
package/symbol assets, public-feed consumer, and release target. No other
package is republished unless actual packed dependency metadata proves it is
required.

## Review And Closeout

1. Inspect the complete diff and scan new project-visible names for neutrality.
2. Commit only goal-owned files with a neutral subject.
3. Push and open a ready pull request against `main`.
4. Require successful remote checks on the exact head.
5. Review the production/query and test assertions; resolve every actionable
   finding. If self-approval is prohibited, record it and use no bypass.
6. Merge normally using the repository's established merge-commit strategy.
7. Synchronize local `main` with `origin/main` and require a clean worktree.
8. If a patch package is required, publish from the exact clean merge commit,
   verify it independently, then merge a small evidence-only documentation
   update normally.
9. Mark the active goal complete only after all required code, evidence,
   publication, documentation, memory, cleanup, and synchronization work is
   complete.

## Acceptance Criteria

This goal is complete only when:

- the repeated `5 + 0` observation has a documented causal explanation;
- the final behavior satisfies the explicit multi-owner lease contract;
- deterministic regression coverage proves disjoint exact coverage under real
  provider contention;
- neither the primary nor secondary tests depend on scheduler luck, arbitrary
  sleeps, increased timeouts, retries, or skips;
- public APIs, configuration, provider ownership, and schema remain unchanged
  unless separately replanned;
- package version/publication matches the test-only versus production decision;
- all focused, provider, repository, release, formatting, dependency, and
  package gates pass with exact evidence;
- no secret or test-owned resource leaks;
- goal, docs, memory index/current state, and a new memory record are updated;
  and
- local `main` is clean and synchronized after normal review and merge.

## Completion Evidence

Implementation, verification, review, merge, cleanup, and synchronization are
complete.

### Root-Cause Decisions

- Archived release attempts `30786539863` and `30794438388` confirmed either
  simultaneous T-SQL owner could receive zero while the other received five.
  `MaxCount` is an upper bound and the published provider uses `READPAST` to
  skip row locks; equal `5 + 5` fairness was an invalid test-only assumption.
- The corrected multi-owner test proves bounded disjoint leases, exact owner
  and token identity, one immediate remainder drain, exact ten-row coverage,
  and persisted leased state.
- A deterministic test holds update row locks on five exact queue entries,
  proves another store leases the other five, rolls back, then proves the held
  five are immediately recoverable. Removing only `READPAST` makes this test
  fail by command timeout; the restored production file matches the base hash.
- Both secondary timing failures published before registering a receiver on a
  non-replaying `FlowOutput<T>`. Each test now registers first and asserts the
  complete event or request/fault state transition. Existing timeout guards are
  unchanged.

### Local Verification

| Gate | Result |
|---|---|
| Timing-test focused repetition | 8/8 per test plus one concurrent pass |
| Full affected projects | Composition 123/123 twice; RequestReply 27/27 twice |
| T-SQL input real provider | 90/90, zero skips |
| T-SQL output real provider | 117/117, zero skips |
| Pinned provider image | `sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89` |
| Restore | 134 projects, zero errors/warnings |
| Serialized Release build | 134 projects, zero errors/warnings |
| Complete Release solution | 2,509/2,509 across 66 projects, zero warnings |
| Release governance | 141/141, zero warnings |
| Formatting and whitespace | solution format and `git diff --check` clean |
| Dependencies | no direct or transitive vulnerable package reported |
| Package boundary | `git diff -- src` empty; production/package hashes match base |
| Cleanup | no goal-owned container or temporary result directory remains |

### Review And Merge

| Evidence | Result |
|---|---|
| Implementation commit | `ef98b4ad59a0a1b547e4d37a99c8133521a14c97` |
| Pull request | `#71`, ready, clean, and mergeable |
| Exact-head CI | run `30832186883`, `build-test` succeeded on `ef98b4a` |
| Review findings | no comments and no actionable diff findings |
| Self-approval | host rejected author self-approval; no bypass used |
| Merge | normal merge commit `da9f1d0be93b55461577c8a92aacbea589715cac` |
| Synchronization | local `main` clean and equal to `origin/main` at the merge commit |

This evidence-only closeout changes no code, test, project, manifest, package,
version, dependency, API, schema, tag, release, or public artifact.

### Version And Publication Decision

The final diff contains tests, documentation-site content, goal, and memory
only. No production source, package-local README, project, dependency,
manifest, public API, schema, or packed input changes. Consequently no package
version, changelog, tag, repository release, or public package is changed, and
package archive/API/binary-compatibility gates have no changed packable target.
