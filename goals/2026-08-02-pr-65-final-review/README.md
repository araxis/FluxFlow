# GOAL: Perform the final release-candidate review of pull request 65

## Status

- State: complete
- Date: 2026-08-02
- Repository: FluxFlow
- Branch: `work/major-surface-reset`
- Pull request: 65, targeting `main`
- Baseline head: `9c74edb5`
- Baseline remote state: draft, clean merge state, ordinary CI successful
- Scope: evidence-backed final review, bounded remediation, local verification,
  remote verification, memory, and ready-for-review transition
- Explicit stop boundary: no merge, tag, release, package publication, or
  release-workflow dispatch

## Role And Execution Instruction

Act as the senior maintainer responsible for deciding whether this large major
release candidate is ready for formal review and eventual merge. Review the
actual pull-request diff against `origin/main`; do not infer readiness solely
from the green test count or historical goal records.

Create this complete goal before review-driven source changes. Execute every
review phase, fix only concrete findings, preserve the intended major breaking
change, record exact evidence, push the final review commits, obtain successful
remote CI on the final head, and convert the pull request from draft to ready.

Favor KISS, SRP, explicit ownership, direct dependencies, immutable
configuration, bounded asynchronous work, and provider-local persistence. Do
not introduce reflection, discovery magic, generic repositories, service
locators, hidden workers, speculative interfaces, new dependencies, or
compatibility shims while reviewing.

## Baseline And Context

The candidate intentionally contains the accumulated canonical-runtime reset
and optional durability work. It is a large but linear change set based directly
on the remote `main` head. The pre-review state has:

- a clean and synchronized local worktree;
- a draft pull request with no merge conflict;
- 126 commits ahead of the remote base and none behind;
- approximately 1,708 changed files representing the accepted accumulated
  goals rather than an accidental single-round expansion;
- a successful clean detached-worktree restore, serialized Release build,
  complete solution suite, Release-governance suite, formatting gate,
  vulnerability audit, and both explicit real T-SQL provider suites;
- a successful ordinary remote restore/build/test workflow on head
  `9c74edb5`; and
- no merge, tag, publication, or release action.

This review must validate the candidate's final shape. It must not reconstruct
or squash its history, restore removed compatibility surfaces, or start a new
product capability.

## Objectives

1. Confirm the pull-request scope, commit ancestry, remote state, and repository
   hygiene are understood and internally consistent.
2. Review the architecture and public surface for one canonical, explicit,
   lightweight runtime and authoring path.
3. Review registration and C# DSL ergonomics for flat, predictable builder
   actions without nested callback chains or hidden activation behavior.
4. Review optional durability contracts and providers for honest guarantees,
   atomic transitions, cancellation, ownership, recovery, lifecycle, privacy,
   and operational safety.
5. Review packages, versions, manifests, public API baselines, migration
   boundaries, archives, and release workflow ordering.
6. Review documentation and samples for consistency with implemented behavior
   and the intentional breaking-change posture.
7. Review tests and CI for meaningful cross-platform, concurrency, lifecycle,
   and real-provider coverage without skips, sleeps, or weakened assertions.
8. Review performance-sensitive paths and dependency shape for avoidable
   allocations, repeated parsing, sync-over-async, unbounded work, reflection,
   cycles, or unjustified packages.
9. Resolve every proven blocking or significant finding with the smallest
   cohesive correction and regression evidence.
10. Record the completed review in goal and memory, push the final head, obtain
    successful remote CI, and make pull request 65 ready for review.

## Safety And Scope Boundaries

- Treat all committed branch content as authoritative accepted work unless the
  current review proves a defect.
- Do not rewrite, rebase, squash, amend, or force-push the branch.
- Do not merge or update `main` in this goal.
- Do not tag, publish, create a release, or dispatch the release workflow.
- Do not change public APIs, schemas, package versions, or guarantees for style
  alone.
- Do not revive obsolete aliases, compatibility packages, migration helpers,
  or the retired runtime/hosting paths.
- Do not move optional durability into the core engine or require external
  infrastructure in ordinary CI.
- Do not add a database, ORM, serializer, mapper, validation framework, test
  framework, analyzer, or workflow action.
- Do not weaken tests, add skips, increase semantic timeouts, introduce retries,
  or hide failures to obtain a green result.
- Do not expose payloads, credentials, connection strings, exception content,
  high-cardinality identifiers, or application data in metrics/logs.
