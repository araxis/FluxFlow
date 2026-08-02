# GOAL: Make the FluxFlow Release test gate deterministic and bounded

## Status

- State: complete
- Date: 2026-08-02
- Repository: FluxFlow
- Scope: test code and test-only process orchestration
- Compatibility posture: no production, public API, package, persistence,
  workflow, JSON, DSL, registration, or runtime behavior change

## Role And Execution Instruction

Act as a senior .NET maintainer completing a narrowly bounded release-test
stabilization round. Treat the current workspace as authoritative, including
all existing dirty and untracked files. Preserve unrelated work. Save this goal
before changing test or helper source, then execute it completely.

Prefer direct, familiar C# and existing .NET APIs. Apply KISS, SRP, explicit
ownership, deterministic synchronization, and bounded cancellation. Do not add
magic, reflection, global state, a framework, a broad abstraction hierarchy, or
another dependency. A capable maintainer must be able to understand every
touched path in one sitting.

## Context And Evidence

The durable-output lease-renewal round is functionally green:

- core durable-output tests passed 162/162;
- SQL-file durable-output tests passed 166/166;
- fast T-SQL tests passed 136/136 across both target frameworks;
- the full real-server T-SQL suite passed 117/117 with zero skips;
- the serialized Release solution build completed with zero errors and
  warnings;
- public API, package, consumer, vulnerability, formatting, and release gates
  passed.

However, repeated solution-wide test attempts did not produce one trustworthy
all-green aggregate on a concurrently loaded workstation:

- two broad attempts reached 2,452/2,453 with only the slow non-server sample
  smoke test timing out;
- another reached 2,449/2,453 with four unrelated load-sensitive failures that
  all passed unchanged in isolation;
- a final run excluding sample documentation tests exceeded 30 minutes while
  `FluxFlow.Resilience.Tests` remained active, although that project passed
  11/11 in isolation.

Inspection identified concrete deterministic weaknesses:

1. `RetryTests.Executor_retries_results_with_deterministic_time` starts the
   executor, advances `FakeTimeProvider`, yields once, and advances again. A
   scheduler delay can allow either clock advance to occur before the retry
   operation has reached and registered the corresponding timer. `Task.Yield`
   is not a causal synchronization boundary.
2. `SampleDocumentationTests.Non_server_samples_run_to_completion` invokes
   `dotnet run` without `--no-build`, `--no-restore`, or an explicit matching
   configuration. During a Release solution test it can start nested Debug
   builds and contend for build servers, package state, CPU, and disk.
3. `ReleaseScriptRunner` redirects both output streams but has no timeout,
   cancellation contract, or process-tree cleanup. A child script or descendant
   can therefore outlive or indefinitely block the test host.

This goal must fix those mechanisms rather than hiding them with longer
timeouts, sleeps, global serialization, test skips, or weaker assertions.

## Objective

Make the normal Release verification path deterministic, finite, and honest:

- fake-time tests advance time only after the exact preceding operation state
  is causally observable;
- sample smoke tests execute the artifacts already built for the current test
  configuration and never restore or build inside `dotnet test`;
- every test-owned child process has an explicit timeout/cancellation boundary,
  concurrently drained output streams, exact process-tree cleanup, and no
  abandoned descendant;
- focused stress evidence and consecutive complete solution passes demonstrate
  that the repair addresses the observed failures;
- no production behavior, public contract, package version, or dependency
  changes.

## Required Design

### 1. Causal fake-time retry test

Strengthen `FluxFlow.Resilience.Tests/RetryTests.cs` without changing
`RetryExecutor` production behavior merely to accommodate the test.

- Replace `Task.Yield` as the sequencing mechanism.
- Use exact attempt observation, normally one small
  `TaskCompletionSource`-based causal gate or an equally explicit existing
  repository helper.
- Use `TaskCreationOptions.RunContinuationsAsynchronously` when a completion
  source is used.
- Do not advance the fake clock until the operation delegate has entered the
  attempt whose preceding timer should already have completed.
- Advance exactly one configured delay at a time.
- Bound all waits with `WaitAsync(...)` so a regression fails rather than hangs.
- Assert the exact attempt numbers and final result, not only the aggregate
  attempt count.
- Do not use `Thread.Sleep`, `Task.Delay` with real time, spin waiting,
  unbounded polling, or scheduler luck.
- Do not change `RetryExecutor`, `RetryStateMachine`, or retry policy semantics
  unless a separately proven production defect is discovered.

