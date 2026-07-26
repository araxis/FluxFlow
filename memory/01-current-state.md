# Current State

Date: 2026-07-27

## Repository

- Repository: `D:\Projects\FluxFlow`.
- Active local branch: `work/di-first-application-components`.
- The manifest contains 62 independently versioned packages.
- This refactoring is local only. No branch push, tag, package publication, pull
  request, or merge is part of the current program.
- `graphify-out/` is generated locally and excluded from git.
- The DI-first application and component simplification is complete locally.
  Standard `IServiceCollection` registration is the sole maintained public
  registration path; immutable descriptors and catalogs replace mutable
  registries, contributors, and transitional builders.

## Canonical Application Model

- `FluxFlow.Composition.Model.ApplicationDefinition` is the persisted model.
- The root contains exactly `Resources` and `Workflows`, both JSON objects.
- Resource, workflow, and component identity comes from object keys.
- Components appear directly below each workflow. There are no maintained
  `Configuration`, `Composition`, `Nodes`, or root `Links` wrappers.
- `ComponentDefinition` uses flat structural properties: `Type`, optional
  `Processing`, options, resource references, and input/output link
  declarations.
- `ApplicationAddress` is the single ordinal, case-sensitive address value:
  `Component.Port` is local, `Workflow.Component.Port` is cross-workflow, and
  `Resources.Group.Resource` addresses nested resources.
- Links can be a string, array, or `{ "Port", "Condition" }` object and may
  be declared on either endpoint. Fan-in, fan-out, conditional/default routes,
  and cross-workflow links compile to one canonical link model.
- Genuine message data cycles remain invalid. Links into explicitly registered
  signal ports are bounded feedback relations, permitting Ack/Nak/Cancel
  feedback without treating different message-port names as a cycle bypass.
- Legacy document shapes are accepted only by explicit one-way Composition or
  Engine migrators. Registered component/resource type aliases normalize on
  input; persistence and Designer output always use canonical names.
- Ordinary configuration uses semantic processing profiles. Technical
  Dataflow capacities, parallelism, and ordering are mapped internally.

## Foundation And Runtime

- `FluxFlow.Data` `2.0.0` owns exact immutable `FlowContent` bytes and the
  transport-neutral immutable `FlowError` contract. It no longer owns a
  universal value tree, result wrapper, or hidden content codecs.
- `FluxFlow.Nodes` `3.0.0` owns the closed `FlowMessage<T>` value-or-error
  envelope, strict JSON projection, trace, correlation, message, causation,
  immutable string headers, and source lifecycle plumbing.
- `FluxFlow.Coordination` `2.0.0` owns generic bounded pending exchanges,
  deterministic deadlines, exact-once terminal settlement, and duplicate/late
  feedback classification. Workflow coordination uses stable `TraceId` by
  default; generation-bearing operations include generation in the generic key.
- `FluxFlow.Resilience` `1.0.0` owns transport-neutral retry policies,
  schedules, budgets, state transitions, jitter sources, and direct execution.
- `FluxFlow.Composition` `5.0.0` owns canonical normalization,
  addressing, link compilation, component factories, fan-in coordination,
  code-first runtime ownership, and attempt-all aggregate cleanup.
- `FluxFlow.Composition.Hosting` `5.0.0` owns configuration loading, definition
  sources, revision planning, immutable DI snapshots, hosted lifecycle, and
  transactional revision coordination.
- `FluxFlow.Engine` `5.0.0` owns canonical runtime preparation, resource and
  component activation, stable ports, complete-link binding, direct port
  access, system events/diagnostics, runtime generations, and rollback.
- Component add, update, remove, and port-surface changes prepare isolated
  candidates, preserve the active revision on pre-commit failure, atomically
  publish successful generations, and drain old ownership after replacement.
- Stable inputs are bounded; outputs broadcast. Shared fan-in inputs complete
  only after every upstream succeeds and fault once on the first upstream
  fault.
- Diagnostic queues are bounded and best effort; accepted diagnostics preserve
  order. System events and accepted workflow data retain their stronger
  delivery contracts.
- Ordinary component failures remain local data. Unexpected implementation or
  lifecycle faults use `Completion` and do not define host lifetime.

## Component Model

- Standalone component packages remain usable without Engine or Composition.
- Canonical boundaries use typed CLR commands, events, results, snapshots, or
  exact `FlowContent`; explicitly schema-less JSON uses owned `JsonElement`.
- `FlowMessage<T>` carries exactly one value of `T` or one `FlowError`.
  Expected failures remain normal `Output` data and canonical registrations do
  not expose a universal `Errors` port.
- `Events` is the component diagnostic stream. `Completion` is lifecycle state,
  not workflow data.
- Mapping, Validation, Assertions, State, Sources, Timers, Observability,
  Payloads, Serialization, HTTP, FileSystem, Storage, Sessions, Expectations,
  Metrics, Projections, Routing, and Resilience each have one maintained
  component path.
- `FluxFlow.Components.Resilience` and its Composition adapter are `2.0.0`.
  Canonical `flow.retry` has Input/Ack/Nak/Cancel/Output/Events, preserves one
  logical TraceId, rejects stale attempts, and represents expected failures as
  normal `FlowMessage<RetrySignal>` output data.
- Control Filter/When and Routing Switch/Fork/Merge structural nodes are
  removed; canonical links own graph structure.
- Routing Window/Correlation/Join retain their mature algorithms as internal
  collaborators behind typed public components.
- FileSystem, Observability, Routing, Sessions, and Storage runtime packages are
  on local `6.0.1`; other migrated runtime component families remain on their
  `6.0.0` lines. The DI-migrated composition adapters are on major `5.0.0`
  lines, except Resilience Composition `3.0.0` and Payloads, Serialization,
  Metrics, and Projections Composition `4.0.0`.
