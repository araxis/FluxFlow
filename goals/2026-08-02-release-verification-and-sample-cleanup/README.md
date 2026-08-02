# GOAL: Simplify release verification and the durability operations sample

## Status

- State: complete
- Date: 2026-08-02
- Repository: FluxFlow
- Scope: release-test process ownership and scheduling, durability operations
  sample-local telemetry, focused sample verification, documentation review,
  goal evidence, and memory
- Compatibility posture: no production or public behavior change; test and
  sample implementation cleanup only

## Role And Execution Instruction

Act as a senior .NET library maintainer. Treat the current dirty and untracked
workspace as authoritative and preserve all unrelated work. This complete goal
must exist on disk before changing the release tests, sample, documentation, or
memory for this round. Then execute it fully and update this file with factual
completion evidence.

Favor KISS, SRP, explicit ownership, direct framework APIs, and the smallest
cohesive change. Do not use reflection, assembly scanning, dynamic proxies,
hidden global state, polling, arbitrary sleeps, magic discovery, global test
parallelism switches, or a new dependency. Do not create a generic process
framework, telemetry framework, builder hierarchy, or broad abstraction merely
to reduce a few lines.

## Context And Evidence

The preceding durability operations sample round completed successfully and
proved the intended public behavior. It also exposed two maintainability issues
under aggregate verification load:

1. release tests that own child processes currently run in unrelated xUnit
   collections, so process-heavy sample and PowerShell tests can contend with
   the timeout/cancellation lifecycle tests in the same assembly;
2. the operations sample owns four separate telemetry completion signals even
   though the scenario needs one causal signal meaning that all required
   observations have arrived, and its source-shape fact checks many private
   implementation strings that are not part of the public behavior.

Initial full-suite runs under machine load observed intermittent timeouts in
the process-owner tests and one unrelated source timing test. The same tests
passed together in isolation, and two serialized full-suite passes completed.
That evidence supports a narrow scheduling and ownership cleanup; it does not
justify production runtime changes or broad timeout increases.

The release project currently has eleven test classes that launch child
processes either through `ReleaseTestProcess` or `ReleaseScriptRunner`:

- `PackageArchiveInspectScriptTests`;
- `PackageBinaryCompatPreflightScriptTests`;
- `PackageConsumerSmokeScriptTests`;
- `PackageFeedVerifyScriptTests`;
- `PackageListScriptTests`;
- `PackageReleaseDryRunScriptTests`;
- `PackageReleasePreflightScriptTests`;
- `PackageReleaseTagScriptTests`;
- `ReleaseScriptTests`;
- `ReleaseTestProcessTests`; and
- `SampleDocumentationTests`.

The operations sample already has the correct external contract: one durable
input, workflow transformation, durable output capture/delivery, host-owned BCL
listeners, exactly three explicit persisted-status reads, source-generated JSON,
deterministic output, bounded causal waits, and exact temporary-data cleanup.
This round must preserve that contract exactly.

## Objective

Make normal parallel release verification reliable and make the durability
operations sample easier to read without losing coverage or changing its output.

The completed round must:

1. serialize only release-test classes that own child processes;
2. preserve parallel execution for all other release-test classes and all other
   projects;
3. ensure abnormal child-process paths finish process-tree termination and
   stream/handle release before an owned temporary directory is deleted;
4. preserve timeout and caller-cancellation semantics without arbitrary timeout
   inflation;
5. replace redundant sample telemetry completion state with one explicit causal
   completion boundary;
6. retain exact two-run sample output verification;
7. reduce source-shape assertions to stable architectural boundaries rather
   than private variable names or formatting choices;
8. classify existing release-project formatting findings and fix only relevant
   touched-area findings; and
9. update documentation review evidence, memory, and this goal.

## Required Process-Test Design

Add one explicit xUnit collection definition in the release test project for
process-owning tests. Use a neutral, descriptive collection name. Keep the
collection's normal xUnit parallelization setting: membership in one collection
already serializes those classes with each other, while unrelated collections
remain free to execute in parallel.

