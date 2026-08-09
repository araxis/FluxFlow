# Goal: Consolidate FluxFlow into a clean release candidate and prove both consumer paths

- Date: 2026-08-08
- State: complete
- Scope: accumulated code-first, JSON, resource, durability, health, performance, package, sample, documentation, and release-validation changes
- Compatibility: freeze the intended public API and portable JSON format; remove only obsolete or contradictory material already superseded by the accepted design
- Publication: no push, pull request, tag, release, or package publication without a separate explicit user request

## Objective

Turn the accumulated FluxFlow work into one coherent, distributable, and
independently consumable release candidate.

This is a consolidation and proof round, not a feature-development round. The
repository already contains the intended core capabilities:

- typed, flat C# application authoring;
- portable JSON application definitions and hot reload;
- complete component contracts and typed port handles;
- executable code-first application resource contracts;
- canonical Engine execution for both code-first and Fluent applications;
- explicit advanced dynamic registration;
- optional durable input and durable output packages;
- optional application readiness integration;
- deterministic lifecycle, revision, rollback, drain, and ownership behavior;
- a permanent Engine benchmark baseline and focused concurrency hardening.

The purpose of this goal is to make those capabilities read, build, package,
install, run, and operate as one trustworthy product surface.

The decisive outcome is:

> A new developer can restore only locally packed FluxFlow packages and build
> and run either a typed C# application or a JSON-defined application without
> project references, internal repository knowledge, duplicate code-first
> registration, reflection-driven discovery, or undocumented setup.

## Architectural principles

- **KISS:** prefer one obvious normal path for each authoring mode and keep
  advanced escape hatches visually separate.
- **SRP:** authoring builds definitions, contracts carry executable code-first
  behavior, Engine owns execution and revisions, optional packages own their
  integrations, and the host owns external process configuration.
- **IOC/DIP:** factories receive explicit contexts and ordinary dependency
  injection. Do not add service locators, assembly scanning, static mutable
  registries, or hidden global state.
- **No magic:** no reflection dispatch, convention-only activation, generated
  runtime IDs, delegate hashing, or inferred ownership.
- **Flat normal API:** normal C# code-first composition should remain at one
  fluent level, with at most one component- or resource-specific options
  callback.
- **Independent authoring paths:** C# and JSON are two inputs to the same
  Engine. The C# builder is not constrained by JSON serialization. JSON remains
  portable and does not embed executable delegates or runtime objects.
- **Exact ownership:** host-owned services are never disposed by a revision;
  revision-owned components and resources are disposed exactly once.
- **Evidence before change:** do not alter runtime behavior, allocation, or
  synchronization without a reproduced defect or focused measurement.
- **Small dependency graph:** prefer the BCL and the existing package set. Do
  not add a new runtime dependency in this round.

## Frozen supported product paths

### 1. Typed C# code-first path

The normal compiled-C# path is:

1. select package-owned `ComponentContract` and
   `ApplicationResourceContract` declarations;
2. build an `ApplicationDefinition` with `ApplicationDefinitionBuilder` or the
   canonical `FluxFlow.Fluent` facade;
3. capture typed component/resource/port handles during authoring;
4. connect typed ports with `ConnectTo` or the workflow/application `Connect`
   methods, including C# predicates where required;
5. call `services.AddFluxFlow(definition)` once;
6. start or host the resulting `FluxFlowApplication`;
7. use the same typed handles at the runtime and durability boundaries.

The definition owns the exact executable component/resource contracts used by
this path. A code-first consumer must not repeat ordinary component or resource
registration solely to make the authored definition executable.

### 2. Portable JSON path

The normal configuration path is:

1. deserialize a portable `ApplicationDefinition` from JSON;
2. explicitly register the package component/resource behavior referenced by
   portable type names;
3. call `services.AddFluxFlow(definition)`;
4. start, host, or reload the same `FluxFlowApplication` runtime.

JSON must remain free of delegates, CLR types, service providers, contract
instances, runtime resource objects, and code-first predicate identities.
Portable JSON loading, validation, persistence, and hot reload must continue to
work without requiring the C# builder.

### 3. Advanced dynamic path