- `FlowNode<TInput,TOutput>` owns one bounded processing block. Serialization
  and Timers retain dedicated pipelines only for their distinct delayed and
  completion-sensitive behavior.
- `FlowContent` has one deterministic versioned JSON representation. Storage
  and Sessions use it directly while preserving legacy stored-envelope reads.
- Designer `4.0.0` combines explicit presentation metadata with structural
  metadata from the same immutable `ComponentCatalog` used for activation.
- Mapping abstractions, Expressions, Control, Control Composition, Journal,
  and the BCL-only Resilience foundation remain unchanged because their public
  and dependency contracts were not affected.

## MQTT

- `FluxFlow.Components.Mqtt` is `7.0.0` and remains one component family in the
  general-purpose engine.
- Broker resources own endpoint and transport defaults. Logical client
  resources own identity, credentials, certificates, reconnect, autoconnect,
  desired subscriptions, and one shared client lifecycle.
- One `MqttClientController` is the public facade per logical client. Multiple
  clients may share a broker; multiple components may share one client.
- Canonical components are command, publish, receive, and client events.
  Commands accept discriminated requests and emit discriminated normal results.
- Exact payload bytes use `FlowContent`. Named and inline subscriptions,
  reconnect restoration, exclusive effective trigger ownership, overlapping
  filters, and payload-independent TraceId Ack/Nak are retained.
- Workflow acknowledgement is separate from broker acknowledgement.
- Workflow Ack/Nak pending state uses shared TraceId coordination. Broker
  acknowledgement aggregation remains MQTT-owned. Reconnect delay and budget
  planning uses shared resilience while MQTT retains classification and
  lifecycle ownership; configured jitter now uses a varying injectable source.
- Core owns policy and lifecycle. MqttNet `3.0.0` and PulseMqtt `4.0.0` expose
  only provider transport factories/sessions over the neutral SPI.
- MQTT Composition `5.0.0` separates resource indexing, validation,
  conversion, resource registration, and component factories through normal
  DI registration.

## DI And Ownership

- Standard DI, explicit `IServiceCollection` composition, keyed services, and
  exact resource addresses are the registration foundation.
- Immutable `ComponentDescriptor` and `ResourceTypeAliasDescriptor` singleton
  services are collected into a concrete revision-scoped `ComponentCatalog`.
  Family packages register them through `Add...Components()` methods.
- `ComponentCatalog` is authoritative for component type identity, aliases,
  typed ports, cardinality, and processing capabilities. Designer providers add
  presentation metadata without defining a parallel component catalog.
- `IApplicationResourceRegistrar` is the retained focused resource extension
  boundary. Mutable registries, general service contributors, registration
  builders, and delegate resource wrappers are removed.
- There is no reflection discovery, assembly scanning, custom container, or
  per-message service provider creation.
- Provider snapshots preserve host, resource-revision, workflow-revision, and
  external ownership boundaries. Externally supplied resources are explicitly
  non-owning.
- Concrete clients, stores, clocks, credentials, certificates, secrets, and
  transport lifetimes remain host or adapter owned as documented by each
  package.

## Compatibility And Release Readiness

- `eng/canonical-vnext-cleanup-ledger.json` records removed, migrated,
  internally consolidated, and retained-reviewed candidates.
- Public source-declaration baselines were regenerated only after reviewed
  removals. SDK package validation reports intentional major-version removals;
  no compatibility suppressions recreate the duplicate architecture.
- Package release notes, package READMEs, the top-level changelog, public API
  overview, component coverage matrix, and migration guides describe the
  current major-version boundaries.
- A fresh complete temporary source contains all 62 current packages. For the
  DI-first simplification, all 25 changed packages passed release preflight and
  package dry-run against that source plus the public feed. SDK comparison with
  preceding releases reported only expected major-version API diagnostics for
  24 packages; Fluent.Hosting remained binary-compatible.

## Verification

- Full solution sweep: 1,726 tests passed in 65 projects with zero failures,
  skips, or warnings. Focused component/runtime/composition suites also passed
  throughout the simplification.
- Final Release tests: 99 passed with zero warnings.
- Controlled Debug and Release solution confirmation builds each completed 137
  projects with zero errors and zero warnings.
- Public API baseline tests passed, production scans contain no legacy
  FlowValue/result/codec/error-port escape path, and the three-size benchmark
  supports typed CLR values plus explicit JSON conversion.
- Complete DI-first architecture, package, and verification evidence is recorded
  in `memory/265-di-first-application-component-simplification.md`.

## Deferred Work

The current architecture does not implement supervision, polling/latest-value APIs, durable
mailboxes, broker clusters, automatic mapper insertion, custom containers,
cyclic data-graph execution, or hot-reload enhancements. Gate remains a
separate future `control.gate` pass. Each requires a separate plan and explicit
behavior contract.

## Primary References

- `docs/19-vnext-runtime-architecture.md`
- `docs/20-flow-data-contracts.md`
- `docs/21-component-type-names.md`
- `docs/22-canonical-vnext-migration.md`
- `docs/23-engine-2-to-3-migration.md`
- `memory/256-composition-canonical-runtime-removal.md`
- `memory/257-engine-canonical-runtime-simplification.md`
- `memory/258-structural-control-routing-removal.md`
- `memory/259-mqtt-canonical-consolidation.md`
- `memory/260-routing-canonical-consolidation.md`
- `memory/262-coordination-and-resilience-refactoring.md`
- `memory/263-typed-flow-data-contract-simplification.md`
- `memory/264-framework-simplification-round-2.md`
- `memory/265-di-first-application-component-simplification.md`