Apply that collection to every release-test class that launches a child process,
including all eleven classes listed above. Re-audit the project after editing so
no direct `Process.Start`, `ReleaseTestProcess.RunAsync`, or
`ReleaseScriptRunner.RunAsync` owner is missed.

The design must remain explicit:

- do not set `DisableParallelization` on the collection and do not disable
  assembly-wide or solution-wide test parallelism;
- do not serialize file-only release governance tests;
- do not use reflection or a convention runner to discover process tests;
- do not add traits, retry attributes, or a custom test framework;
- do not duplicate process execution logic in individual test classes; and
- keep `ReleaseTestProcess` as the one test-only owner of process start, output
  capture, timeout/cancellation handling, and abnormal termination.

For the blocking-process fixture, remove avoidable ownership of its disposable
temporary directory by a live process. The launched script may use `$PSScriptRoot`
for script-relative files, but its current working directory should be a stable
external directory that is not deleted by the fixture. Preserve the exact
temporary script and marker ownership boundary.

On timeout or cancellation, `ReleaseTestProcess` must still:

- kill the owned process tree;
- wait for the root process to exit;
- finish draining redirected stdout and stderr;
- return only after its owned process and stream handles are releasable;
- distinguish timeout from caller cancellation;
- preserve the caller's exact cancellation token; and
- preserve cleanup failure as causal exception information.

Do not add `Task.Delay`, `Thread.Sleep`, polling, unbounded retry, or arbitrary
post-exit grace periods. Do not weaken the real-descendant assertion. Do not
change the three-second semantic timeout merely to hide contention. A larger
outer test watchdog is acceptable only if it guards the test harness rather
than changes the process helper's semantic timeout, and only if evidence shows
it is required after narrow serialization.

## Required Operations-Sample Simplification

Keep `DurabilityTelemetry` sample-local and directly based on
`MeterListener` and `ActivityListener`. It must continue to recognize only the
existing durable-input and durable-output sources, instruments, semantic tag
combinations, and activities.

Replace the four task-completion sources for input delivery, output completion,
all metrics, and all activities with one sample-owned completion signal meaning
that every required observation key has been recorded. A straightforward
implementation may use:

- one thread-safe observation dictionary;
- one fixed list of required metric and activity keys; and
- one `TaskCompletionSource` completed after the fixed set is present.

Keep metric/activity key creation explicit and readable. Do not introduce a
generic observer, rule engine, callback builder, inheritance hierarchy, or
production abstraction. Preserve checked counting, thread safety, listener
source filtering, instrument filtering, disposal, and stable formatting.

Update `Program.cs` to await only the two true scenario-level causal boundaries:

- the sample delivery handler received the transformed value; and
- the telemetry listener observed the complete fixed semantic set.

Continue to use the one bounded scenario cancellation token. Do not add delays,
polling, gauges, hosted status services, or database-read loops.

The sample's exact ten-line console contract must remain byte-for-byte stable
after newline normalization. It must still prove:

- one pending durable input before host startup;
- transformed output value `HELLO DURABILITY`;
- the representative durable-input metrics and input process activity;
- the representative durable-output capture/delivery metrics and activities;
- delivered final input status;
- completed final output status; and
- exactly two input snapshots, one output snapshot, and automatic polling off.

## Required Sample-Test Simplification

Keep the exact-output fact as the behavioral authority. It must run the sample
twice with `--no-build --no-restore`, require zero exit code and empty stderr for
both executions, compare the first output to the full expected contract, and
compare the second normalized output to the first.

Simplify the source-shape fact so it checks only stable architecture and safety
boundaries:

- host-owned `MeterListener` and `ActivityListener` construction;
- filtering to the durable-input and durable-output sources and known
  instruments;
- explicit disposal of both listeners;
- normal Generic Host construction, one start, and one stop;
- explicit input/output status-store usage and exactly three status reads;
- explicit source-generated string metadata and its JSON context declaration;
- exact owned temporary-directory cleanup;
- no arbitrary delay/polling loop, OpenTelemetry/exporter registration,
  observable gauge, hosted status service, server/listener, reflection, or
  synchronous task blocking; and
- only the existing Hosting package reference in the sample project.

