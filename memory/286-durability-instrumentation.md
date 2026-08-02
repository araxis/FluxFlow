# Durability Instrumentation

Date: 2026-08-02

## Decision

Optional durable input and output publish event-driven metrics and activities
at their existing provider-neutral semantic boundaries. Each package owns one
small internal static instrumentation class built only on
`System.Diagnostics.Metrics` and `ActivitySource`:

- durable input source and meter: `FluxFlow.Engine.DurableInput`;
- durable output source and meter: `FluxFlow.Engine.DurableOutput`.

There is no telemetry service interface, dependency-injection registration,
options object, shared framework, exporter dependency, reflection path,
background worker, or status poller. SQL-file and T-SQL providers, schemas,
commands, migrations, settings, and tests remain unchanged.

## Exact Signals

Durable input exposes:

- consumer activity `fluxflow.durable_input.process`;
- counter `fluxflow.durable_input.leases.acquired` (`{lease}`);
- counter `fluxflow.durable_input.messages` (`{message}`);
- counter `fluxflow.durable_input.lease.renewals` (`{renewal}`);
- counter `fluxflow.durable_input.store.failures` (`{failure}`); and
- histogram `fluxflow.durable_input.processing.duration` (`ms`).

Durable output exposes:

- producer activity `fluxflow.durable_output.capture`;
- consumer activity `fluxflow.durable_output.deliver`;
- counter `fluxflow.durable_output.captures` (`{capture}`);
- histogram `fluxflow.durable_output.capture.duration` (`ms`);
- counter `fluxflow.durable_output.leases.acquired` (`{lease}`);
- counter `fluxflow.durable_output.handler.calls` (`{call}`);
- counter `fluxflow.durable_output.deliveries` (`{message}`);
- counter `fluxflow.durable_output.lease.renewals` (`{renewal}`);
- counter `fluxflow.durable_output.store.failures` (`{failure}`); and
- histogram `fluxflow.durable_output.delivery.duration` (`ms`).

Metric dimensions are restricted to bounded semantic `outcome`, `result`,
`operation`, and `failure.kind` values. They exclude application and message
identity, contracts, tracing identity, lease identity/ownership, payloads,
headers, exception text, providers, paths, connection details, and secrets.
Activities use static names and may carry only safe correlation attributes such
as `flow.trace_id`, attempt, and acknowledgement mode.

## Semantic Boundaries

Input lease metrics begin only after a store lease is validated. Message
outcomes are counted only after delivered, retry, or dead-letter settlement is
applied; renewal results preserve applied/rejected ownership semantics.

Output capture wraps serialization and the awaited store call exactly once.
Its accepted, conflict, caller-cancellation, and failure results preserve the
existing exception and dispatch behavior. Delivery begins only for a validated
lease and observes handler result, renewal, ownership loss, settlement, store
failure, and elapsed time without adding another queue or worker.

Every instrumentation entry point isolates host listener exceptions. With no
listener, BCL enablement checks avoid activity/tag/timestamp work where it is
material. Instrumentation never queries status or performs provider I/O.

## Guarantee And Operations Boundary

Metrics are transition/event counters and latency histograms; they are not a
durable backlog snapshot. `IDurableInputStatusStore` and
`IDurableOutputStatusStore` remain explicit read-only aggregate queries with a
caller-owned observation time. FluxFlow installs no exporter, health check,
cache, timer, dashboard, or automatic translation between the two boundaries.

At-least-once delivery, crash windows, settlement compare-and-set rules, and
host-owned destination idempotency remain unchanged. The instrumentation adds
no exactly-once, checkpoint, distributed-transaction, replay, retention,
transport, batching, or parallel-delivery behavior.

## Test And Verification Evidence

- The mandatory static source/test pairing analysis ran once: 759 production
  sources, 311 test sources, 528 paired, and 231 unpaired in 2,520 ms. This is
  filename/content pairing evidence, not runtime coverage.
- Before instrumentation, the durable-input suite passed 144 tests and the
  durable-output suite passed 162 tests in Release with zero warnings.
- Final focused filters passed input 10/10 and output 17/17 twice. Complete
  provider-neutral projects passed 154/154 and 179/179. SQL-file/T-SQL fast
  suites passed input 127/138 and output 166/136, all without warnings.
- Release governance passed 123/123. Both fresh core package/symbol archive
  pairs passed inspection, four touched project format gates passed, and the
  serialized Release build passed 133 targets with zero errors/warnings.
- Two consecutive complete Release sweeps each passed 2,486/2,486 tests across
  66 projects with zero warnings. Diff, whitespace, forbidden-pattern,
  privacy/cardinality, dependency, API/package, assertion-quality, and pseudo-
  mutation audits found no unresolved material issue.

## Recommended Next Step

After this bounded round is stable, add an operations example that shows a host
attaching its chosen diagnostics/OpenTelemetry-compatible bridge and combining
live rate/latency signals with explicitly scheduled status snapshots. Keep that
example outside runtime registration: FluxFlow should not choose an exporter,
poll interval, dashboard, or health policy for the host.