Dynamic registration remains available only through the explicit advanced
surface. It must not compete with complete `ComponentContract` authoring in
normal samples or getting-started documentation.

## Current accepted baseline

The immediately preceding hardening round established:

- a non-packable `FluxFlow.Engine.Benchmarks` project with eight real Engine
  scenarios;
- equivalent typed and addressed request allocation in the recorded baseline;
- an approximately 18.7 percent allocation reduction in the eight-hop typed
  pipeline;
- deterministic concurrent request, compatible revision, rejected revision,
  graceful drain, and disposal tests;
- complete Engine tests: 134 passed, zero warnings;
- complete Release tests: 189 passed, zero warnings;
- complete solution tests: 2,673 passed across 67 test projects, zero warnings;
- solution build: 137 project targets, zero errors, zero warnings;
- a clean dependency vulnerability and formatting audit.

These are the starting facts. This goal must preserve them and add clean,
packed-package, release-candidate evidence rather than reinterpret them.

## Non-goals

Do not add or redesign:

- workflow semantics, scheduling, or delivery guarantees;
- component package families;
- application-definition fields or JSON schema;
- public configuration callbacks or options merely for future flexibility;
- another registration abstraction;
- another runtime, Fluent execution model, or host lifecycle model;
- another health, metrics, or telemetry framework;
- persistence engines, ORMs, provider abstractions, or generic repositories;
- background polling, caching, pooling, retries, or unbounded workers;
- reflection, assembly scanning, dynamic proxying, source generation, or
  convention-based activation;
- speculative optimization of link compilation or message routing;
- the synchronous rejection-to-system-event backpressure behavior without
  focused contention evidence;
- package versions, tags, releases, or publication.

Do not retain obsolete aliases merely for backward compatibility when the
accepted breaking-change goals already replaced them. Conversely, do not
remove a supported public or JSON capability merely to reduce line count.

## Phase 1: Preserve and inventory the accumulated work

Before editing implementation:

1. record the exact branch, head, and working-tree inventory;
2. distinguish tracked modifications, tracked deletions, and untracked files;
3. verify that untracked files are intended source, test, benchmark, goal,
   documentation, or memory artifacts rather than build output, results,
   databases, credentials, secrets, caches, editor state, or logs;
4. inspect repository-level instructions, central package management, solution
   membership, package manifest, public API baseline, CI, publication workflow,
   package acceptance runner, and real-provider runners;
5. query the existing repository knowledge graph before doing manual
   relationship discovery;
6. preserve all unrelated user changes and never use destructive reset or
   checkout operations;
7. create a neutral work branch before recording commits so the accumulated
   work is not committed directly on `main`.

## Phase 2: Consolidate the product surface

Perform a bounded review of the files changed by the accepted recent goals.

### Public API and registration

- Confirm the accepted public API baseline matches the intended typed
  code-first, JSON, health, durability, Fluent, and advanced registration
  surfaces.
- Confirm normal documentation and samples use `ComponentContract` and
  `ApplicationResourceContract`, not retired authoring-contract names.
- Confirm code-first definitions execute embedded component/resource contracts
  without duplicate ordinary service registration.
- Confirm JSON applications still resolve executable package registrations by
  portable type name.
- Confirm raw runtime component registration is available only from the
  explicit advanced surface.
- Confirm typed handle overloads delegate to the same address-based runtime and
  durability implementation rather than creating parallel logic.
- Confirm component events are explicit named output ports and that declaration
  terminology remains `HasInput`, `HasSignalInput`, `HasOutput`, and
  `HasEvents`.
- Remove only declarations, aliases, samples, or documentation that contradict
  this frozen surface.

### Application definition and JSON

- Confirm portable definition equality, revision planning, and JSON round trips
  exclude executable contract collections and C# predicates.
- Confirm C#-authored definitions retain exact executable contract identity for
  revision decisions and lifetime ownership.
- Confirm first-class links preserve local, cross-workflow, fan-out, signal,
  unconditional, portable condition, and C# predicate behavior.
- Confirm failed construction and failed revision preparation remain atomic and
  do not partially mutate or activate the application.
- Do not add C#-to-JSON export as a required code-first feature.