Expected causal sequence:

1. start execution;
2. observe operation attempt 1 returning retry;
3. advance fake time by exactly the first delay;
4. observe operation attempt 2 returning retry;
5. advance fake time by exactly the second delay;
6. observe operation attempt 3 returning success;
7. await bounded completion and assert `success-3` plus exact sequence
   `[1, 2, 3]`.

### 2. Small test-only bounded process boundary

Introduce at most one small internal process helper inside
`FluxFlow.Release.Tests`, only if it removes the duplicated lifecycle logic
between release scripts and sample smoke execution.

The helper must have one responsibility: execute one explicitly supplied
`ProcessStartInfo` within a supplied positive timeout and optional cancellation
token, returning exit code plus complete standard output/error on normal exit.

Required behavior:

- validate a positive, finite timeout;
- start exactly the requested process without shell interpolation;
- set `UseShellExecute = false` whenever streams are redirected;
- begin draining stdout and stderr concurrently immediately after start;
- wait with the caller token and timeout token;
- distinguish caller cancellation from timeout;
- on timeout or cancellation, kill the entire owned process tree when it is
  still active;
- observe process exit and both stream tasks after cleanup;
- tolerate the normal race in which the process exits between the state check
  and `Kill(...)`;
- never swallow an output-read failure or return a successful result before
  both streams are complete;
- do not log or include complete argument lists, environment values, or other
  potentially sensitive process configuration in timeout text;
- use a small constant cleanup bound only to prevent cleanup itself from
  hanging;
- contain no platform-specific process enumeration, WMI, shell script, global
  registry, service locator, or reflection.

Keep result data immutable. Reuse the existing `ReleaseScriptResult` if its
name remains accurate at the release-script boundary; otherwise use one small
test-local immutable process result and adapt without expanding public surface.

### 3. Bound `ReleaseScriptRunner`

Route `ReleaseScriptRunner` through the bounded process boundary.

- Preserve both existing overloads and all current call sites when practical.
- Use one explicit default timeout suitable for the existing local release
  scripts; do not choose an effectively infinite value.
- Optionally add an internal timeout/cancellation overload only when required
  for direct regression testing.
- Preserve environment removal/override behavior exactly.
- Preserve `-NoLogo`, `-NoProfile`, `-ExecutionPolicy Bypass`, `-File`, and
  argument-list construction without string concatenation.
- On a normal nonzero script exit, return the existing result; do not turn the
  exit code into an exception.
- On timeout, throw a stable `TimeoutException` that identifies only the safe
  script filename and configured timeout.
- On caller cancellation, propagate `OperationCanceledException` carrying the
  caller token after cleanup.

### 4. Run sample smoke tests from prebuilt artifacts

Keep all three current non-server sample assertions and their exact expected
output. Change only how the child process is launched and bounded.

- Continue using the project path so the sample inventory remains explicit.
- Invoke `dotnet run` with separate `ArgumentList` entries.
- Add `--no-build` and `--no-restore`.
- Add `--configuration` using the configuration that compiled the current test
  assembly. A direct compile-time `Debug`/`Release` constant is acceptable and
  preferable to path parsing or reflection.
- Remove `--disable-build-servers`; no child build should exist to disable.
- Use the shared bounded process boundary rather than a second timeout/kill
  implementation.
- Use a short but practical per-sample timeout because the artifacts are
  already built; the timeout must remain explicit and finite.
- Preserve complete stdout/stderr in the existing nonzero-exit assertion.
- Include only the safe project path and timeout in a timeout failure.
- Execute samples serially as they are now; do not introduce parallel child
  processes.

The normal full verification sequence must build the solution before invoking
`dotnet test --no-build`, so every sample artifact for the chosen configuration
exists. Running the Release test project without first building its sample
dependencies may correctly fail with a clear missing-artifact error; the test
must never silently trigger a build.

### 5. Regression evidence for process ownership

Use the repository's existing xUnit and Shouldly conventions. Add only focused
test-local regression cases that are deterministic and do not require network,
fixed ports, containers, external services, or machine-global mutation.

At minimum prove:

- normal exit returns the exact exit code, stdout, and stderr;
- timeout throws the exact stable timeout exception and terminates the owned
  child process;
- caller cancellation propagates cancellation and terminates the owned child;
- release-script environment override/removal behavior remains intact when an
  existing test already proves it, or add a focused case if absent;