Remove assertions coupled only to private field/property names, redundant
string fragments, or implementation formatting when the exact-output fact and
the smaller boundary checks already prove the behavior. Do not weaken the exact
status-read count, JSON metadata, no-poller, no-reflection, listener-disposal, or
cleanup protections.

Follow existing xUnit and Shouldly conventions. Tests must remain deterministic,
mutation-sensitive, and free of external services or network ports.

## Formatting-Finding Policy

The previous whole-project format scan reported 52 findings that predate this
round. Re-run or inspect the applicable formatting result and classify findings
before changing them.

- Fix formatting issues in files touched by this goal.
- Fix an adjacent finding only when it is clearly caused or exposed by this
  edit and is safe.
- Do not bulk-format the release project, sample tree, solution, or repository.
- Do not mix unrelated line-ending or style rewrites into this bounded goal.
- Record remaining pre-existing findings factually; do not claim the entire
  project is format-clean unless it actually is.

## Production Boundaries And Non-Goals

Do not change production code under `src/` in this round. Specifically, do not
change or add:

- public APIs or public API baselines;
- durable input/output dispatcher state machines;
- capture, delivery, retry, lease, acknowledgement, or idempotency semantics;
- persistence contracts, records, schemas, SQL, migrations, indexes, or cleanup;
- SQL-file or T-SQL provider behavior;
- workflow definitions, component activation, DSLs, registration APIs, or
  options;
- runtime telemetry sources, instruments, activities, tags, or packages;
- automatic status polling, health checks, dashboards, exporters, or servers;
- reflection, scanning, dynamic code, service locators, or hidden registration;
- package versions, package dependencies, or solution membership; or
- global test-runner configuration.

Do not split the cohesive production dispatchers merely because they are long.
They are reliability state machines whose explicit ordering is currently more
valuable than an extra abstraction. Do not expand this cleanup into unrelated
source timing tests unless repeated focused evidence identifies a real defect.

## Documentation And Memory

Review the operations sample README and the canonical hosting/status docs after
the implementation. They must continue to describe host-owned listeners,
causal waiting, explicit status snapshots, no background polling, provider
neutrality, at-least-once behavior, and cleanup accurately.

If the implementation does not change a public concept, do not churn the
documentation site merely to create a diff. Record the completed documentation
review in this goal and memory. Make a concise documentation edit only when the
existing wording is materially incomplete or no longer matches the sample.

Add `memory/288-release-verification-and-sample-cleanup.md` containing:

- the motivation and evidence;
- the narrow process-test collection decision;
- the process working-directory/cleanup ownership decision;
- the sample telemetry simplification;
- retained behavioral and architectural coverage;
- exact verification results;
- known pre-existing format findings; and
- the next recommendation.

Also update:

- `memory/00-index.md`;
- `memory/01-current-state.md`;
- `memory/04-architecture-decisions.md`; and
- `memory/07-progress-log.md`.

When execution completes, update this file to `State: complete`, list actual
files changed, record exact commands/results, and record deliberate deferrals.
Do not claim checks that were not executed.

## Test-Agent Evidence

Because this round edits tests, preserve the repository's mandatory test-agent
artifacts:

- `.testagent/research.md` must contain the bounded target inventory, existing
  conventions, process-owner audit, and acceptance checklist;
- `.testagent/plan.md` must map each behavioral requirement to an exact test or
  verification command;
- `.testagent/status.md` must record implementation status plus assertion-gap
  and assertion-quality review; and
- run the source/test pairing analyzer exactly once for this round, treating it
  as discovery evidence rather than coverage evidence.

Do not restore, reconstruct, or overwrite unrelated workspace content.

## Required Verification

Run verification in a bounded, non-overlapping order:

1. complete the mandatory source/test pairing analyzer exactly once and record
   its counts;
2. inspect the release project's test platform/framework and use the correct
   command/filter syntax;
3. build `FluxFlow.Release.Tests` in Release;
4. run focused `ReleaseTestProcessTests` and audit that every process-owning
   class has the shared collection attribute;
