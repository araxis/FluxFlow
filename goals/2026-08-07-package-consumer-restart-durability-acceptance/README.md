# Goal: Prove Package-Only Durability Recovery Across Process Restart

## Status

- State: accepted for execution
- Date: 2026-08-07
- Repository: `C:\Projects\FluxFlow`
- Accepted base branch: `main`
- Runtime feature scope: none unless the acceptance proof exposes a real defect
- Public API scope: none
- Publication scope: none

## Objective

Close the remaining operational evidence gap between FluxFlow's focused
durability tests and its one-process operations sample. Extend the existing
package-only acceptance consumer so two distinct operating-system processes
share SQL-file durable state:

1. a seed process persists durable input and output work, leases both records,
   records one already-applied host-owned side effect, and exits without
   settling either lease; and
2. a recovery process opens the same files, starts the normal Generic Host,
   FluxFlow application, durable-input dispatcher, durable-output capture, and
   durable-output delivery dispatcher, then proves that expired work is
   recovered and reaches terminal state.

This is an acceptance and release-confidence round. It must validate the
existing at-least-once contract without claiming exactly-once delivery and
without adding workflow checkpoints, distributed orchestration, reflection,
runtime discovery, or a second acceptance framework.

## Current Evidence And Remaining Gap

- Focused dispatcher and provider suites already prove lease expiry,
  redelivery, retry, fencing-token replacement, terminal transitions, and
  provider reopen behavior.
- `samples/FluxFlow.DurabilityOperationsSample` proves durable input, workflow
  execution, durable output capture, and output delivery through one normal
  Generic Host process.
- `eng/package-consumer-acceptance` proves that an isolated package-only
  consumer can restore the exact candidate package closure and reopen
  SQL-file input/output stores after service-provider disposal.
- The checked-in release gates do not yet prove that the normal hosted
  dispatchers compose correctly after a real process boundary.
- The output delivery contract is intentionally at-least-once. A destination
  side effect may have succeeded before a process stops and before the durable
  completion transition commits. The acceptance proof must demonstrate a
  simple host-owned idempotency receipt for that window.

## Architecture Decision

Reuse the existing package-only fixture and runner:

- keep `eng/package-consumer-acceptance/Program.cs` as the small mode router and
  existing Engine, Fluent, and store-reopen scenarios;
- add one cohesive
  `eng/package-consumer-acceptance/RestartDurabilityScenario.cs` file containing
  the restart-specific seed/recover workflow, fixed clock, delivery handler,
  receipt handling, and exact assertions;
- extend `eng/package-consumer-acceptance.ps1` to copy all checked-in fixture
  C# files into its isolated work directory and invoke the already-built
  consumer three times: the existing default mode, restart seed, and restart
  recovery; and
- keep the existing nine-package candidate closure, isolated restore, archive
  hash verification, build, cleanup, and CI integration.

Do not create a second console project, duplicate candidate package
resolution, add the fixture to the solution, or generalize the runner into a
process framework. Separate process invocation is the required boundary; a
new reusable abstraction is not.

## Required Behavior

### 1. Explicit command modes

The package-only executable must accept exactly these shapes:

- no arguments: run the existing Engine, Fluent, and SQL-file reopen checks;
- `durability-restart-seed <absolute-data-directory>`: create the restart
  state; and
- `durability-restart-recover <absolute-data-directory>`: recover it through
  normal hosted services.

Reject missing paths, relative paths, extra arguments, and unknown modes with
clear errors and a non-zero exit code. Mode selection must be a direct switch;
do not use reflection, command frameworks, configuration binding, environment
discovery, or hidden defaults.

### 2. Deterministic seed process

The seed mode must:

1. require that its caller-owned data directory does not already contain
   restart state, then create only its required child files/directories;
2. use fixed identities, contracts, addresses, payloads, trace ids, headers,
   and a fixed UTC seed time;
3. register the existing SQL-file durable-input and durable-output providers
   with explicit allowed absolute paths;
4. enqueue one durable string input targeting the restart workflow input and
   require `Enqueued`;
5. enqueue one independent durable output representing an external side
   effect and require `Enqueued`;
6. lease the input and output at the fixed seed time with a fixed expiry and
   require attempt 1, non-empty lease tokens, exact identities, exact owners,
   and exact expiry;
7. write one host-owned idempotent effect file for the pre-leased output; the
   file is both the destination effect and its receipt, representing the crash
   window after the destination accepted the effect but before FluxFlow
   completed the delivery lease;
8. leave both durable leases unsettled and dispose the provider; and
9. emit exact seed success and identity markers only after all assertions
   pass.

The seed process must not kill itself, corrupt a database, sleep until lease
expiry, or use an internal test hook. A normal process exit with intentionally
unsettled leases provides the required process boundary safely.

