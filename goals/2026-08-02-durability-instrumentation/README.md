# GOAL: Add lightweight built-in durability instrumentation

## Status

- State: complete
- Date: 2026-08-02
- Repository: FluxFlow
- Scope: provider-neutral durable-input and durable-output semantic boundaries,
  focused tests, documentation, goal evidence, and memory
- Compatibility posture: additive telemetry behavior only; no workflow, JSON,
  DSL, registration, persistence, provider schema, delivery-guarantee, package-
  dependency, or `FluxFlowApplicationOptions` change

## Role And Execution Instruction

Act as a senior .NET library maintainer. Treat the current dirty and untracked
workspace as authoritative and preserve all unrelated work. Save this complete
goal before changing production or test source, then execute it fully.

Favor direct framework APIs, KISS, SRP, explicit ownership, and stable semantic
boundaries. Use `System.Diagnostics.Metrics` and `System.Diagnostics.ActivitySource`
from the BCL. Do not introduce OpenTelemetry packages, reflection, scanning,
dynamic proxies, a telemetry framework, background metric workers, database
pollers, caches, dashboards, administration endpoints, or a large abstraction
graph. Do not generalize test-only process infrastructure into production.

## Context

FluxFlow now has optional provider-neutral durable input and output packages,
local SQL-file providers, networked T-SQL providers, acknowledgement, capture,
leased delivery, exact renewal, retry, dead letter, replay, status, and bounded
retention. Existing runtime instrumentation uses the standard BCL `Meter` and
`ActivitySource`, but the durability packages currently expose semantic state
only through logs and explicit status-store queries.

Status queries are exact database aggregate reads. They must remain explicit
host operations and must not become an automatic metrics source. The next
production-readiness improvement is event-driven instrumentation at the
provider-neutral semantic boundaries already owned by FluxFlow.

## Objective

Add low-overhead, listener-driven metrics and tracing for durable input and
durable output so hosts can observe capture, dispatch, settlement, retry,
dead-letter, renewal, ownership-loss, failure, and duration behavior through
standard .NET diagnostics without changing any provider or application model.

When no listener is attached, instrumentation must perform no I/O, allocate no
background object, start no worker, and execute only the ordinary minimal BCL
instrument checks/calls plus existing time reads needed for an observed
operation. Host listener failures must never change durable processing.

## Required Architecture

### 1. Package-local instrumentation ownership

Add exactly one small internal static instrumentation class to each package:

- `FluxFlow.Engine.DurableInput/DurableInputInstrumentation.cs`
- `FluxFlow.Engine.DurableOutput/DurableOutputInstrumentation.cs`

Each class owns its package-local `Meter`, `ActivitySource`, instruments,
static operation names, bounded tag values, listener isolation, and elapsed-
time recording. Do not add an interface, service registration, options object,
shared runtime telemetry framework, or provider callback.

Use these source names:

- meter/activity source `FluxFlow.Engine.DurableInput`
- meter/activity source `FluxFlow.Engine.DurableOutput`

The classes remain internal. Meter, activity, instrument, and tag names are a
documented telemetry contract, not a new public C# API surface.

### 2. Cardinality and privacy rules

Metric tags may contain only bounded semantic values controlled by FluxFlow:

- `outcome`
- `result`
- `operation`
- `failure.kind`
- `acknowledgement.mode` when it materially distinguishes behavior

Do not put application addresses, contract names, message IDs, trace IDs,
correlation IDs, causation IDs, lease tokens, owner IDs, payloads, headers,
exception types/messages, connection strings, database names, file paths, or
provider names into metric tags.

Activities use static operation names, never message-, address-, contract-, or
exception-derived names. They may carry `flow.trace_id`, delivery `attempt`,
and acknowledgement mode as correlation/debug attributes. They must not carry
payloads, headers, errors, credentials, connection details, lease tokens, or
owner IDs. Trace identity is allowed only on activities, never metrics.

### 3. Durable-input metrics

Instrument the existing provider-neutral `DurableInputDispatcher`; do not
instrument SQL-file or T-SQL commands.

Required instruments:

- `fluxflow.durable_input.leases.acquired`, `Counter<long>`, unit `{lease}`:
  one for every validated lease returned by the store; do not count empty poll
  results.
- `fluxflow.durable_input.messages`, `Counter<long>`, unit `{message}`:
  record applied outcomes `delivered`, `retry`, and `dead_letter`. Retry and
  dead-letter measurements include the bounded public failure kind. A rejected
  stale/not-found transition is not reported as a completed outcome.
- `fluxflow.durable_input.lease.renewals`, `Counter<long>`, unit `{renewal}`:
  record `applied` or `rejected` after an exact validated renewal result.