- sample launch arguments include current configuration, `--no-build`, and
  `--no-restore`, preferably by a directly testable start-info factory rather
  than introspecting a live process;
- the retry test cannot advance past attempt boundaries without observing the
  preceding attempt.

Do not add a mock framework solely for these cases. If testing actual local
process lifecycle is required, use a short-lived, repository-owned .NET or
PowerShell command with temporary files/directories and guaranteed cleanup.
Do not use sleeps as synchronization; child readiness must be signaled through
stdout or a temporary marker observed with a bounded asynchronous mechanism.

## Testing Pipeline Requirements

Before editing any test file:

1. Run the mandatory Roslyn `find-untested-sources` analyzer exactly once for
   the bounded repository scope and record its exact counts. Treat it as static
   source/test pairing, not line or branch coverage.
2. Create or update `.testagent/research.md` with:
   - affected files and call sites;
   - current xUnit/Shouldly conventions;
   - exact test-platform/SDK detection;
   - the complete acceptance checklist;
   - the one-time pairing result and caveat.
3. Create or update `.testagent/plan.md` mapping every behavioral requirement
   to a concrete test name or a precise non-test verification artifact.
4. Use the independent `code-testing-generator` pipeline for test generation
   or strengthening. Treat the current workspace as authoritative and do not
   restore or reconstruct missing files.
5. Build and run the narrow test projects during correction loops.
6. Reopen every changed test and complete pseudo-mutation and assertion-quality
   audits before the full solution gate.
7. Record final requirement-to-evidence mapping and audit conclusions in
   `.testagent/status.md`.

No test may be skipped, muted, weakened, or converted into a mere smoke call to
obtain a pass. An empty method body, omitted process kill, removed timeout,
wrong configuration, missing `--no-build`, reordered fake-time advance, or
unobserved stream task must be caught by at least one assertion or static
source-shape check.

## Verification Plan

### Discovery and baseline

- Record SDK, test platform, xUnit version, and target frameworks from
  `global.json`, project files, `Directory.Build.props`, and
  `Directory.Packages.props`.
- Capture the current targeted file status without modifying unrelated work.
- Run the pairing analyzer once before test-source edits.
- Reproduce or document the pre-fix race mechanism without adding a sleep-based
  flaky test.

### Focused build and tests

- Build `FluxFlow.Resilience.Tests` and `FluxFlow.Release.Tests` in Release.
- Run `FluxFlow.Resilience.Tests` repeatedly under a bounded command and verify
  every repetition completes with all 11 logical tests.
- Run the exact retry test repeatedly enough to exercise scheduling variance.
- Run focused process-boundary regression tests repeatedly.
- Run the three `SampleDocumentationTests` after a Release solution/sample
  build and verify all pass without starting a nested build.
- Run the complete Release test project and report exact counts, failures,
  skips, duration, and warnings.
- Re-run the previously load-sensitive MQTT controller, Sessions, and MQTT
  adapter filters to ensure no unrelated regression.

### Repository gates

- Run a serialized, no-incremental Release solution build with no restore and
  require zero errors and warnings.
- Run the full Release solution tests with no build and a finite outer command
  envelope. Use the platform-correct VSTest syntax for SDK 10 plus xUnit v2.
- Require two consecutive complete all-green solution test runs before calling
  the aggregate gate deterministic. If external load prevents that, record the
  exact limitation and do not convert it into a pass.
- Use blame-hang only if a post-fix run still hangs; do not add a new package
  merely to obtain diagnostics.
- Run touched-project `dotnet format --verify-no-changes --no-restore`.
- Run `git diff --check` and a touched-scope trailing-whitespace scan.
- Verify no owned sample, script, `dotnet test`, testhost, or descendant process
  remains after every focused and aggregate run.
- Confirm no package dependency, package version, public API baseline, package
  manifest, schema, production assembly, or release artifact changed.

## Documentation And Memory

Update repository records after implementation:

- add one concise testing/release-gate section to the appropriate developer or
  release documentation, or update the existing section if one already owns
  this guidance;
- explain that solution tests execute prebuilt sample artifacts and therefore
  require the matching configuration to be built first;
- document the finite child-process and cleanup guarantee without exposing
  internal implementation trivia;
- update `memory/00-index.md`, `memory/01-current-state.md`,
  `memory/04-architecture-decisions.md`, and `memory/07-progress-log.md`;