### 3. Recovery through normal hosting

The recovery mode must:

1. require the input database, output database, receipt, and external-effect
   evidence created by seed mode;
2. use a fixed UTC recovery time later than the seeded lease expiry so recovery
   is immediate and independent of wall-clock speed;
3. create a normal Generic Host with logging noise disabled;
4. register one explicit uppercase runtime component and a one-node canonical
   restart workflow through `AddFluxFlow`;
5. register the existing SQL-file providers over the seed files;
6. register the normal durable-input dispatcher and the exact source-generated
   string contract for the workflow input;
7. register durable-output capture for the workflow output and the normal
   durable-output delivery dispatcher;
8. replace the default `TimeProvider` with a tiny explicit provider whose UTC
   value is fixed for lease decisions while base timers continue to drive the
   hosted polling delays;
9. start the host with a bounded cancellation timeout;
10. prove the expired durable input is recovered, delivered to the workflow,
    transformed exactly, captured durably, and settled as delivered;
11. prove the expired pre-existing output is recovered by the delivery
    dispatcher and settled as completed;
12. prove the captured workflow output is delivered and settled as completed;
13. verify final input status is exactly one delivered record with no pending,
    leased, or dead-lettered records;
14. verify final output status is exactly two completed records with no
    unmaterialized, pending, leased, or dead-lettered records;
15. verify exactly two idempotent effect/receipt files exist: the already
    applied seed effect appears once, and the transformed workflow effect
    appears once;
16. stop the host with a separate bounded timeout; and
17. emit exact input-recovery, output-recovery, idempotency, and final restart
    success markers only after terminal status and filesystem evidence agree.

### 4. Host-owned idempotency receipt

The acceptance handler must model the public contract honestly:

- use `DurableOutputEnvelope.Key`, including its message identity, as the
  idempotency identity;
- represent the local acceptance destination with one small effect file that
  is also its receipt and contains the exact contract and payload content;
- create that idempotent destination effect atomically;
- when the same identity and content already have a receipt, return success
  without repeating the external effect so the dispatcher can complete the
  abandoned lease;
- fail if one identity is reused with different content; and
- remain fixture-local rather than becoming a FluxFlow runtime abstraction.

This demonstrates a recommended host pattern. It does not make FluxFlow
exactly-once, does not coordinate the receipt transaction with the SQL-file
store, and must not be documented as doing so.

### 5. Determinism and bounded execution

- Lease timestamps and observations must use fixed UTC values.
- Do not add arbitrary long sleeps or wait for wall-clock lease expiry.
- Short hosted-service polling intervals are allowed because they exercise the
  real dispatcher loop; completion must be observed through explicit task or
  persisted-state conditions.
- Every wait must have a cancellation token or explicit timeout.
- Host stop must remain bounded even after a primary failure.
- Failure messages must name the phase and missing invariant.

### 6. Runner integration

Extend the existing acceptance runner to:

1. copy every top-level fixture `*.cs` file, not just `Program.cs`, while
   retaining the exact project-file check and project-reference rejection;
2. build the isolated consumer once;
3. run the existing default scenario once and retain its four exact markers;
4. create one restart data directory under the already owned work root;
5. invoke seed and recover as two separate `dotnet run --no-build --no-restore`
   processes with the same absolute restart path;
6. print stable default, seed, and recovery command markers during preparation;
7. capture and echo both process outputs;
8. require every seed/recovery marker exactly once and fail closed for missing,
   duplicate, or malformed evidence;
9. print `PACKAGE_ACCEPTANCE_RESTART_COMPLETE=True` only after recovery proof;
10. retain caller-owned work roots for diagnostics and remove runner-owned
    roots in the existing `finally` block on success or failure; and
11. preserve candidate archive verification, source ownership, package-cache
    isolation, and all existing parameters and behaviors.

Do not add a second script or CI step. The existing CI call already owns the
complete package-only behavioral gate and will gain the restart proof through
the runner.

## Test Plan

Use the existing xUnit/Shouldly release-test conventions and the mandatory
test-generation evidence under `.testagent/`.

Focused tests must prove at least:

1. the fixture remains `net8.0`, package-only, and free of project references;
2. the fixture contains explicit seed/recover modes, normal hosting
   registrations, fixed recovery time, source-generated string metadata, and
   exact markers;
3. the fixture-local idempotency handler uses durable output identity and
   rejects identity/content conflict;
4. the runner copies all fixture C# files;
5. preparation prints separate default, seed, and recovery commands without
   creating work or package directories;
6. real isolated execution starts seed and recovery as distinct processes over
   one shared restart directory;
7. output contains exact input recovery, output recovery, idempotency, restart,
   and runner-completion markers;