- `fluxflow.durable_input.store.failures`, `Counter<long>`, unit `{failure}`:
  record the fixed internal store operation name when a non-caller-cancellation
  store exception is wrapped.
- `fluxflow.durable_input.processing.duration`, `Histogram<double>`, unit `ms`:
  record elapsed time for each leased input processed by the dispatcher.

Required activity:

- `fluxflow.durable_input.process`, `ActivityKind.Consumer`, surrounding one
  leased input's provider-neutral processing. Use the persisted flow trace id
  only as an activity attribute; do not claim it is automatically a W3C parent
  context. Include the integer attempt and acknowledgement mode.

Activities and durations must close on success, retry, dead letter, ownership
loss, exception, and caller cancellation. Instrumentation must not settle or
otherwise mutate a lease.

### 4. Durable-output capture metrics

Instrument `DurableOutputCapture<T>.CaptureAsync` once around serialization and
store enqueue. Preserve all existing exception instances, messages, ordering,
cancellation, serialization, key validation, and conflict behavior.

Required instruments:

- `fluxflow.durable_output.captures`, `Counter<long>`, unit `{capture}`, with
  bounded result values `enqueued`, `already_exists`, `conflict`, `canceled`,
  and `failed`.
- `fluxflow.durable_output.capture.duration`, `Histogram<double>`, unit `ms`,
  recorded once for every attempted capture with the same bounded result tag.

Required activity:

- `fluxflow.durable_output.capture`, `ActivityKind.Producer`. It should inherit
  `Activity.Current` naturally when capture runs inside an existing runtime
  trace. Add only safe static/correlation attributes.

An invalid/null/mismatched/unknown store result and serialization/store failure
are `failed`. A caller-requested cancellation is `canceled`. A conflict is
recorded before preserving the existing conflict exception. Accepted statuses
retain their exact `enqueued` or `already_exists` result.

### 5. Durable-output delivery metrics

Instrument the existing provider-neutral `DurableOutputDeliveryDispatcher`;
do not touch provider SQL or schema.

Required instruments:

- `fluxflow.durable_output.leases.acquired`, `Counter<long>`, unit `{lease}`,
  only after lease validation.
- `fluxflow.durable_output.handler.calls`, `Counter<long>`, unit `{call}`, with
  bounded result `succeeded`, `failed`, or `canceled`.
- `fluxflow.durable_output.deliveries`, `Counter<long>`, unit `{message}`, with
  outcome `completed`, `retry`, `dead_letter`, or `ownership_lost`; transition
  outcomes include result `applied` or `rejected` where relevant.
- `fluxflow.durable_output.lease.renewals`, `Counter<long>`, unit `{renewal}`,
  with result `applied` or `rejected`.
- `fluxflow.durable_output.store.failures`, `Counter<long>`, unit `{failure}`,
  tagged only by the fixed internal operation name.
- `fluxflow.durable_output.delivery.duration`, `Histogram<double>`, unit `ms`,
  once per validated leased delivery.

Required activity:

- `fluxflow.durable_output.deliver`, `ActivityKind.Consumer`, surrounding one
  validated lease. Include the persisted flow trace id only as an activity
  attribute and the integer attempt. Keep the operation name static.

Record handler success only when the handler completes normally. Record failure
when a synchronous or asynchronous handler exception reaches the dispatcher.
Record cancellation when host cancellation, store/renewal failure, or ownership
loss causes the in-flight handler to be canceled and observed. Do not count a
missing lease as work. Preserve the existing serial dispatcher, renewal race
rules, cancellation observation, retry/dead-letter threshold, and at-least-once
guarantee.

### 6. Listener isolation and no-listener cost

Every instrumentation entry point must catch exceptions raised by host-owned
metric/activity listeners. Instrumentation failures are ignored and must not
alter return values, thrown production exceptions, settlement, cancellation,
or logging.

Use `Instrument.Enabled` and `ActivitySource.HasListeners()` where they avoid
unnecessary tag/timestamp work. Do not benchmark by assertion, add a production
feature flag, or create per-message dictionaries solely for telemetry. Use
`TagList` or direct tag parameters and the existing injected `TimeProvider`.

### 7. Public API, package, and provider boundary

This round must not:

- add or change public C# contracts;
- change API baselines or package versions;
- add a NuGet package or project reference;
- change SQL-file/T-SQL code, schema, migrations, commands, or tests except
  where a repository-wide test naturally observes unchanged behavior;
- change durable settings or registration;
- add instrumentation to `FluxFlowApplicationOptions`;
- add automatic status polling, health checks, gauges backed by database I/O,
  exporter configuration, dashboards, or OpenTelemetry dependencies;
- add parallel delivery, batching, backoff policies, transport adapters,
  automatic replay/purge, checkpoints, distributed transactions, or exactly-
  once claims.