- add the next numbered memory file describing the decision and exact evidence;
- mark this goal complete only after final evidence is written here.

## Explicit Non-Goals

Do not implement any of the following:

- production retry, scheduling, timing, logging, or process APIs;
- a public process runner or reusable package;
- a new test framework, mock library, assertion library, or dependency;
- global xUnit parallelization disablement;
- solution-wide test serialization as the fix;
- longer timeouts as the only remedy;
- sleeps, spin waits, retry-until-pass loops, or probabilistic assertions;
- removal, skipping, conditional exclusion, or weakening of an existing test;
- changes to durable input/output, SQL providers, MQTT, Sessions, components,
  Engine, Composition, Designer, JSON, C# DSL, registration, or application
  options;
- public API baseline, package version, release manifest, package description,
  changelog, or migration changes;
- CI workflow, branch, commit, push, pull request, tag, or publication changes;
- cleanup of unrelated dirty or untracked files.

## Acceptance Criteria

The goal is complete only when:

- the detailed goal existed before test/helper source edits;
- the pairing analyzer ran exactly once before test-source edits and its counts
  are recorded with the static-pairing caveat;
- the fake-time retry test uses causal attempt gates and bounded waits, with no
  `Task.Yield`, real-time delay, sleep, spin, or polling;
- sample smoke tests run the prebuilt current-configuration artifacts with
  `--no-build --no-restore` and preserve all output assertions;
- all release-test child processes have finite timeout/cancellation ownership,
  concurrent stream draining, tree cleanup, and observed exit;
- normal exit, timeout, cancellation, output capture, launch arguments, and
  cleanup have concrete regression evidence;
- the independent generated-test pipeline, gap audit, and assertion audit find
  no remaining requirement-level hole;
- focused stress tests pass repeatedly without skip or hang;
- the complete Release test project passes;
- the Release solution build is warning- and error-free;
- two consecutive full Release solution test runs pass, or an exact external
  limitation is recorded honestly without a false pass;
- formatting, whitespace, docs links, dependency/version/API invariants, and
  owned-process cleanup pass;
- documentation, goal evidence, and memory are current;
- no unrelated file is restored, deleted, staged, or rewritten.

## Completion Evidence

Completed on 2026-08-02.

- The mandatory source/test pairing inventory ran once before implementation:
  1,068 C# files were discovered, comprising 759 source files and 309 test
  files; the static naming heuristic reported 528 paired and 231 unpaired
  source files in 3,239 ms. This was treated as discovery evidence, not runtime
  coverage.
- `FluxFlow.Resilience.Tests` built in Release with 0 warnings and 0 errors.
  `Executor_retries_results_with_deterministic_time` passed individually and
  in ten consecutive repetitions. Its assertions prove the exact result,
  attempt sequence, and causal fake-time transitions.
- `FluxFlow.Release.Tests` built in Release with 0 warnings and 0 errors. The
  bounded-process regression suite passed 5/5 in three observed runs, and its
  combined process/sample-argument filter passed 6/6.
- The three non-server sample projects built in Release without warnings. Their
  serial prebuilt smoke test passed, and the complete Release verification
  project passed 123/123 tests in 16.0 seconds.
- The serialized no-restore, no-incremental Release solution build completed
  133 projects/targets with 0 warnings and 0 errors in 2:03.94.
- Two consecutive no-restore, no-build full Release solution passes each
  completed 2,459/2,459 tests across 66 projects with 0 warnings. Their elapsed
  test times were 126.8 seconds and 68.9 seconds.
- Both touched test projects passed `dotnet format --verify-no-changes`. The 14
  documentation-boundary tests passed after the documentation and memory
  updates, `git diff --check` was clean, and no independent test-owned sample or
  process-fixture command remained.
- Process regressions prove concurrent draining with exact 131,072-character
  stdout and stderr payloads, exact nonzero exit propagation, finite timeout,
  caller-token preservation, environment removal/override, and termination of
  a real descendant process tree on timeout and cancellation.
- Assertion-quality and pseudo-mutation review confirmed that removing attempt
  gates, timeout handling, cancellation distinction, tree termination, stream
  redirection, configuration selection, `--no-build`, `--no-restore`, or
  environment behavior is detected by a focused test.
- Production source, public API baselines, package references, package
  versions, persistence, workflow semantics, DSL, and registration behavior
  were not changed. No new dependency or test-parallelization override was
  added. Existing unrelated dirty and untracked workspace files were preserved.