### Packages and dependency closure

- Reconcile `eng/packages.json`, central package versions, project references,
  package READMEs, changelog entries, and public API baseline identities.
- Every packable project must have intentional target frameworks, package
  metadata, repository-local dependencies, and package documentation.
- The benchmark project and every test/sample project must remain non-packable.
- The package-only consumer must have no `ProjectReference` and must restore
  FluxFlow packages only from the isolated candidate source.
- Microsoft/framework dependencies may come from the explicitly configured
  public dependency source; FluxFlow candidates must resolve to exact locally
  packed bytes.
- No package may gain an accidental dependency on Designer, MQTT, durability,
  health checks, a database provider, or a benchmark package.

### Samples and documentation

- Keep one compact canonical C# sample that demonstrates application/resource
  construction, typed handles, typed connections, registration, start, message
  exchange, and stop.
- Keep one compact JSON sample that demonstrates portable loading and the
  explicit package behavior registration needed by JSON.
- Keep Fluent as a concise facade over the canonical definition/application
  runtime, not a second runtime.
- Ensure README and docs distinguish code-first embedded contracts from JSON
  explicit registration in the first relevant section.
- Ensure advanced dynamic registration is documented separately and not shown
  as the normal path.
- Ensure health, durability, observability, delivery guarantees, and operational
  limits are stated honestly and consistently.
- Remove stale snippets, names, duplicate setup, and references to deleted
  types; do not expand prose merely to repeat existing documents.

## Phase 3: Strengthen release-candidate acceptance evidence

Reuse the existing package-consumer acceptance fixture and runner. Do not create
a competing harness.

The isolated package consumer must prove, from package references only:

### Typed C# scenario

- build a definition from complete component contracts;
- capture typed handles with the flat fluent API;
- use at least one definition-owned application resource contract;
- connect typed output and input handles;
- register the definition once with `AddFluxFlow` and no duplicate normal
  component/resource registrar call;
- start the real Engine;
- send and receive through typed runtime handles;
- prove the exact transformed value and metadata/trace behavior;
- stop and dispose cleanly;
- emit one exact code-first success marker.

### JSON scenario

- deserialize a portable definition from JSON;
- register the corresponding executable component behavior explicitly;
- start the same Engine runtime;
- send and receive through the portable/addressed boundary;
- prove the exact transformed value;
- prove a compatible JSON reload is applied or unchanged as intended;
- prove an invalid candidate is rejected while the active route remains usable;
- stop and dispose cleanly;
- emit one exact JSON success marker.

### Existing acceptance preservation

Preserve and continue proving:

- exact candidate package closure and archive hashes;
- health-check registration and safe readiness metadata;
- canonical Fluent execution;
- SQL-file durable input and output behavior;
- restart seed/recovery, abandoned lease recovery, pending output resumption,
  captured workflow output, and receipt idempotency;
- exact marker multiplicity;
- isolated restore/build/run;
- rejection of project or non-candidate resolution;
- deterministic owned temporary source/work-directory cleanup on success and
  failure.

Tests must assert the fixture source, package graph, runner behavior, failure
cleanup, and real pack-mode execution. Use xUnit and Shouldly. Avoid sleeps,
polling, retry-until-pass, elapsed-time performance assertions, and fragile
test-order dependence.

## Phase 4: Create a coherent local release-candidate history

After the working tree is audited and focused validation is green:

1. create or use a neutral branch named for release-candidate consolidation;
2. stage only audited intended files;
3. scan new names and user-facing text for neutral repository terminology;
4. prefer a small number of coherent commits over artificial file-type commits;
5. every implementation commit should represent an internally understandable
   state; do not manufacture a split that knowingly leaves an intermediate
   public surface incoherent;
6. keep the final validation/memory closeout separate when that produces a
   clearer audit trail;
7. do not rewrite, squash, push, open a pull request, tag, release, or publish.

An acceptable shape is:

1. one authoritative accumulated implementation commit for the already agreed
   code-first/JSON/durability/health/performance surface;
2. one release-candidate validation/governance correction commit if validation
   discovers test, script, package, sample, or documentation defects;
3. one final evidence record commit.

