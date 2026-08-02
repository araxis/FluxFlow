# Architecture Decisions

Date: 2026-05-31

## Package boundary

`FluxFlow.Engine` is a protocol-neutral workflow runtime. It knows how to build and run typed node graphs, but it does not know how to connect to brokers, call web endpoints, write files, store sessions, or render a designer.

## Extension model

Applications add behavior by registering node factories with `RuntimeNodeFactoryRegistry`.

Component packages should expose:

- one or more `IFlowNode` implementations;
- node factory registration helpers;
- component-owned options and validation;
- component-owned event type constants;
- focused tests for the component behavior.

## Definition ownership

The engine owns only graph execution definitions. Design-time metadata should remain outside the engine unless it directly affects runtime behavior.

## Scenario ownership

Scenario and test definitions are not part of the engine package boundary.
Applications or companion testing packages own test documents, step types,
validation, runners, and reports. The engine exposes runtime events and
diagnostics so those layers can observe workflow behavior without making the
engine own test semantics.

## Versioning

Use semantic versioning. Until the public API is stable, publish prerelease versions such as `0.1.0-alpha.1`. Use `1.0.0` only after the boundary is clean, docs are accurate, and core behavior is covered by tests.

## Durable-input completion acknowledgement

Engine acceptance remains the default durable-input settlement boundary because
it is small, fast, and sufficient for ordinary in-process workflows. A host
that needs later acknowledgement must opt into `WorkflowCompleted` and provide
one `IDurableInputCompletionSource`. Completion is an explicit result tied to
the exact durable lease; the adapter must not infer it from workflow graphs,
outputs, diagnostics, trace timing, or idle periods.

Lease renewal is a separate optional store capability rather than another
member on the cohesive `IDurableInputStore`. Workflow-completion mode requires
exactly one `IDurableInputLeaseRenewalStore`, processes one entry at a time, and
renews only the exact current unexpired token to the requested expiry. This
keeps existing providers source-compatible and ordinary dispatch free of
completion subscriptions and renewal calls.

The feature strengthens the acknowledgement boundary but remains
at-least-once. A crash after workflow side effects or completion but before
durable settlement can redeliver the entry. It does not provide durable
workflow state, checkpoints, exactly-once execution, or a distributed
transaction with application side effects.

## Networked durable-input provider

Shared durable ingress belongs in a separate optional provider package rather
than Engine or `FluxFlowApplicationOptions`. The production T-SQL provider
implements the three established durable-input capabilities through one
singleton and one flat registration callback. The provider adds no new core
contract, dispatcher branch, hosted service, reflection, ORM, generic
repository, or provider-neutral relational framework.

Multi-host correctness uses explicit database semantics: serializable
idempotent enqueue, locking-read-committed cooperative leasing, exact token and
expiry compare-and-set transitions, exact renewal, and generation-protected
replay. Schema creation/validation is versioned, transactionally application-
locked, bounded, and fail-closed. State-changing commands are not automatically
retried because an interrupted commit can be ambiguous. The guarantee remains
at-least-once, and durable internal workflow checkpoints remain a separate
non-goal.

## Durable terminal retention

Retention is a separate optional operational store capability, not a member of
the cohesive capture, delivery, dead-letter, renewal, or status interfaces and
not an application/workflow option. Hosts own policy and scheduling. FluxFlow
provides only explicit address-scoped deletion with an exclusive cutoff and a
hard bounded batch size; it does not register a timer or worker.

Provider transactions are the concurrency boundary. SQL-file and T-SQL delete
only the requested terminal state with direct set-based SQL. Output providers
delete capture parents so the existing foreign-key cascade removes delivery
state and materialization cannot recreate completed work. No schema migration,
ORM, generic repository, reflection, or distributed lock is justified.

Terminal deletion deliberately ends the identity's deduplication/idempotency
window. Dead-letter deletion deliberately ends replay availability. These
consequences belong in the public contract and operations documentation rather
than hidden provider defaults.

## Durable-output lease renewal

Long-running durable-output handlers renew their exact current unexpired lease
through the cohesive `IDurableOutputDeliveryStore`. Unlike durable input, the
output delivery interface already owns the entire lease lifecycle, so a second
renewal capability and another DI alias would split one responsibility without
enabling a meaningful capture-only combination. The deliberate 3.0 contract
change adds one immutable renewal request and one store method directly.