5. repeat the timeout/cancellation lifecycle facts enough times to detect the
   original contention or cleanup race without overlapping runs;
6. build `FluxFlow.DurabilityOperationsSample` in Release;
7. run the sample twice with `--no-build --no-restore` and compare exact output;
8. run the two durability-operations release facts;
9. run complete `SampleDocumentationTests`;
10. run the complete `FluxFlow.Release.Tests` project at least twice with its
    normal parallel settings;
11. run format verification for every touched C# project/file without rewriting
    unrelated files;
12. run a serialized Release solution build;
13. run one complete Release solution test pass with normal project/test
    parallelism and one complete serialized pass; repeat the normal path if it
    exposes a credible order/global-state problem;
14. run release governance/documentation boundary tests after goal, docs, and
    memory updates;
15. run `git diff --check`; and
16. inspect final diffs and repository status, preserving unrelated work.

Use bounded command timeouts. Shut down reusable build servers only when needed
to remove proven stale shared state. Never overlap full-solution builds or tests
that share `bin`/`obj`. Do not stage, commit, push, publish, or delete unrelated
files.

## Acceptance Criteria

The goal is complete only when all of the following are true:

- this complete goal existed before implementation edits;
- every release-test class that launches a process belongs to the one explicit
  nonparallel process-owner collection;
- non-process release tests remain parallelizable;
- timeout and cancellation tests retain exact semantics and real descendant
  termination coverage;
- the blocking fixture no longer makes its disposable directory a process
  working directory;
- no delay, retry attribute, global parallel switch, or inflated semantic
  timeout was introduced;
- the sample has one telemetry completion signal and no generic framework;
- the sample's exact two-run output is unchanged;
- exactly three explicit status reads, source-generated JSON, listener ownership,
  listener disposal, and cleanup remain protected;
- source-shape checks are smaller and focused on stable boundaries;
- no production source, API, package, schema, provider, registration, or
  delivery semantics changed;
- touched files are formatted and unrelated format findings were not bulk-fixed;
- focused release tests and repeated normal release-project tests pass;
- normal and serialized full verification pass;
- documentation was reviewed and remains accurate;
- memory and test-agent artifacts contain factual evidence; and
- unrelated dirty-worktree changes remain intact.

## Deliberately Deferred

- No production runtime refactor.
- No dispatcher split or generic reliability state-machine abstraction.
- No OpenTelemetry/exporter integration.
- No health/readiness adapter or status poller.
- No persistence/provider/schema work.
- No configuration or Fluent DSL work.
- No broad format cleanup.
- No change to the unrelated source timing test without reproducible evidence.

After this round, stop speculative cleanup. Choose the next product change from
concrete capability, operational, or user-experience evidence.

## Completion Evidence

Completed on 2026-08-02 without changing production source, public APIs,
dependencies, package metadata, schemas, providers, dispatchers, workflow/JSON/
registration contracts, delivery semantics, or global test-runner settings.

### Files Changed For This Goal

- `tests/FluxFlow.Release.Tests/ReleaseProcessCollection.cs`
- `tests/FluxFlow.Release.Tests/PackageArchiveInspectScriptTests.cs`
- `tests/FluxFlow.Release.Tests/PackageBinaryCompatPreflightScriptTests.cs`
- `tests/FluxFlow.Release.Tests/PackageConsumerSmokeScriptTests.cs`
- `tests/FluxFlow.Release.Tests/PackageFeedVerifyScriptTests.cs`
- `tests/FluxFlow.Release.Tests/PackageListScriptTests.cs`
- `tests/FluxFlow.Release.Tests/PackageReleaseDryRunScriptTests.cs`
- `tests/FluxFlow.Release.Tests/PackageReleasePreflightScriptTests.cs`
- `tests/FluxFlow.Release.Tests/PackageReleaseTagScriptTests.cs`
- `tests/FluxFlow.Release.Tests/ReleaseScriptTests.cs`
- `tests/FluxFlow.Release.Tests/ReleaseTestProcessTests.cs`
- `tests/FluxFlow.Release.Tests/SampleDocumentationTests.cs`
- `samples/FluxFlow.DurabilityOperationsSample/DurabilityTelemetry.cs`
- `samples/FluxFlow.DurabilityOperationsSample/Program.cs`
- `.testagent/research.md`
- `.testagent/plan.md`
- `.testagent/status.md`
- `memory/288-release-verification-and-sample-cleanup.md`
- `memory/00-index.md`
- `memory/01-current-state.md`
- `memory/04-architecture-decisions.md`
- `memory/07-progress-log.md`
- this goal file