- Preserve unrelated user-owned files if the worktree changes unexpectedly.

## Finding Severity And Disposition

Classify review findings before changing code:

- P0: data loss, security exposure, corrupted release, or generally unsafe
  runtime behavior. Must be fixed before readiness.
- P1: incorrect public behavior, broken guarantee, lifecycle/concurrency defect,
  or release blocker. Must be fixed before readiness.
- P2: meaningful maintainability, performance, portability, documentation, or
  test-quality defect with a bounded correction. Fix in this goal when it does
  not expand architecture.
- P3: optional polish or speculative improvement. Record or discard; do not
  churn the candidate for it.

Every finding must identify the exact file/line or contract, reachable failure
mode, severity, and evidence. A large file, personal style preference, or
possible future need is not a finding.

## Phase 1: Pull-request And Repository Integrity

1. Refresh the remote and confirm `origin/main` is the pull-request base.
2. Confirm the branch is zero commits behind, has a single linear ancestry from
   the base, and has no unexpected merge commits or rewritten history.
3. Inspect pull-request metadata, changed-file count, commit count, checks,
   review threads, and comments through the connected repository service.
4. Verify local and remote head identity and a clean worktree.
5. Inspect changed filenames and Git state for build output, test results,
   databases, logs, credentials, editor state, temporary files, or externally
   branded generated artifacts.
6. Run whitespace and conflict-marker checks across the complete diff.
7. Confirm deletions correspond to documented retirement/migration decisions
   and additions correspond to saved goals, documentation, tests, or active
   projects.

## Phase 2: Architecture And Canonical Surface Review

Review the Engine, Nodes, Composition, Fluent, component packages, adapters,
samples, and migration documents for:

- one canonical runtime path and one application/revision ownership model;
- explicit composition and DI boundaries without a service locator;
- small interfaces with concrete consumers and no speculative abstraction;
- immutable descriptors, definitions, options, and catalog snapshots;
- flat registration/build actions with no nested callback hierarchy;
- deterministic identifiers, addresses, link ownership, and validation;
- correct cancellation, completion, disposal, and failure propagation;
- bounded queues/dataflow capacity and intentional concurrency;
- no reflection scanning, assembly discovery, global mutable registry, hidden
  static state, or dynamic code generation;
- no provider implementation leaking into engine/domain orchestration; and
- documentation/API baselines matching the canonical surface.

Trace representative end-to-end paths rather than reviewing types in isolation:

1. service registration to immutable component catalog;
2. C# DSL application/resource/workflow construction to serialized definition;
3. definition validation/compilation to runtime assembly and execution;
4. typed input through node/link/output capture; and
5. host start, revision replacement, stop, failure, and disposal.

## Phase 3: Registration And DSL Review

Verify every component family follows the intended familiar shape:

- package-owned `IServiceCollection` extension;
- flat `Action<TBuilder>` configuration;
- component-specific builder methods and immutable final option records;
- consistent `AddComponent` entry shape without forcing every component into
  the same internal option type;
- no mutable option object escaping registration;
- no nested callbacks beyond the documented maximum practical depth;
- explicit resource handles and typed node/port handles;
- deterministic fluent capture/out-variable behavior;
- exact validation for null, duplicate, missing, or incompatible values; and
- no registration-time I/O or hidden hosted service.

Compare representative simple, resource-backed, trigger, adapter, and
durability registrations. Use existing matrix/governance tests as evidence,
then inspect the production implementations that those tests characterize.

## Phase 4: Optional Durability Review

Review durable input and output core contracts, SQL-file providers, T-SQL
providers, dispatchers, registration, instrumentation, operations, and samples.

Required checks:

- at-least-once guarantees and host-owned destination idempotency are stated
  honestly;
- capture/enqueue idempotency distinguishes equivalent, existing, and conflict
  states atomically;
- lease acquisition, exact-token renewal, expiry, settlement, retry,
  dead-letter, replay, status, and retention transitions are guarded and
  transactional;
- stale owners cannot settle, renew, replay, or delete active work;
- cancellation never produces a false terminal transition;
- background dispatchers are opt-in, bounded, serial where documented, and
  stopped/disposed by the host;
- retries and readiness are bounded and do not use arbitrary sleeps;
- schema creation/migration/validation modes are explicit and fail closed;
- local and network providers implement the same conformance floor while
  preserving provider-specific SQL/lifecycle ownership;