Timing remains one level deep on the existing delivery builder. The immutable
options require a positive renewal interval shorter than the lease duration;
there is no hidden defaulting overload for direct construction. The dispatcher
uses its existing `TimeProvider`, serial handler, and hosted loop. It creates no
heartbeat worker, `Task.Run`, queue, reflection path, or policy graph.

The store is the ownership authority. Renewal updates only expiry for the exact
key/token in leased, unexpired state. Any non-applied renewal cancels and
observes the handler and prevents stale settlement. SQL-file and T-SQL providers
use their existing transaction/transition paths and columns, so no schema,
dependency, provider option, or application option is added. The guarantee
remains at-least-once; destination idempotency is still host-owned.

## Deterministic test-owned time and process boundaries

Tests that control virtual time synchronize on observable domain progress
before advancing the exact configured interval. Scheduler hints, wall-clock
sleeps, polling, and arbitrary extra advances are not acceptable substitutes
for a causal gate.

Release verification owns every process that it starts. One small test-only
boundary starts the exact command without a shell, drains standard output and
error concurrently, applies an explicit finite timeout, distinguishes timeout
from caller cancellation, and terminates the owned process tree before it
returns. Sample smoke tests consume matching prebuilt artifacts with
`--no-build --no-restore`. This remains test infrastructure: it adds no runtime
service, public API, package, reflection path, or production dependency.

All release-test classes that launch child processes share one named xUnit
collection. Membership serializes those owners with each other; the collection
keeps normal parallelization behavior so unrelated file-only tests are not
blocked. The blocking descendant fixture runs outside its deletable script
directory, avoiding current-directory handle ownership without adding retries,
sleeps, or longer semantic timeouts.

## Provider-neutral durability instrumentation

Durability telemetry belongs at the provider-neutral capture and dispatcher
boundaries because those boundaries know whether work was accepted, retried,
dead-lettered, renewed, rejected, canceled, or lost. Provider commands and
schemas must not duplicate those semantic signals.

Each optional durability package owns one internal static BCL `Meter` and
`ActivitySource` holder. This is intentionally smaller than a registered
telemetry service or shared abstraction: hosts already know how to attach .NET
diagnostics or an OpenTelemetry-compatible bridge, while FluxFlow should not
own exporter choice or configuration.

Metric tags are a bounded semantic contract. Application, message, tracing,
lease, payload, provider, connection, path, exception, and credential data are
excluded to protect privacy and cardinality. Safe trace identity remains an
activity attribute only. Listener exceptions are isolated from durable work.

Status stores remain explicit read-only aggregate queries and are not polled to
produce metrics. Event counters and latency histograms explain live rates and
outcomes; status snapshots explain current backlog. Neither changes the
at-least-once guarantee or host-owned destination idempotency.

## Host-owned durability operations example

Operational integration belongs in a sample host, not another FluxFlow runtime
abstraction. The durability operations sample composes the normal Generic Host,
existing flat registration callbacks, provider-neutral durability contracts,
and local SQL-file providers. FluxFlow gains no telemetry service, exporter,
health-check adapter, status scheduler, persistence wrapper, or application
option.

The host constructs and disposes the BCL listeners. Listener callbacks observe
only exact durability sources and reduce events to bounded semantic facts in
memory; they never perform provider or console I/O. A production host may
replace those listeners with its chosen OpenTelemetry-compatible bridge without
changing engine registration.

Status remains explicitly pulled. The sample makes one pre-start input query to
show queued state and two post-completion queries to show terminal input/output
state. Causal telemetry and handler signals—not sleeps or database polling—gate
the final snapshots. Temporary storage is host-owned and deleted only after the
host and listeners are disposed.

The sample listener uses one bounded observation map, one fixed required-key
set, and one completion signal. This is sample-local coordination, not a generic
telemetry abstraction. The host awaits that signal alongside the delivery
handler, preserving two clear scenario responsibilities and the exact output.

The sample handler demonstrates duplicate-key tolerance in memory, but the
guarantee remains at-least-once and real destination idempotency remains a host
responsibility. SQL-file storage is chosen only for a server-free example; the
same runtime/status boundaries support the existing T-SQL provider and future
providers.
