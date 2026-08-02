# Pull Request 65 Final Review

Date: 2026-08-02

## Outcome

Pull request 65 received a complete final release-candidate review against
`origin/main`. The review found and fixed two P1 defects and one P2 repository
hygiene defect. No P0 finding was found, and no unresolved P0-P2 finding
remains. The canonical runtime, flat registration and authoring APIs, optional
durability boundary, package surface, migration path, and ordinary-versus-
release validation split remain intact.

The review intentionally made no public API, schema, package-version,
dependency, persistence-provider, workflow-feature, or release-publication
change. Merge, post-merge dry run, tag creation, release creation, and package
publication remain separate decisions.

## Baseline And Integrity

- Branch: `work/major-surface-reset`.
- Base: `origin/main`; the branch began zero commits behind and 126 commits
  ahead, with no merge commits in the pull-request ancestry.
- Baseline reviewed head: `9c74edb5d50b0887aea8e80381441dde0f84cf39`.
- Hosted pull-request baseline: draft, clean merge state, successful ordinary
  CI, 1,708 changed files, and no review threads or submitted reviews.
- Conflict-marker, suspicious filename, generated-artifact, credential,
  external-branding, and skipped-test searches found no release blocker.
- The complete diff was checked against the saved canonical cleanup ledger,
  package manifest, public API baseline, migration guides, goals, and memory.

## Review Method

The review inspected representative high-risk code and the governing tests for:

- Engine startup, reload, apply, activation, rollback, drain, stop, disposal,
  stable ports, and revision-event publication;
- Composition registration, descriptor conflict detection, immutable catalogs,
  flat component metadata registration, C# authoring handles, same-builder
  ownership, cardinality, and cross-workflow links;
- durable input/output enqueue, lease, exact-token settlement, renewal, retry,
  dead letter, replay, retention, cancellation, store ownership, SQL-file
  transactions, and network-provider boundaries;
- package inventory, versions, public source declarations, project references,
  release scripts, workflow permissions/order, ordinary CI isolation, and
  release-only real-provider validation;
- canonical docs, package readmes, samples, obsolete-surface migration, and
  durability guarantees and limitations; and
- production static dependencies, sync-over-async candidates, timers,
  serializer reuse, collection/allocation patterns, dependency vulnerability,
  test determinism, assertion quality, cleanup, and skip state.

The performance/static-dependency scan covered 698 production C# files. It
found no `async void`, per-call `HttpClient`, culture-sensitive comparison
normalization, mutable static dictionary, or proven sync-over-async path. The
four `.Result`/`.Wait` text matches were nonblocking semaphore acquisition, an
already-completed task guarded by `IsCompletedSuccessfully`, or naming false
positives. All 13 production delays use an injected clock and cancellation.
Filesystem statics remain inside explicit filesystem/provider owners, and the
two custom serializer-option instances are cached statically. No speculative
wrapper, cache, pool, benchmark project, or abstraction was added.

## Findings And Corrections

### P0

Count: zero.

No data-loss, security-exposure, corrupt-release, credential, or generally
unsafe runtime behavior was found.

### P1-1: exceptional updates could leave a transient public state

`FluxFlowApplication` entered `Starting` or `Reloading` before loading or
applying a revision. Cancellation or a caller-visible exception after that
assignment could escape without restoring the prior stable state, leaving the
public state stuck even though no transition remained active.

The public methods now capture and restore the exact previous stable state on
exception while retaining the single lifecycle gate and existing lower-level
rollback behavior. Regression tests cover initial cancellation back to
`Empty`, retry cancellation back to `Degraded`, active reload cancellation
back to `Running` with the same revision objects, and duplicate-revision apply
failure with the current revision preserved.

### P1-2: multiple durable-input stores were selected implicitly

The durable-input client used single-service resolution, which silently chose
one registration when multiple `IDurableInputStore` services existed. That
contradicted the documented one-store ownership model and could make enqueue
and dispatch resolve different ambiguous registrations in a custom host.

Normal hosted registration now uses one explicit factory that validates the
exact store count before constructing the dispatcher. The client uses the same
selector. Missing and ambiguous ownership fail deterministically; equivalent
registration remains idempotent; the existing dispatcher constructor and
public API baseline remain unchanged. Package and public docs now state the
exact-one-store rule. Regression coverage resolves both the client and the
registered hosted dispatcher with duplicate stores and verifies the same
diagnostic.

### P2-1: complete-diff whitespace gate was not clean

Two historical memory files contained an extra terminal blank line, causing
the complete `origin/main...HEAD` whitespace check to report findings. Only
those terminal blanks were removed. No historical meaning changed.

### P3

Accepted count: zero.

Observational clock calls, provider-owned filesystem calls, small registration
collections, workflow action pinning style, and other scan candidates did not
demonstrate a current correctness or measured performance defect. They were
not converted into churn.

## Verification

- Focused Engine Release tests: 101/101 passed, zero warnings.
- Focused durable-input Release tests: 155/155 passed, zero warnings.
- Release-governance tests: 127/127 passed, zero warnings, including package
  inventory, public API baseline, documentation boundaries, cleanup ledger,
  workflow, archive, and release-script contracts.
- Serialized CI-style Release build with
  `ContinuousIntegrationBuild=true`: 134 project targets, zero errors and zero
  warnings.
- Complete Release solution: 2,495/2,495 tests passed across 66 projects, zero
  warnings.
- Touched-project formatting and file-scoped test formatting passed. A
  solution-wide formatter invocation exceeded the bounded local command window
  without reporting a defect; every changed C# file was then verified through
  its owning project.
- Direct and transitive vulnerability audit: no known vulnerable package under
  the configured sources.
- Package inventory: 59 maintained packages; public API baseline unchanged.
- Whitespace checks passed after the bounded two-file cleanup.
- Existing clean-checkout real-provider evidence remains applicable because
  this review changed neither provider SQL nor shared persistence semantics:
  durable input 89/89 and durable output 117/117 passed with zero skips, and
  both owned test environments were cleaned.

## Commits And Remote Evidence

- `11ff9e00216ab5b2d0c32091d35b7f23babeaa8d` — bounded lifecycle,
  durable-input ownership, regression, documentation, and whitespace fixes.
- Ordinary remote CI run `30753161344` passed checkout, restore, build, and test
  for that exact remediation head.
- `Record final release-candidate review` contains this completed goal and
  memory evidence. Its content-derived hash is obtained from repository history
  after commit and therefore cannot be embedded in itself.
- The final evidence-only head is independently required to pass ordinary CI
  before the pull request is converted from draft to ready. That final remote
  result and ready/clean state live in pull-request metadata to avoid an
  impossible recursive evidence commit.

## Final Boundary

After the final evidence-only CI gate, pull request 65 is ready for formal
review with a clean merge state and no unresolved review request. It is not
merged. No post-merge dry run, tag, release, package publication, or feed
verification belongs to this goal.