- connection/command/transaction disposal is complete on success, exception,
  timeout, and cancellation;
- status queries remain payload-free and do not create/repair schema;
- retention documents destructive deduplication/replay consequences;
- diagnostics have bounded tags, preserve trace causality, and cannot change
  durable behavior when listeners throw; and
- credentials, connection strings, payloads, exceptions, addresses, and
  high-cardinality identifiers are not emitted.

Any correction to provider behavior requires its focused fast suite plus the
corresponding real-provider runner. Test/document-only changes do not justify a
new real-server run when the previously validated production head is unchanged.

## Phase 5: Package, Public API, And Release Review

1. Compare source package projects, solution entries, central package versions,
   `eng/packages.json`, public API baseline, documentation inventory, and
   changelog.
2. Confirm every package version reflects the intentional compatibility change
   and every new project has the expected package metadata/readme/license/icon.
3. Confirm project references follow package boundaries and no removed package
   remains referenced.
4. Run the existing release governance tests, preflight/package inventory, and
   package/archive checks appropriate before merge without publishing.
5. Inspect release workflow permissions, triggers, secret boundaries, command
   argument construction, and ordering.
6. Confirm normal CI remains server-free and release validation runs both
   project-owned real-provider runners before pack/publish.
7. Confirm workflow YAML does not duplicate credentials, server lifecycle,
   image selection, test filters, or cleanup policy.
8. Confirm release actions are impossible in the pull-request workflow.

Do not perform the post-merge publication dry run in this goal. That remains a
separate action on the final `main` commit.

## Phase 6: Documentation And Migration Review

Review README, canonical docs, component/package readmes, samples, migration
guides, changelog, goal index, and memory for:

- accurate current package and namespace names;
- runnable registration and DSL examples;
- no examples using removed compatibility surfaces;
- clear distinction between in-process execution and optional durability;
- accurate at-least-once, idempotency, replay, retention, lease, telemetry, and
  provider limitations;
- explicit breaking-change guidance from the previous public surface;
- correct ordinary-versus-release validation commands; and
- no duplicated or contradictory source of truth.

Update public documentation only for a proven reader-facing defect. Record
review-only evidence in goal/memory rather than manufacturing public-doc churn.

## Phase 7: Performance, Dependency, And Code-quality Review

Use targeted searches and representative hot-path inspection to identify:

- sync-over-async (`.Result`, `.Wait()`, blocking waits) in production paths;
- unbounded channels/dataflow blocks/collections or runaway task creation;
- per-message service-provider construction, reflection, JSON metadata
  reflection, repeated schema work, or avoidable hot-path parsing;
- repeated payload copying/materialization and unnecessary large allocations;
- locks held across asynchronous I/O or callbacks;
- swallowed exceptions, broad catch-and-ignore behavior, orphaned tasks, or
  cancellation-token loss;
- static mutable cross-host state;
- dependency cycles, redundant project/package references, version divergence,
  and known vulnerable direct/transitive packages; and
- abstractions with no concrete consumer or duplicate implementation.

Only optimize a path when the cost and correction are concrete. Do not add a
cache, pool, benchmark project, or abstraction speculatively.

## Phase 8: Test And CI Quality Review

Inspect representative unit, conformance, integration, release-governance, and
sample tests for:

- exact behavioral and side-effect assertions;
- meaningful failure, cancellation, concurrency, expiry, stale-token, conflict,
  migration, and cleanup cases;
- cross-platform path/process handling;
- deterministic clocks/signals rather than sleeps/polling;
- no skipped real-provider suite disguised as success;
- test-owned process/container/temp/database cleanup;
- no global parallelization workaround hiding an ownership defect; and
- no assertion-free, self-referential, implementation-spelling-only, or
  excessively duplicated tests.

Use the existing VSTest/xUnit/Shouldly conventions and installed SDK. If a
review finding requires new regression tests, invoke the repository's mandatory
test-generation workflow before editing test files.

## Remediation Rules

For each P0-P2 finding selected for correction:

1. state the root cause and reachable failure mode;
2. identify the smallest cohesive owner;
3. add or strengthen regression evidence when behavior changes;
4. update documentation/memory only where the contract or evidence changes;
5. run the narrow affected build/tests/format gate;
6. run broader gates once after all fixes; and
7. commit with a neutral concise subject.