If a smaller logical split can be proven buildable without risky history
surgery, it may be used. Do not split merely to increase commit count.

## Phase 5: Clean committed-snapshot validation

Create a detached temporary worktree from the exact candidate commit. Resolve
its absolute path and keep every owned temporary path outside the main working
tree. Run all validation from that clean snapshot so untracked source or stale
build output cannot satisfy a gate accidentally.

Required clean-snapshot gates:

1. report the exact SDK selected by `global.json`;
2. restore the solution;
3. build the Release solution with zero warnings and zero errors;
4. run the complete solution test suite with zero failures, skips introduced by
   this round, or warnings;
5. run the complete Release governance test project;
6. execute the package-consumer acceptance script in real pack mode;
7. verify the isolated consumer contains no project references and resolves
   exact candidate package bytes;
8. run the canonical executable samples from the intended build output;
9. run public API baseline acceptance;
10. run solution-wide formatting verification;
11. run whole-worktree whitespace/diff verification;
12. audit direct and transitive packages for known vulnerabilities;
13. run the real durable-input T-SQL integration runner;
14. run the real durable-output T-SQL integration runner;
15. verify both runners execute all expected tests with zero skips and clean up
    owned containers/resources;
16. verify the clean worktree remains clean apart from ignored build output;
17. remove the temporary worktree through Git, prune it, and verify its exact
    path no longer exists.

External provider gates may stop only for a genuine environment/permission
blocker. They must not be silently skipped or replaced by SQL-file tests. Never
print or persist full connection strings, credentials, secrets, or container
environment values.

## Test-generation workflow

Use the repository testing workflow before writing any new tests:

1. record a bounded inventory and requirement checklist in
   `.testagent/research.md`;
2. run the static source/test pairing analyzer exactly once for this goal;
3. map every required new assertion to an exact planned test in
   `.testagent/plan.md`;
4. add tests only for material missing evidence;
5. compile and run the narrowest relevant project during correction cycles;
6. audit assertion strength and pseudo-mutation gaps;
7. record exact results and a `Requirement | Evidence` map in
   `.testagent/status.md`;
8. run broad validation once after focused lanes are green.

Tests must preserve the existing framework and assertion style. Do not add a
test framework, mocking library, coverage package, test-only production hook,
or InternalsVisibleTo solely for this round.

## Required audits

### Public surface

- accepted public API baseline is unchanged except for already approved recent
  additions;
- no obsolete authoring-contract or normal raw-registration alias remains;
- no accidental public helper, marker, runtime implementation, mutable
  collection, delegate identity, or reflection helper is exposed;
- XML/package documentation and README snippets compile against the public
  packages.

### Complexity and dependency

- no new runtime package dependency;
- no assembly scan, reflection dispatch, service locator, static mutable
  registry, unbounded channel/queue, polling worker, or hidden retry;
- code-first and JSON converge on `FluxFlowApplication` rather than separate
  runtime implementations;
- normal and advanced registration remain visibly separate;
- no provider details leak into core Composition or Engine;
- benchmark code remains outside shipped packages.

### Ownership and lifecycle

- revision-owned factories/resources/nodes are retired and disposed exactly
  once;
- host-owned dependencies remain non-owned by revision snapshots;
- rejected candidates leave the prior active route operational;
- reload and stop preserve deterministic drain/cancellation semantics;
- package-consumer processes and temporary directories are bounded and cleaned.

### Privacy and diagnostics

- package and health output contains only bounded operational identifiers;
- no payloads, connection strings, exception objects, secrets, or arbitrary
  diagnostic details are exposed through readiness or validation logs;
- marker output is exact, stable, and asserted once.

## Documentation and memory deliverables

Update only the authoritative documents needed to describe the final result:

- root `README.md` if the first-use distinction between C# and JSON is not
  already clear;
- `docs/01-getting-started.md` for the two supported entry paths;
- `docs/02-definitions-and-links.md` for portable versus code-first link rules;
- `docs/05-hosting-and-observability.md` for lifecycle/readiness boundaries;
- `docs/14-public-api-overview.md` for the final package/API surface;
- `docs/38-release-validation.md` for exact clean-candidate commands and
  package-consumer markers;
