# GOAL: Add a runnable durability operations sample

## Status

- State: complete
- Date: 2026-08-02
- Repository: FluxFlow
- Scope: one non-server sample, focused release coverage, solution/docs inventory,
  documentation, goal evidence, and memory
- Compatibility posture: additive sample and documentation only; no production API,
  workflow, JSON, DSL, registration, persistence schema, provider behavior,
  delivery guarantee, or package-output change

## Role And Execution Instruction

Act as a senior .NET library maintainer. Treat the current dirty and untracked
workspace as authoritative and preserve all unrelated work. Save this complete
goal before changing sample, test, solution, documentation, or memory files,
then execute it fully.

Favor KISS, SRP, explicit dependencies, local ownership, and direct framework
APIs. The result must teach an ordinary host how to operate FluxFlow durability
without adding another framework to FluxFlow. Do not introduce reflection,
assembly scanning, dynamic proxies, automatic registration, hidden lifecycle
behavior, a telemetry facade, a health-check framework, a background status
poller, a database cache, dashboards, administration endpoints, or a large
dependency graph.

## Context

FluxFlow already provides the runtime features required by this slice:

- durable application input and durable output capture;
- SQL-file stores for local, runnable persistence;
- optional durable-output delivery;
- payload-free provider-neutral input/output status snapshots;
- event-driven BCL metrics and activities from the semantic durability
  boundaries;
- source-generated JSON overloads suitable for trimming/AOT-oriented hosts.

The missing piece is a concise, executable operations example that connects
these features in the way a real host should own them. Documentation currently
explains each feature, but users should also be able to run one deterministic
program and see:

1. a durable message waiting before the host starts;
2. that message entering a workflow after startup;
3. the workflow output being durably captured and delivered;
4. metrics and activities observed by host-owned BCL listeners; and
5. explicit before/after status snapshots requested by host code.

This is the next step because it validates and documents the existing extension
points without increasing production complexity. It is deliberately not a new
runtime abstraction.

## Objective

Add `samples/FluxFlow.DurabilityOperationsSample`, a normal .NET console-host
sample that runs one complete durable input-to-output cycle using local SQL-file
providers and terminates by itself.

The sample must demonstrate the ownership boundary clearly:

- FluxFlow emits semantic metrics and activities;
- the host chooses listeners/exporters and owns their lifecycle;
- status stores are queried only when the host explicitly asks for a snapshot;
- no component continuously queries persistence for observability;
- the at-least-once and idempotency contracts remain unchanged.

## Required Runtime Scenario

Use one small workflow with one explicit component:

- one string input port;
- one string output port;
- deterministic processing, such as converting the input to uppercase;
- one stable workflow-port address for durable input;
- one stable workflow-port address for durable output capture.

The program must:

1. create a unique temporary directory and database path owned by the sample;
2. configure a normal Generic Host;
3. register the FluxFlow application explicitly;
4. register SQL-file durable input and durable output against the temporary
   path using their existing builder-action APIs;
5. register a source-generated JSON contract for the input string;
6. register source-generated JSON metadata for the captured output string;
7. register one sample-owned durable-output delivery handler;
8. enable the existing durable-input and durable-output dispatchers with short,
   reasonable sample delays, without zero-delay busy loops;
9. attach host-owned `MeterListener` and `ActivityListener` instances before the
   host starts;
10. enqueue exactly one durable input before startup;
11. explicitly read and retain an input status snapshot before startup, proving
    that one item is pending;
12. start the host through the normal host lifecycle;
13. wait on causal completion signals with a bounded timeout, not on arbitrary
    sleeps or repeated database queries;
14. explicitly read input and output status snapshots after completion;
15. stop and dispose the host;
16. print a short deterministic summary; and
17. remove the temporary data after all database-owning services are disposed.

The completed scenario must establish that:

- the enqueue result is accepted;
- the before snapshot reports one pending input;
- the delivered value is the workflow's transformed value;
- the after input snapshot reports the message as delivered;
- the after output snapshot reports the captured output as completed;
- expected durable-input and durable-output metric names were observed; and
- expected durable-input and durable-output activity names were observed.

If any of these expectations is not met within the bounded timeout, the sample
must fail visibly rather than print a false success result.

## Sample Structure

Keep files shallow and cohesive. Prefer this feature-local structure unless an
existing convention makes a smaller equivalent clearer:

- `FluxFlow.DurabilityOperationsSample.csproj` — executable target and direct
  package/project references only;
- `Program.cs` — composition root and the short ordered scenario;
- `SampleWorkflow.cs` — application definition and component behavior;
- `DurabilityTelemetry.cs` — sample-owned BCL listener lifecycle and bounded
  in-memory observations;
- `SampleOutputDeliveryHandler.cs` — explicit delivery handler and causal
  completion signal;
