# Goal: Automate External Package-Consumer Acceptance

## Status

- State: complete
- Date: 2026-08-04
- Repository: `C:\Projects\FluxFlow`
- Accepted base branch: `main`
- Accepted base commit: `d9432c81d2102fedaea2ef57ede85e89aac7020e`
- Working branch: `work/package-consumer-acceptance`
- Runtime feature scope: none
- Publication scope: none

## Objective

Turn the representative package-only execution proof used during the canonical
release train into a small, deterministic, repeatable acceptance gate. The gate
must prove that a real external consumer can restore only package artifacts,
compile against their public APIs, and execute the three most important
consumer paths:

1. strict canonical JSON through normal dependency injection and the Engine
   lifecycle;
2. the type-safe Fluent DSL; and
3. real SQL-file durable-input and durable-output persistence across container
   disposal and store reopen.

This is a release-confidence and packaging-boundary round. It must not change
runtime behavior, public APIs, packages, schemas, versions, or published state.
The existing per-package assembly-load smoke remains useful and must stay. The
new gate adds behavior proof for a deliberately small representative package
closure instead of attempting bespoke behavior tests for all 59 packages.

## Current Evidence And Gap

- `eng/package-consumer-smoke.ps1` already restores one archive into an
  isolated cache, builds a temporary consumer, loads the package assembly, and
  enumerates its types.
- The coordinated release train separately proved that a public-feed-only
  executable could resolve Engine, run a Fluent graph, and perform SQL-file
  durable-input/output operations.
- That executable was intentionally temporary and removed after the release,
  so the behavioral proof is not currently a checked-in repeatable gate.
- The normal release workflow now validates package binary compatibility,
  archive integrity, assembly loading, feed visibility, and release ordering.
  It does not execute representative public APIs from the complete candidate
  package closure.
- The complete package rehearsal is documented as an explicit manifest-wide
  process but has no final behavioral consumer command.

## Architecture Decision

Add one checked-in external consumer fixture and one direct PowerShell runner:

- `eng/package-consumer-acceptance/FluxFlow.PackageConsumerAcceptance.csproj`
- `eng/package-consumer-acceptance/Program.cs`
- `eng/package-consumer-acceptance.ps1`

The fixture is not added to `FluxFlow.sln`. It is an external-consumer artifact,
contains package references only, and is copied to a fresh temporary directory
before restore/build/run so repository-wide build properties, project outputs,
and source references cannot become hidden inputs.

The runner owns orchestration, validation, and cleanup. It uses an explicit
nine-alias package closure rather than reflection, project-graph discovery, or
a generalized dependency framework:

1. `nodes`;
2. `mapping`;
3. `composition`;
4. `engine`;
5. `fluent`;
6. `engine-durable-input`;
7. `engine-durable-input-sqlfile`;
8. `engine-durable-output`; and
9. `engine-durable-output-sqlfile`.

Those aliases are the complete maintained FluxFlow closure required by the
three acceptance scenarios. The manifest remains authoritative for package id,
project path, and project version.

## Implementation Plan

### 1. Checked-in external consumer

Create a small `net8.0` executable that:

1. references only the four top-level candidate packages needed by source code:
   - `FluxFlow.Engine`;
   - `FluxFlow.Fluent`;
   - `FluxFlow.Engine.DurableInput.SqlFile`; and
   - `FluxFlow.Engine.DurableOutput.SqlFile`;
2. obtains their versions from explicit MSBuild properties supplied by the
   runner, avoiding copied hard-coded release versions;
3. contains no `ProjectReference`, repository path, generated source, dynamic
   assembly loading, reflection, or test-framework dependency;
4. uses only public package APIs;
5. treats every missing/incorrect outcome as an exception and non-zero process
   result; and
6. emits one exact success marker per scenario plus one final marker.

### 2. Canonical Engine scenario

The consumer must:

1. deserialize a literal strict canonical document with exactly `Resources`
   and `Workflows` through `ApplicationDefinitionJson`;
2. register the definition through normal `ServiceCollection` and
   `AddFluxFlow(...)` with explicit lifecycle control;
3. explicitly register one small consumer-owned uppercase component without
   scanning or reflection;
4. start `FluxFlowApplication` and require an applied result;
5. install the output receive before sending input through stable canonical
   ports;
6. send one typed message and require the exact transformed value;
7. stop and dispose the application/provider cleanly; and
8. emit `PACKAGE_ACCEPTANCE_ENGINE_OK=True` only after all assertions pass.

### 3. Fluent DSL scenario

The consumer must:

1. construct a small source-transform-sink graph with the public Fluent API;
2. use consumer-owned typed nodes and normal `FlowMessage<T>` values;
3. start the graph, await bounded completion, and dispose it;
4. require the exact transformed sink result; and
5. emit `PACKAGE_ACCEPTANCE_FLUENT_OK=True` only after success.

### 4. SQL-file durability scenario

The consumer must:

1. create a unique temporary data directory;
2. register SQL-file durable input and output stores through their flat public
   DI callbacks using explicit allowed absolute paths;
3. resolve provider-neutral input/output store interfaces;
4. enqueue complete deterministic input and output envelopes and require the
   exact `Enqueued` results;