8. a missing seed/recovery marker fails the runner;
9. failure cleanup removes runner-owned work while caller-owned diagnostic work
   remains;
10. the complete CI rehearsal still invokes the one acceptance runner exactly
    once after solution tests; and
11. all pre-existing package source, hash, restore, build, marker, and ownership
    contracts continue to pass.

Before closeout, review the new tests for assertion quality and gaps. Each
required behavior must map to an exact test name or an explicit non-behavioral
artifact/command in `.testagent/status.md`.

## Documentation And Memory

Update:

- `docs/24-reliable-in-process-delivery.md` to point to the process-restart
  acceptance evidence while preserving the in-process/durable boundary;
- `docs/25-durable-inputs.md` with the expired-lease restart proof and its
  at-least-once limitation;
- `docs/29-durable-output-delivery.md` with the fixture-local idempotency
  receipt proof and the remaining crash/transaction boundary;
- `docs/38-release-validation.md` so the existing package-consumer command is
  documented as a three-process durability restart gate in addition to the
  existing scenarios;
- `docs/README.md` only if a new documentation page is added; none is planned;
- `memory/00-index.md`;
- `memory/01-current-state.md`; and
- one new numbered memory record containing implementation, validation,
  boundaries, and remaining limitations.

The repository's Markdown documentation is the documentation-site source for
this round; no separate site directory exists.

## Non-Negotiable Principles

1. KISS, SRP, explicit registrations, and one cohesive acceptance slice.
2. Reuse the existing package-only fixture, runner, candidate closure, and CI
   invocation.
3. No reflection, assembly scanning, service locator, hidden static mutable
   state, command framework, environment inference, or generated runtime graph.
4. No arbitrary lease-expiry sleep and no unbounded wait.
5. No runtime source, public API, schema, package identity, version, or
   publication change unless the test exposes a real product defect that is
   documented before fixing.
6. No exactly-once claim. The engine and delivery handler remain at-least-once;
   host-owned idempotency controls repeated destination effects.
7. No durable workflow checkpointing, internal queue persistence, distributed
   transaction, transport acknowledgement, or broker scenario.
8. No new database provider, ORM, container, network service, or credential.
9. No second acceptance project, generalized process harness, or abstraction
   hierarchy.
10. Preserve existing Engine, Fluent, store reopen, archive hash, package-only,
    per-package smoke, and CI behavior.
11. Preserve unrelated work and do not perform broad cleanup.

## Explicit Non-Goals

- durable workflow execution state or checkpoint/replay;
- exactly-once workflow or output semantics;
- crash injection by terminating the process mid-instruction;
- T-SQL or another provider in the restart gate;
- MQTT, HTTP, broker, AI-agent, or IoT integration;
- new health/readiness APIs or telemetry exporters;
- public idempotency-store interfaces;
- runner/framework extraction for hypothetical future acceptance fixtures;
- release publication, tag creation, pull request, or package version change;
- unrelated refactoring or code-size cleanup.

## Validation Sequence

1. Build the package-only fixture against controlled local candidate packages.
2. Run the focused `PackageConsumerAcceptanceScriptTests` class using the
   repository's VSTest/xUnit filter syntax.
3. Run the package-consumer runner in preparation mode and verify exact
   default/seed/recover commands without filesystem mutation.
4. Run the real package-only acceptance gate with locally packed candidates;
   require all existing and restart markers.
5. Inspect retained diagnostic work once, then remove only owned temporary
   state.
6. Run the complete `FluxFlow.Release.Tests` project.
7. Run the complete solution Release build and tests if the focused gate is
   green.
8. Run formatting, diff/whitespace, vulnerable-package, scope, neutral-name,
   and repository-status checks.
9. Confirm runtime source, public API baselines, package projects, package
   versions, schemas, CI step count, and publication state are unchanged.

## Acceptance Criteria

This goal is complete only when:

- the package-only consumer has explicit default, seed, and recovery modes;
- seed and recovery execute as separate operating-system processes over one
  shared SQL-file state directory;
- an input lease and output lease abandoned by seed mode are expired
  deterministically without wall-clock waiting;
- normal Generic Host registrations recover the input, execute the workflow,
  capture its output, and complete both output deliveries;
- final persisted status proves one delivered input and two completed outputs
  with no live or dead-lettered work;
- the pre-applied destination effect is not repeated, the workflow effect is
  applied once, and identity/content conflict fails closed;
- exact restart evidence markers are required by the existing runner;
- runner ownership and diagnostics behavior remain correct on success and
  failure;
- focused and release validation pass with auditable test names and commands;
- documentation and memory state the proof and its at-least-once boundaries;
  and
- no unapproved runtime, API, schema, package, dependency, publication, or
  architectural expansion occurs.