- `SampleJsonContext.cs` — source-generated JSON metadata;
- `README.md` — runnable instructions and operational ownership guidance.

Combine files when that produces a genuinely smaller readable sample, but do not
create one oversized file or generic helper layer. Helper types are sample-only
and must not leak into production projects.

## Registration And Dependency Rules

- Use `Host.CreateApplicationBuilder` and normal `IHost` start/stop/disposal.
- Use the existing `services.AddFluxFlow(...)` registration path.
- Use the existing builder-action registration methods for SQL-file durable
  input/output and dispatcher options.
- Keep registration flat; avoid nested callback chains beyond the natural
  one-level builder actions.
- Register the delivery handler explicitly by its contract.
- Use `TimeProvider` already supplied by the host/runtime where an observation
  timestamp is required.
- Add only the standard `Microsoft.Extensions.Hosting` package if the executable
  sample needs the concrete Generic Host implementation. Manage its version in
  `Directory.Packages.props`, aligned with the repository's existing
  `Microsoft.Extensions.*` versions.
- Add no OpenTelemetry, exporter, logging-provider, database, ORM, resilience,
  health-check, or test package for this slice.
- Add no production project dependency because of the sample.
- Use no external server, credentials, ports, network access, or environment-
  specific absolute storage location at runtime.

The sample uses the SQL-file provider because it is self-contained. It must not
imply that durability is tied to SQL-file storage: the status and dispatcher
contracts remain provider-neutral, and hosts may substitute the existing T-SQL
provider or future providers at the same boundary.

## JSON And Trimming Rules

- Define a source-generated `JsonSerializerContext` for every payload type used
  by durable input/output registration.
- Pass the resulting `JsonTypeInfo<T>` explicitly to the applicable registration
  APIs.
- Do not use reflection-based serializer metadata as a shortcut.
- Do not add custom serialization wrappers or a second contract registry.

## Telemetry Listener Contract

`DurabilityTelemetry` must be a small sample-owned `IDisposable` or equivalent
lifecycle owner built directly on:

- `System.Diagnostics.Metrics.MeterListener`; and
- `System.Diagnostics.ActivityListener`.

It must listen only to these existing sources:

- `FluxFlow.Engine.DurableInput`; and
- `FluxFlow.Engine.DurableOutput`.

It must recognize the existing semantic activity names:

- `fluxflow.durable_input.process`;
- `fluxflow.durable_output.capture`; and
- `fluxflow.durable_output.deliver`.

It must collect observations in memory and print them only after the scenario;
callbacks must not perform file/database/network I/O or block dispatcher work.
Use thread-safe bounded state appropriate for this one-message sample. Preserve
deterministic output by sorting distinct names before rendering them.

Use causal completion signals from listener callbacks or the delivery handler
only where they correspond to an actual semantic completion event. Await those
signals with `WaitAsync` and one explicit timeout. Do not use `Thread.Sleep`,
`Task.Delay` as a readiness mechanism, retry loops, or status polling.

Do not print payloads, application/workflow/component/port identifiers, message
identifiers, trace identifiers, lease owners/tokens, exception text, or database
paths from telemetry tags. The demonstration should print only stable metric and
activity names plus a small, intentional business result. Explain that real
exporter configuration, sampling, retention, and redaction remain host policy.

The sample may state that BCL `Meter` and `ActivitySource` signals can be consumed
by an OpenTelemetry bridge, but it must not install or configure one in this
round. This keeps the example runnable with framework APIs alone and avoids
presenting one exporter stack as runtime policy.

## Status Snapshot Contract

Resolve the existing `IDurableInputStatusStore` and
`IDurableOutputStatusStore` explicitly from the host service provider and call
their `GetStatusAsync` APIs only at the two intentional observation points:

- before host start: durable-input snapshot after enqueue;
- after causal completion: durable-input and durable-output snapshots.

Pass an explicit current timestamp and cancellation token as required by the
contracts. Do not wrap status stores, cache their results, expose them as gauges,
add a hosted status service, or schedule recurring queries.

Render only the counts necessary to show the state transition. Treat status as
an on-demand persisted snapshot, not a liveness proof and not an automatic health
check.

## Lifecycle, Failure, And Cleanup Rules

- Attach listeners before starting work that should be observed.
- Enqueue before host startup so the before status is deterministic.
- Start the Generic Host exactly once and stop it exactly once.
- Use one bounded scenario cancellation/timeout and propagate cancellation.
- Dispose listeners and the host in a clear ownership order.
- Delete only the exact unique temporary directory created by the sample, after
  all provider connections are disposed.
- Do not use broad recursive deletion targets, unresolved environment variables,
  or repository-relative runtime data.
- Let unexpected failures produce a non-zero process exit so release smoke tests
  cannot accept partial output.

## Deterministic Console Contract

Keep the final console output short and stable. It must include unique markers
that focused release tests can assert, covering:

- the before input state;
- the after input state;
- the after output state;
- the delivered transformed value;
- the durable-input process activity;
- the durable-output capture and delivery activities;
- representative input/output metric names; and
- an explicit statement that status snapshots were requested on demand and no
  automatic status polling was installed.

Do not include timestamps, GUIDs, generated paths, duration values, unordered
collections, or logging noise in the required output. If default host logging
would make the sample noisy or environment-dependent, configure it explicitly
and minimally at the sample boundary.

## Production Boundaries And Non-Goals

Do not change any production source under `src/` unless implementation proves an
actual defect that makes the documented public contracts unusable. If that
happens, stop and record the blocker rather than silently expanding this goal.

Specifically do not add or change:

- automatic metric/status polling;
- observable gauges backed by database calls;
- built-in health checks or readiness endpoints;
- OpenTelemetry packages or exporters;
- a FluxFlow telemetry configuration object;
- global runtime options or `FluxFlowApplicationOptions` settings;
- delivery semantics, retry policy meanings, idempotency, or at-least-once
  guarantees;
- persistence interfaces, records, provider factories, schemas, SQL, migrations,
  or cleanup policy;
- workflow/component DSL APIs;
- package IDs, versions, symbols, XML docs, or public API baselines;
- production reflection, scanning, dynamic code, or service-locator patterns.

This round does not implement dashboards, alert rules, an operator UI, distributed
coordination, a new provider, an ORM, or end-to-end OpenTelemetry export.

## Solution And Documentation Work

Add the sample project to the `samples` solution folder in `FluxFlow.sln`, with
the repository's complete configuration mappings.

Update:

- `docs/README.md` sample inventory;
- `docs/05-hosting-and-observability.md` with the host-owned listener example and
  link to the runnable sample;
- `docs/35-durability-operational-status.md` with the explicit on-demand status
  usage and link to the runnable sample;
- any directly affected durability documentation only where required to avoid
  contradictory guidance; and
- the sample README with `dotnet run --project ...`, expected behavior,
  ownership boundaries, provider-neutral substitution guidance, privacy and
  cardinality notes, at-least-once semantics, and cleanup behavior.

Do not duplicate the entire observability contract across multiple documents.
Use one concise canonical explanation and cross-links where practical. All code
snippets must match compilable repository APIs.

## Test Work

Extend focused release coverage in the smallest suitable existing test class.
At minimum:

1. the existing sample-inventory test must discover the new project and prove it
   is listed in the solution and docs inventory;
2. the non-server sample smoke data must run the new project with
   `--no-build --no-restore`, require exit code zero, and assert the stable
   success markers; and
3. repeat execution must remain deterministic and must not leave repository
   artifacts or shared database state.

Prefer output-contract assertions over tests coupled to internal helper types.
Do not add delays, retry polling, broad exception catches, exact elapsed-time
assertions, environment-specific paths, or a second process runner.

Because process-level listeners and host lifecycle are involved, repeat the
focused smoke and full suite to catch global-state leaks and disposal errors.

## Memory And Goal Evidence

Update the durable repository memory in the same round:

- add `memory/287-durability-operations-sample.md` with the decision, ownership
  boundary, exact scenario, tests, and next recommendation;
- add the entry to `memory/00-index.md`;
- update `memory/01-current-state.md`;
- update `memory/04-architecture-decisions.md`; and
- append factual completion and verification evidence to
  `memory/07-progress-log.md`.

When execution is complete, update this file:

- change the state to `complete` only after all required work passes;
- list the actual files changed;
- record exact focused/full verification commands and results;
- record final source/test pairing analyzer counts from the analyzer already run
  once for this round; and
- document any deliberately deferred work.

Do not claim checks that were not executed.

## Required Verification

Run verification in an order that gives fast, attributable failures:

1. run the repository's source/test pairing analyzer exactly once for this
   round and record its output;
2. restore the new sample after its project/package references are added;
3. build the new sample in Release;
4. run the new sample twice in Release with no build/restore and compare the
   required deterministic markers;
5. run the focused `SampleDocumentationTests` in Release;
6. run directly affected durability tests if any sample integration exposes an
   affected contract;
7. run format verification without rewriting unrelated files;
8. build the full solution in Release with one serialized build path;
9. run the complete test suite in Release twice to detect order/global-state
   leakage;
10. run public API, package metadata, documentation inventory, and release
    governance tests to prove the sample did not change shipped contracts;
11. run relevant package-vulnerability inspection for the added host package;
    and
12. inspect final diffs and repository status, preserving unrelated work.

Use bounded command timeouts. Never run overlapping full-solution builds or test
runs into shared `bin`/`obj` outputs. Do not stage, commit, push, or publish unless
the user separately requests it.

## Acceptance Criteria

The goal is complete only when all of the following are true:

- the full goal existed on disk before implementation began;
- the new non-server sample builds and exits successfully without external
  infrastructure;
- it demonstrates one actual durable input, workflow execution, durable output
  capture, and durable output delivery;
- it uses source-generated JSON metadata;
- host-owned BCL listeners observe both durability sources;
- before/after status is queried explicitly, with no background status polling;
- the sample uses bounded causal waits and deterministic output;
- temporary storage is removed after disposal;
- no production runtime/API/schema/provider behavior changed;
- no reflection, magic discovery, automatic telemetry policy, or large
  dependency graph was introduced;
- the solution and docs inventory include the sample;
- focused release tests cover its runnable output;
- docs explain ownership, privacy/cardinality, provider neutrality, and
  at-least-once limitations honestly;
- memory and this goal contain factual final evidence;
- focused and full verification pass repeatedly; and
- the dirty worktree's unrelated changes remain intact.

## Deferred Follow-Up

After this sample proves the host boundary, evaluate a separate, optional
OpenTelemetry host integration example only if users need exporter-specific
guidance. Keep that work outside the FluxFlow runtime, choose packages based on
the target host, and avoid turning exporter policy into engine configuration.

Also consider an operator-facing health/readiness adapter only after concrete
host requirements define thresholds and query cadence. It must remain an
explicit host integration over existing status contracts, never an automatic
database poller inside FluxFlow.

## Completion Evidence

Completed on 2026-08-02 without changing any production source under `src/`,
public API, provider schema/behavior, workflow/JSON/DSL contract, delivery
guarantee, or shipped package dependency/version.

### Files Changed For This Goal

- `Directory.Packages.props`
- `FluxFlow.sln`
- `samples/FluxFlow.DurabilityOperationsSample/FluxFlow.DurabilityOperationsSample.csproj`
- `samples/FluxFlow.DurabilityOperationsSample/Program.cs`
- `samples/FluxFlow.DurabilityOperationsSample/SampleWorkflow.cs`
- `samples/FluxFlow.DurabilityOperationsSample/SampleOutputDeliveryHandler.cs`
- `samples/FluxFlow.DurabilityOperationsSample/SampleJsonContext.cs`
- `samples/FluxFlow.DurabilityOperationsSample/DurabilityTelemetry.cs`
- `samples/FluxFlow.DurabilityOperationsSample/README.md`
- `tests/FluxFlow.Release.Tests/SampleDocumentationTests.cs`
- `docs/README.md`
- `docs/05-hosting-and-observability.md`
- `docs/35-durability-operational-status.md`
- `memory/00-index.md`
- `memory/01-current-state.md`
- `memory/04-architecture-decisions.md`
- `memory/07-progress-log.md`
- `memory/287-durability-operations-sample.md`
- `.testagent/research.md`, `.testagent/plan.md`, and `.testagent/status.md`
- this goal file

### Exact Verification Results

- Mandatory source/test pairing analysis ran once before implementation:
  759 production sources, 311 test sources, 528 paired, 231 unpaired. This is
  static pairing evidence, not runtime coverage.
- Sample restore: nine projects restored, zero errors/warnings.
- Sample Release build: nine projects, zero errors/warnings.
- Direct sample run: exit code zero and identical deterministic output in two
  executions.
- Focused durability-operations release facts: 2/2 passed.
- Complete `SampleDocumentationTests`: 6/6 passed.
- Final combined sample/documentation-boundary filter after goal and memory
  updates: 20/20 passed.
- Sample format verification: passed with no changes.
- Touched `SampleDocumentationTests.cs` format verification: passed with no
  changes. A whole release-project scan surfaced 52 pre-existing unrelated
  style findings; none was rewritten.
- Sample package vulnerability inspection: no vulnerable packages under the
  configured sources.
- Serialized Release solution build: 134 targets, zero errors/warnings.
- Release governance project: 125/125 passed.
- Full serialized Release suite, pass one: 2,488/2,488 across 66 projects,
  zero warnings.
- Full serialized Release suite, pass two: 2,488/2,488 across 66 projects,
  zero warnings.
- `git diff --check`: passed before final record updates and is rerun as the
  final hygiene gate.

Initial aggregate runs under machine load produced timeouts in unrelated
existing process/source timing tests after 2,486 passes. All three observed
failures passed together in isolation. Reusable build servers were shut down,
and the two consecutive complete serialized passes above are the authoritative
repository result. No unrelated runtime or timing-test change was made.

### Deliberately Deferred

- No OpenTelemetry/exporter package or exporter-specific sample was added.
- No automatic status polling, gauge, health check, readiness policy, cache,
  dashboard, server, or administration surface was added.
- No new storage provider, ORM, generic repository, or provider abstraction was
  added.
- Exporter-specific host guidance and a host-owned readiness adapter remain
  separate future decisions driven by concrete requirements.