## Test Generation Pipeline

Before writing test source:

1. Run the mandatory Roslyn `find-untested-sources` analyzer exactly once for
   the repository and record the static pairing counts and caveat.
2. Record the bounded source/test inventory, existing xUnit/Shouldly and
   `MeterListener`/`ActivityListener` conventions, and the complete acceptance
   checklist in `.testagent/research.md`.
3. Record a concrete test map in `.testagent/plan.md`.
4. Use the available independent `code-testing-generator` agent to review and
   implement or recommend the focused tests. It must preserve the current
   workspace and may touch only assigned durability test files and test-agent
   artifacts.
5. Before completion, perform assertion-quality and pseudo-mutation review and
   record results in `.testagent/status.md`.

Focused tests must use real `MeterListener` and `ActivityListener`, deterministic
`FakeTimeProvider`, existing recording stores/handlers, exact values, and
bounded waits only where asynchronous coordination is unavoidable. They must
not use sleeps, polling, network ports, external services, reflection, mocks,
new packages, global test-parallelization disablement, or assertion-free smoke
calls.

At minimum, prove:

- exact meter/source and instrument/activity names;
- correct counter values and bounded tags for input delivered, retry,
  dead-letter, renewal applied/rejected, and store failure;
- input duration and activity close exactly once;
- output capture records each accepted status, conflict, cancellation, and
  failure without changing existing behavior;
- output delivery records lease, handler success/failure/cancellation,
  completed/retry/dead-letter/ownership-loss, renewal applied/rejected, store
  failure, duration, and activity;
- no forbidden identity, payload, exception, connection, path, provider, or
  ownership tag appears on metric measurements;
- activity names are static and allowed activity attributes are exact;
- a throwing metric listener and a throwing activity listener cannot fault or
  alter capture/input/output behavior;
- instrumentation does not query a status capability or perform additional
  store/provider operations;
- existing cancellation tokens and production exception identity/messages are
  preserved.

Prefer a small instrumentation-test file per package plus narrowly extended
behavior tests only where existing fixtures make the semantic assertion clearer.
Do not duplicate the entire dispatcher suite.

## Documentation And Memory

Update:

- `docs/05-hosting-and-observability.md` with the standard listener/exporter
  boundary and cross-reference;
- `docs/25-durable-inputs.md` with exact input meter/activity names,
  instruments, tags, meanings, privacy/cardinality rules, and limits;
- `docs/27-durable-output-capture.md` and
  `docs/29-durable-output-delivery.md` with exact output contracts;
- `docs/35-durability-operational-status.md` to distinguish event-driven
  metrics from explicit status snapshots and preserve the no-poller rule;
- both package READMEs with concise consumption and contract summaries;
- `docs/README.md` only if its index needs an entry or changed description;
- `memory/00-index.md`, `memory/01-current-state.md`,
  `memory/04-architecture-decisions.md`, and `memory/07-progress-log.md`;
- a new `memory/286-durability-instrumentation.md` with decisions, exact names,
  verification evidence, limits, and the recommended next step;
- this goal's completion evidence and status.

Documentation must state that:

- the instruments exist only when the optional durability packages execute;
- any .NET diagnostics/OpenTelemetry-compatible host can subscribe to the
  documented names, but FluxFlow installs no exporter;
- metrics are transition/event counters and latency histograms, not durable
  state snapshots;
- status snapshots remain explicit store reads and may change immediately;
- metric listeners receive no application identity or payload tags;
- at-least-once and destination-idempotency requirements do not change.

## Verification Plan

### Discovery and focused development

- Detect SDK/test platform/framework from `global.json`, build props, package
  props, and the target test projects.
- Run the static pairing analyzer once before test generation.
- Build and test `FluxFlow.Engine.DurableInput.Tests` and
  `FluxFlow.Engine.DurableOutput.Tests` in Release.
- Run focused instrumentation filters repeatedly to detect global-listener
  leakage or nondeterminism.
- Run provider-neutral durability tests and the existing SQL-file/T-SQL fast
  suites because custom providers consume the unchanged contracts.

### Repository gates

- Run `dotnet format --verify-no-changes` for both touched production/test
  project slices.
- Run a serialized no-restore, no-incremental Release solution build.
- Run the complete Release solution test gate without restore/build at bounded
  project concurrency; repeat once if global static listener behavior makes
  isolation a material risk.
- Run release documentation, package-boundary/version, public-API, and package
  archive tests relevant to the touched packages.
- Verify public API baselines and package references/versions are unchanged.
- Run `git diff --check`.
- Scan the touched scope for reflection, status polling, hidden workers,
  sleeps/polling tests, high-cardinality metric tags, sensitive telemetry,
  new dependencies, and global test-parallelization overrides.
