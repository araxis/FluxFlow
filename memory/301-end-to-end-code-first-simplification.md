# End-To-End Code-First Simplification

Date: 2026-08-08

## Decision

FluxFlow's compiled-C# experience is one explicit declaration-to-execution
path. `ComponentContract` and `ApplicationResourceContract` are the code-first
authorities; their typed handles continue through definition links, runtime
ports, durability, and keyed resource binding. Portable JSON remains a separate
first-class source and never contains executable C# state.

## Changes

- `ApplicationPorts` and the low-level port runtime accept typed input,
  signal-input, and output handles for the operations that match their
  direction. They delegate to canonical address behavior and retain explicit
  `FlowMessage<T>` envelopes.
- Durable input enqueue accepts `InputPortHandle<T>` and durable output capture
  accepts `OutputPortHandle<T>`. No signal or direction-breaking shortcuts were
  added.
- Typed resource DI helpers accept `ResourceHandle<T>` without changing normal
  DI ownership.
- `ApplicationResourceContract` pairs one portable type/options projection and
  typed handle with an explicit resource registrar. The application builder
  commits the resource and contract atomically and stores exact contracts in a
  runtime-only definition collection excluded from JSON.
- Engine merges host and definition registrars by exact registration identity
  for each candidate revision. Conflicts fail before activation; rollback,
  retirement, host fallback ownership, and captured-closure release follow the
  existing revision boundary.
- MQTT broker, retry-policy, subscription, and client authoring use four
  complete resource contracts backed by one shared stateless registrar.
  Code-first definitions run without `.AddMqtt()`; JSON/configuration hosts
  continue to call `.AddMqtt()` explicitly.
- `FluxFlow.Fluent` retains `From`, `Then`, `Tap`, `Branch`, shared-node fan-in,
  segments, `Apply`, `To`, events, and lifecycle. It now authors canonical
  component contracts and links, owns a `FluxFlowApplication`, and exposes
  `FlowGraph.Definition` and `FlowGraph.Application` instead of a parallel
  runtime.
- Engine performs deterministic topological stage draining for acyclic
  definition graphs. Each stage drains only its own inputs after all upstream
  outputs have drained, then completes nodes and outputs. Cyclic signal graphs
  retain coordinated fallback shutdown.
- Raw/plugin registration moved to
  `AddFluxFlowComponents().Advanced.AddDynamicComponent(...)`. The old normal
  `AddRuntimeComponent` entry point has no forwarding alias.

## Preserved boundaries

- Canonical JSON remains exactly `Resources` plus `Workflows`.
- JSON deserialization creates no executable component/resource contracts.
- Host-owned services and external resources are never disposed by Engine.
- Component/event ports, typed predicates, hot reload, rollback, Designer
  metadata, MQTT semantics, stable addresses, durable schemas, leases,
  retention, and at-least-once guarantees remain unchanged.
- No reflection, scanning, global mutable registry, delegate serialization, or
  hidden package discovery was introduced.

## Verification

Focused Composition, Engine, Fluent, Fluent.Hosting, MQTT, DurableInput, and
DurableOutput suites cover the new public shape and lifecycle. The final gates
completed with:

- 134 Release-build projects, zero errors and zero warnings;
- 2,627 tests across 66 projects, zero failures, skips, or warnings;
- 179 dedicated Release tests, zero warnings;
- a real isolated package-consumer pack/run with every JSON, code-first,
  resource, Fluent, durability, restart, and completion marker exactly once;
- successful MQTT sample execution through both the explicit JSON registration
  path and the self-describing code-first path;
- clean public API baseline, formatter, whitespace, and direct/transitive
  vulnerability checks.

Exact commands, focused counts, named behavioral evidence, and package cleanup
proof are recorded in
`goals/2026-08-08-end-to-end-code-first-simplification/README.md` and
`.testagent/status.md`.