Do not bundle unrelated findings into an architectural rewrite. If no P0-P2
finding survives verification, do not change production or test code merely to
create a review commit.

## Verification Plan

Always run on the final local head:

- `git diff --check origin/main...HEAD` and final working-tree whitespace check;
- serialized Release build with `ContinuousIntegrationBuild=true` when source,
  project, or build configuration changes;
- complete Release-governance project;
- affected focused tests for every correction;
- complete Release solution suite when production behavior changes;
- solution/project format verification for every touched C# project;
- configured direct/transitive vulnerability inspection;
- package/public-API/release-preflight gates affected by the review; and
- documentation-boundary tests for goal, memory, or public-doc changes.

Run a real T-SQL integration suite only when its production provider or shared
durability semantics change. Otherwise retain the already successful clean
real-provider evidence and avoid an unrelated long-running server cycle.

After committing and pushing:

1. verify local head equals remote pull-request head;
2. follow ordinary remote CI to a successful conclusion;
3. verify no unresolved review request or failed external check remains;
4. verify merge state is clean; and
5. convert pull request 65 from draft to ready for review.

## Goal And Memory Evidence

Create `memory/290-pr-65-final-review.md` containing:

- baseline branch/PR/commit/check state;
- review method and exact areas inspected;
- severity-ranked findings, including an explicit zero count where applicable;
- fixes and regression evidence;
- commands/results and reused prior real-provider evidence;
- commit and remote-CI evidence;
- final ready/merge state; and
- deliberate deferrals: merge, post-merge dry run, tag, release, publication.

Update `memory/00-index.md`, `memory/01-current-state.md`,
`memory/04-architecture-decisions.md`, and `memory/07-progress-log.md` only with
concise durable facts. Update this goal to complete with exact outcomes.

## Commit Strategy

- If review finds code/test/document defects, commit each cohesive remediation
  or small related set with a neutral subject after its focused gates pass.
- Commit final goal/memory review evidence separately as
  `Record final release-candidate review`.
- Do not amend or rewrite earlier commits.
- Push normally; never force-push.

## Acceptance Criteria

The goal is complete only when:

- the entire pull-request diff and high-risk representative paths were reviewed;
- repository, ancestry, and remote-head integrity are clean;
- no unresolved P0, P1, or bounded P2 finding remains;
- no accidental artifact, credential, conflict marker, or unrelated file is in
  the candidate;
- canonical runtime, registration, DSL, and component ownership remain simple
  and explicit;
- optional durability guarantees and lifecycle semantics remain correct and
  honestly documented;
- packages, APIs, versions, migration guidance, and workflows are consistent;
- affected and broad local validation passes with no warnings;
- final commits are pushed without rewriting history;
- ordinary remote CI succeeds on the final head;
- pull request 65 has a clean merge state and is ready rather than draft;
- goal and memory evidence are committed;
- local worktree is clean and synchronized; and
- nothing was merged, tagged, released, or published.

## Deliberately Deferred

- Merge into `main`.
- Post-merge release dry run from the actual merged commit.
- Tag creation, release creation, package publication, and feed verification.
- Any new product feature, provider, compatibility shim, or aesthetic cleanup.

## Completion Evidence

- Reviewed the complete candidate and all named high-risk boundaries. Found
  zero P0 findings, corrected two P1 findings, corrected one P2 hygiene
  finding, and left no unresolved P0-P2 item.
- Preserved the public API baseline, package versions, schemas, provider SQL,
  dependency graph, ordinary/release workflow split, canonical JSON/DSL, and
  optional durability boundary.
- Focused Release tests passed 101/101 for Engine and 155/155 for durable input.
  Release governance passed 127/127. The CI-style Release build covered 134
  targets with zero warnings/errors, and the complete Release suite passed
  2,495/2,495 across 66 projects.
- Touched-project formatting, complete whitespace, package inventory, public
  API, documentation boundary, conflict/artifact/credential searches, and
  direct/transitive vulnerability checks passed.
- Remediation commit `11ff9e00216ab5b2d0c32091d35b7f23babeaa8d`
  passed ordinary remote CI run `30753161344`.
- Final goal/memory evidence is committed separately and must pass ordinary CI
  before pull request 65 is changed from draft to ready. The final check and
  ready/clean state are retained in pull-request metadata rather than creating
  a recursive evidence-only commit.
- Nothing was merged, tagged, released, published, or dispatched.