- Confirm no unrelated file was restored, deleted, staged, or rewritten.

## Acceptance Criteria

The goal is complete only when:

- all required metrics and activities are emitted at the exact semantic
  boundaries with documented names, units, and bounded tags;
- listener failure cannot alter durable behavior;
- no provider/schema/status-polling/application-option/public-API/package-
  dependency surface changed;
- focused tests map every behavioral requirement to exact assertions;
- assertion-quality and pseudo-mutation review find no unresolved material gap;
- documentation, memory, and goal evidence are current;
- touched formatting, API, package, focused, provider, release, build, and full
  test gates pass without warnings or failures;
- unrelated dirty and untracked workspace work remains preserved.

## Completion Evidence

### Implemented boundary

- Added exactly one internal instrumentation owner to each provider-neutral
  durability package. Both use only BCL `Meter`/`ActivitySource`; no service,
  registration, option, exporter, provider callback, worker, reflection, or
  dependency was added.
- Wired durable input at validated lease, applied message settlement, renewal,
  wrapped store-failure, and processing-duration boundaries. Wired durable
  output around capture plus validated leased handler/delivery settlement.
- Static instrument publication, measurement callbacks, activity-start
  callbacks, completion callbacks, and ambient `Activity.Current` restoration
  are failure-isolated. Instrument enablement/listener checks avoid material
  tag/timestamp work when unobserved.
- Metric dimensions are limited to the documented bounded `outcome`, `result`,
  `operation`, and `failure.kind` semantics. Flow trace identity and attempt are
  activity-only; no payload, address, contract, message/lease/owner identity,
  provider, path, connection, exception text, or secret enters metric tags.

### Test generation and focused evidence

- The mandatory Roslyn pairing analyzer ran exactly once before test source:
  759 production sources, 311 test sources, 528 paired, and 231 unpaired in
  2,520 ms. This is a static pairing heuristic, not runtime coverage.
- Pre-change Release baselines passed 144 durable-input and 162 durable-output
  tests with zero warnings.
- Independent test generation added one focused file per package. Final filters
  passed input 10/10 and output 17/17 twice consecutively (855 ms and 1.1 s for
  the two final output repetitions). Complete provider-neutral projects passed
  input 154/154 and output 179/179 with zero warnings.
- Real `MeterListener` and `ActivityListener` assertions cover every exact
  instrument/source/activity name, type, unit, bounded tag, activity kind,
  accepted/conflict/cancellation/failure result, applied/rejected transition,
  renewal, ownership loss, handler result, store failure, duration, finalization,
  privacy rule, listener failure, and exact store/handler call count.
- The recorded assertion-quality and pseudo-mutation audit found no unresolved
  material gap, assertion-free/trivial test, sleep, polling loop, network,
  reflection, mock, new package, or global parallelization override.

### Provider, release, package, and repository gates

- Unchanged provider fast suites passed: SQL-file input 127/127, T-SQL input
  138/138 across two target-framework executions, SQL-file output 166/166, and
  T-SQL output 136/136 across two target-framework executions; zero warnings.
- Release governance passed 123/123 with zero warnings, including documentation,
  durability version, package boundary, manifest, and public API baseline gates.
- Fresh `FluxFlow.Engine.DurableInput` 1.3.0 and
  `FluxFlow.Engine.DurableOutput` 3.0.0 package/symbol archives were created in
  an isolated temporary directory and both passed archive inspection. The
  verified temporary directory was then removed.
- All four touched production/test projects passed
  `dotnet format --verify-no-changes --no-restore`.
- The serialized no-restore/no-incremental Release solution build passed 133
  targets with zero errors and zero warnings in 5:03.28.
- Two consecutive no-build/no-restore complete Release sweeps each passed
  2,486/2,486 tests across 66 projects with zero warnings (180.8 s and 65.3 s
  test time). The exact 27-test increase equals the new 10 input plus 17 output
  instrumentation executions.
- `git diff --check`, scoped trailing-whitespace, forbidden-pattern, sensitive-
  tag, listener-leak, status-query, reflection, hidden-worker, sleep/polling,
  new-dependency, and global-parallelization scans passed.

### Compatibility and honest limits

- No public C# contract, API baseline, project/package reference, package
  version, application/DSL/JSON model, registration setting,
  `FluxFlowApplicationOptions`, SQL-file/T-SQL source, schema, migration, or
  command changed in this round.
- Real external T-SQL integration suites were not rerun because this round
  changes no provider, SQL, schema, persistence contract, or registration;
  both fast multi-target T-SQL suites and the complete repository gate passed.
- Metrics remain live event/transition and latency signals, not durable state.
  Exact backlog visibility still requires an explicit status-store query, and
  at-least-once crash windows plus host-owned destination idempotency remain.