The operations sample README, `docs/05-hosting-and-observability.md`, and
`docs/35-durability-operational-status.md` were reviewed and remain accurate.
No documentation-site content was changed because the cleanup changed no public
concept or usage and an edit would have been artificial churn.

### Implementation Result

- All eleven child-process-owning release-test classes share one named xUnit
  collection. The collection retains default parallel behavior, so its members
  serialize with each other while unrelated collections remain parallel.
- The blocking process fixture starts in `Path.GetTempPath()` rather than its
  deletable script directory. Script resolution remains `$PSScriptRoot`-based,
  and the real-descendant termination assertion is unchanged.
- `ReleaseTestProcess`, semantic timeouts, exception shapes, caller-token
  identity, stream draining, and process-tree termination code are unchanged.
- `DurabilityTelemetry` now owns one observation dictionary, one fixed required
  key set, and one completion source instead of two dictionaries and four
  completion sources.
- `Program.cs` awaits only delivery-handler completion and telemetry-set
  completion through the same bounded scenario token.
- The exact two-run ten-line output fact is unchanged. The source-shape fact is
  smaller but still protects listener ownership/filtering/disposal, one host
  lifecycle, exactly three status calls, source-generated JSON, exact cleanup,
  one Hosting package, and the no-poller/no-server/no-reflection/no-blocking
  boundaries.

### Exact Verification Results

- Mandatory Roslyn source/test pairing analyzer ran exactly once: 766 source
  files, 313 test files, 531 statically paired, 235 unpaired, 3,361 ms. This is
  a static pairing heuristic, not coverage evidence.
- Release test Release build: 45 projects, zero errors/warnings.
- `ReleaseTestProcessTests`: 5/5 passed.
- Timeout/cancellation lifecycle filter: five consecutive runs, each 2/2
  passed with zero warnings.
- Operations sample Release build: nine projects, zero errors/warnings.
- Direct operations sample: two successful sequential executions with identical
  exact ten-line output.
- Durability operations release facts: 2/2 passed.
- Complete `SampleDocumentationTests`: 6/6 passed.
- Complete Release project under normal parallel settings: two consecutive
  passes at 125/125, then a final post-memory pass at 125/125; zero warnings.
- Touched sample and Release C# format verification: passed with no changes.
  The preceding whole-project scan's 52 pre-existing unrelated findings were
  inspected as the applicable baseline and not bulk-rewritten.
- Serialized Release solution build: 134 projects, zero errors/warnings.
- Normal full Release suite: 2,488/2,488 across 66 projects, zero warnings.
- Serialized full Release suite: 2,488/2,488 across 66 projects, zero warnings.
- Final combined documentation-boundary and sample-documentation filter after
  completing goal and memory records: 20/20 passed with zero warnings.
- Final process-owner audit found the same eleven launcher classes and eleven
  collection attributes; the collection contains no `DisableParallelization`
  override.
- Final `git diff --check`: passed.
- Assertion-quality review: no new assertion-free, trivial-only,
  self-referential, skipped, or delay-based test.
- Pseudo-mutation review: no material survivor inside the bounded process and
  sample-test scope.

### Deliberately Deferred

- No production state-machine or dispatcher split.
- No generic process, telemetry, or test-convention framework.
- No global parallelism setting, retry attribute, sleep, polling, or semantic
  timeout increase.
- No OpenTelemetry/exporter, health check, readiness adapter, status poller,
  provider/schema/persistence, configuration, or Fluent DSL change.
- No broad rewrite of the 52 pre-existing Release-project format findings.
- No change to the unrelated source timing test because normal and serialized
  complete suites both passed without reproducing a defect.