5. dispose the first provider so no live connection or provider instance can
   satisfy the next assertions;
6. construct a new provider over the same files;
7. prove both records persisted by requiring equivalent duplicate enqueue to
   return `AlreadyExists` and by leasing each stored envelope through the
   provider-neutral input/delivery interfaces;
8. compare exact identities, contracts, and JSON payloads;
9. dispose the reopened provider and remove the temporary data directory; and
10. emit `PACKAGE_ACCEPTANCE_DURABILITY_OK=True` only after cleanup-safe success.

The scenario verifies local persistence and public provider wiring. It does not
start dispatch workers, contact a network server, or claim exactly-once
delivery.

### 5. Acceptance runner

`eng/package-consumer-acceptance.ps1` must:

1. validate framework, configuration, manifest, fixture, source, and work-path
   inputs before mutation;
2. reject a fixture containing `ProjectReference` before restore;
3. resolve the nine explicit aliases from `eng/packages.json` and their exact
   versions from the declared project files;
4. support two explicit modes:
   - caller-supplied `-PackageSource` containing an already completed package
     rehearsal; and
   - `-PackPackages`, which packs the nine declared projects from an already
     completed controlled build into a runner-owned temporary source;
5. reject ambiguous use of both an absent source and no packing request;
6. require one exact candidate `.nupkg` for every declared closure package;
7. copy the checked-in fixture into a fresh isolated work directory;
8. restore with `--no-cache`, a work-directory-local `--packages` root, the
   candidate source first, and the public source only for external packages;
9. pass the four exact top-level versions as MSBuild properties;
10. inspect the restored `project.assets.json` and, for every resolved
    `FluxFlow.*` package, require a matching candidate archive and an exact
    SHA-256 match with the archive stored in the isolated package root;
11. build with `--no-restore`, run with `--no-build`, and require the three
    scenario markers plus `PACKAGE_ACCEPTANCE_OK=True` exactly once;
12. print stable preparation, resolved-version, source/cache, command, package
    verification, and success markers suitable for tests and CI evidence;
13. provide `-PrepareOnly` for deterministic structural verification without
    packing or process execution;
14. delete only directories it owns in `finally`;
15. retain an explicitly caller-provided work directory for diagnostics and
    never delete a caller-owned package source; and
16. add no dependency, reflection, assembly scan, network inference, or broad
    reusable framework.

### 6. Automation and rehearsal integration

1. Add one normal CI step after the complete solution tests.
2. Invoke the runner with `-PackPackages`; reuse the existing controlled Release
   build and never build runtime projects through project references in the
   consumer.
3. Keep the existing solution restore/build/test steps and their order.
4. Keep both real network-provider suites release-only; the new gate uses only
   local SQL-file providers and requires no credentials, container, port, or
   external server.
5. Update the complete package rehearsal guide to invoke the acceptance runner
   once after every candidate archive and per-package dry run succeeds.
6. Do not add the manifest-wide consumer to each single-package publication
   workflow because one package workflow does not contain the complete
   candidate closure.

### 7. Focused verification

Add release tests proving:

- the checked-in fixture exists, targets `net8.0`, contains the four expected
  package references, uses version properties, and contains no project
  references;
- source contains the exact Engine, Fluent, durability, and final markers;
- the runner has the exact nine-alias closure and no manifest-wide implicit
  discovery;
- `-PrepareOnly` resolves exact manifest/project versions without creating
  package or work directories;
- missing candidate archives fail before restore/run;
- a fixture with a project reference fails before restore/run;
- restore uses `--no-cache`, one isolated `--packages` root, candidate source
  first, and explicit version properties;
- candidate hash verification covers every restored `FluxFlow.*` package and
  fails on missing/mismatched archives;
- build and run are each invoked once and require exact success markers;
- owned temporary directories are removed on success and failure;
- a caller-owned work directory and package source remain; and
- CI invokes the runner after solution tests with `-PackPackages` exactly once.

Use the existing xUnit/Shouldly release-test conventions. Fake process commands
must remain deterministic and network-free. Run a real package-only acceptance
execution separately as final proof.

### 8. Documentation and memory

Update:

- `docs/38-release-validation.md` with the two package checks, the one-command
  behavioral gate, its candidate-byte verification, and rehearsal ordering;
- `docs/README.md` only if a new documentation page is added (none is planned);
- `memory/00-index.md`;
- `memory/01-current-state.md`;
- one new numbered memory record; and
- this goal with exact completion evidence.

No package README or changelog entry is required because consumer-facing
runtime behavior and package versions do not change.

## Non-Negotiable Principles

1. KISS, SRP, explicit data flow, and a small package-validation boundary.
2. No reflection, assembly scanning, service locator, hidden project fallback,
   global package cache, generated application code, or generalized framework.
3. No runtime or public API change.
4. No package, schema, dependency, target-framework, or version change.
5. No package publication, tag, or release operation.
6. No claim that four scenarios behaviorally cover all 59 packages.
7. Keep the existing per-package restore/load smoke test.
8. Candidate bytes must be proven, not inferred from source ordering.
9. All work and data directories are bounded and have explicit ownership.
10. Preserve unrelated work and avoid broad cleanup.
11. Update goal, documentation, documentation-site content, and memory.
12. Use the normal branch, commit, pull-request, checks, review, and merge path.