- `docs/README.md` index if a new closeout document is added;
- package-local READMEs and samples only when a stale or contradictory snippet
  is proven;
- `memory/00-index.md` and `memory/01-current-state.md`;
- new `memory/304-release-candidate-consolidation.md` containing decisions,
  changes, exact commands/results, commit hashes, clean-worktree path and
  cleanup, provider evidence, remaining risks, and no-publication statement.

Do not duplicate the full implementation history. Link to the accepted detailed
documents for typed authoring, unified contracts, end-to-end code-first
simplification, health readiness, and performance hardening.

## Acceptance criteria

The goal is complete only when all of the following are true:

1. The goal exists in this README and its state is `complete`.
2. The accumulated working tree has been audited; no unintended generated,
   credential, database, log, result, cache, or editor artifact is committed.
3. The intended changes are recorded on a neutral local branch in coherent
   commits.
4. The public API baseline and portable JSON shape are frozen and accepted.
5. Normal C# code-first use requires one definition registration and no
   duplicate ordinary component/resource registration.
6. Portable JSON use remains explicit, serializable, reloadable, and executable
   through package registration.
7. The isolated packed-package consumer proves both paths with exact outputs
   and marker multiplicity, plus all previously accepted health, Fluent,
   durability, restart, hash, and cleanup behavior.
8. No package-only consumer project reference or non-candidate FluxFlow package
   resolution is possible.
9. Complete Release build and tests are warning-free and green from the exact
   committed clean snapshot.
10. Public API, package manifest, archive, dependency, vulnerability,
    formatting, and whitespace gates are green.
11. Real durable-input and durable-output T-SQL runners execute with zero skips
    and clean up their owned infrastructure.
12. Canonical samples build and run through their intended paths.
13. Documentation and memory describe the final supported paths and exact
    evidence without contradictory setup.
14. No new workflow feature, runtime dependency, reflection, magic activation,
    worker, polling loop, cache, pool, or speculative public option was added.
15. No push, pull request, tag, release, package publication, or external
    product mutation occurred.

## Stop conditions

Stop and report rather than guessing when:

- the inventory includes files whose ownership cannot be determined;
- a requested clean commit would require discarding or overwriting user work;
- a public API or JSON change is required but was not already approved;
- package acceptance resolves a FluxFlow dependency outside the candidate
  source;
- a real-provider gate requires new credentials, license acceptance, or
  authority not already configured by the project runner;
- a release/publish action would be required;
- the same external blocker repeats and no safe local progress remains.

## Final report contract

The final handoff must state:

- the branch and exact candidate commit(s);
- material consolidation changes, if any;
- the final supported C# and JSON paths;
- package-consumer markers and exact pack/restore/build/run results;
- solution, Release, public API, formatting, vulnerability, sample, and
  real-provider results;
- cleanup status for worktrees, package sources, directories, processes, and
  provider infrastructure;
- remaining risks and deliberately deferred work;
- confirmation that nothing was pushed, tagged, released, or published;
- a compact `Requirement | Evidence` table citing exact tests, commands, or
  documents.

## Execution result

Completed on 2026-08-08 on branch
`work/release-candidate-consolidation`. The exact implementation candidate is
`4bf69015b9d3eaa95a45630c91d378c45c5a2aaa`.

The candidate was restored, built, tested, formatted, audited, packed, and run
from a detached clean worktree. Final results were:

- 137-project restore and CI-style Release build with 0 warnings and 0 errors;
- 2,675/2,675 solution tests across 67 test projects;
- 191/191 release-governance tests and 2/2 public API baseline tests;
- clean formatting, whitespace, and direct/transitive vulnerability gates;
- ten exact candidate packages and 15 package-consumer process invocations,
  with every required marker exactly once and owned-directory cleanup;
- real T-SQL durable input: 90/90 with zero skips;
- real T-SQL durable output: 117/117 with zero skips;
- provider digest
  `sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`;
- no remaining candidate worktree change or provider container;
- no push, pull request, tag, release, or package publication.

The supported paths and full evidence are recorded in
`docs/44-release-candidate-consolidation.md` and
`memory/304-release-candidate-consolidation.md`.