## Explicit Non-Goals

- no workflow-engine feature or component;
- no transport, broker, HTTP, AI, IoT, or network-server scenario;
- no T-SQL provider execution;
- no behavioral scenario for every package;
- no replacement of unit, integration, conformance, archive, API, or binary
  compatibility tests;
- no package-source mapping framework or dependency graph engine;
- no release workflow publication change;
- no new health/readiness API;
- no unrelated refactoring.

## Validation Sequence

1. Run focused release tests during implementation.
2. Run structural `-PrepareOnly` proof.
3. Run the runner against a real isolated set of candidate archives produced
   from the controlled Release build.
4. Require all four exact consumer markers.
5. Run the complete `FluxFlow.Release.Tests` project.
6. Run the complete solution test suite in Release configuration.
7. Run the complete solution Release build with continuous-integration flags
   and zero warnings.
8. Run formatting, vulnerable-package, diff/whitespace, scope, and neutral-name
   checks.
9. Confirm no runtime source, public API baseline, package version, changelog,
   tag, release, or public package state changed.
10. Remove runner-owned candidate, consumer, cache, build, and SQL-file
    artifacts and confirm no residue remains.

## Review And Closeout

1. Commit only goal-owned files with a neutral subject.
2. Push the branch and open a ready pull request against `main`.
3. Require successful remote checks on the exact head, including the new real
   package-consumer acceptance step.
4. Resolve every actionable finding without bypassing policy.
5. Merge normally using the repository's established strategy.
6. Synchronize local `main` with `origin/main` and require a clean worktree.
7. Mark this goal complete only after implementation, validation,
   documentation, memory, review, merge, cleanup, and synchronization finish.

## Acceptance Criteria

The goal is complete only when:

- one checked-in package-only consumer executes canonical Engine, Fluent, and
  SQL-file persistence/reopen scenarios;
- its project contains package references only;
- the consumer runs from a fresh external work directory and isolated package
  cache;
- every restored FluxFlow package exactly matches a candidate archive;
- missing or mismatched candidates, project references, scenario failures, and
  missing markers fail closed;
- CI invokes the gate automatically from an already completed Release build;
- the complete rehearsal guide invokes the same gate once over its full
  candidate source;
- the existing per-package assembly smoke remains intact;
- focused and full validation pass with exact evidence;
- runtime source, APIs, package versions, dependencies, schemas, and public
  state remain unchanged;
- goal, documentation, documentation site, and memory are updated; and
- local `main` is clean and synchronized after normal review and merge.

## Completion Evidence

### Implementation

- Added one checked-in `net8.0` package-only console consumer with the four
  explicit top-level package references and no `ProjectReference`.
- Added one explicit runner over the maintained nine-package closure. Restore
  uses a cleared, work-local source configuration and isolated package cache;
  every resolved FluxFlow archive is checked against the candidate archive by
  SHA-256 before build or execution.
- Added canonical JSON/DI Engine, typed Fluent, and SQL-file durability
  dispose/reopen scenarios with four exact success markers.
- Added the normal CI gate, complete-rehearsal documentation, 12 focused
  release contracts, and memory updates. The existing per-package smoke is
  unchanged.

### Local validation

- Windows PowerShell real execution: nine candidates packed and verified;
  Engine, Fluent, durability, final, and completion markers passed.
- PowerShell 7 real execution: the same nine candidates and all markers passed.
- Focused `PackageConsumerAcceptanceScriptTests`: 12 passed, 0 warnings.
- Complete `FluxFlow.Release.Tests`: 163 passed, 0 warnings.
- Controlled Release build: 134 projects, 0 errors, 0 warnings.
- Complete Release solution tests: 2,531 passed across 66 projects, 0 warnings.
- Solution formatting and standalone-consumer whitespace checks passed.
- Full transitive vulnerable-package audit reported no vulnerable packages.
- Test/source pairing audit inspected 766 source files and 318 test files in
  2,868 ms; it was retained as static routing evidence rather than a coverage
  claim.
- Runner-owned real candidate and consumer directories were removed after
  execution. No package was published and no release state was changed.

### Review and merge

- Pull request 75 completed normal review with no review submissions or
  unresolved threads.
- The first remote run correctly exposed one test-only portability assumption:
  styled Linux error output wrapped the asserted phrase across lines. The
  production runner still rejected the altered archive. The focused contract
  was narrowed to the stable error fragment and all 12 focused tests passed
  again.
- Exact-head CI run `30925479707` passed on commit
  `6372051316a7eda3617999ece00c248b187cc8ee`. Restore, build, all solution
  tests, and the new Linux package-consumer acceptance step completed
  successfully.
- Pull request 75 merged normally at 2026-08-04 15:47:20 UTC as
  `014840dd6c35a6f3e74d8bc104ca78ceb7b62d74`.
- Local `main` was synchronized to the same commit with a clean worktree.
