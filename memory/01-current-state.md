# Current State

Date: 2026-07-25

## Repository

- Repository: `D:\Projects\FluxFlow`.
- Active local branch: `work/canonical-vnext-cleanup`.
- The manifest contains 58 independently versioned packages.
- This cleanup is local only. No branch push, tag, package publication, pull
  request, or merge is part of the current program.
- `graphify-out/` is generated locally and excluded from git.

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
- Legacy document shapes are accepted only by explicit one-way Composition or
  Engine migrators. Registered component/resource type aliases normalize on
  input; persistence and Designer output always use canonical names.
- Ordinary configuration uses semantic processing profiles. Technical
  Dataflow capacities, parallelism, and ordering are mapped internally.

## Foundation And Runtime

- `FluxFlow.Data` `1.0.0` owns immutable `FlowValue`, exact-byte
  `FlowContent`, codecs, and `FlowResult<T>`/`FlowError` contracts.
- `FluxFlow.Nodes` `2.1.0` owns `FlowMessage<T>` trace, correlation, message,
  causation, header, and source lifecycle plumbing.
- `FluxFlow.Composition` `3.0.0` owns canonical loading, normalization,
  addressing, link compilation, component factories, fan-in coordination,
  code-first runtime ownership, and attempt-all aggregate cleanup.
- `FluxFlow.Composition.Hosting` `3.0.0` owns definition sources, immutable DI
  snapshots, hosted lifecycle, and transactional revision coordination.
- `FluxFlow.Engine` `3.0.0` owns canonical runtime preparation, resource and
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
- Canonical data boundaries use `FlowValue`, `FlowContent`, or explicit domain
  records where the domain contract is already reusable.
- Expected failures use normal `Output` result values. Canonical Composition
  registrations do not expose a universal `Errors` port.
- `Events` is the component diagnostic stream. `Completion` is lifecycle state,
  not workflow data.
- Mapping, Validation, Assertions, State, Sources, Timers, Observability,
  Payloads, Serialization, HTTP, FileSystem, Storage, Sessions, Expectations,
  Metrics, Projections, and Routing each have one maintained component path.
- Control Filter/When and Routing Switch/Fork/Merge structural nodes are
  removed; canonical links own graph structure.
- Routing Window/Correlation/Join retain their mature algorithms only as
  internal collaborators behind public FlowValue/result components.
- Component runtime packages with intentional public removals are on local
  `5.0.0` major versions. Composition adapters with removed public surfaces are
  on `3.0.0`; unaffected fixed adapters retain their existing `2.x` versions.

## MQTT

- `FluxFlow.Components.Mqtt` is `6.0.0` and remains one component family in the
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
- Core owns policy and lifecycle. MqttNet `2.0.0` and PulseMqtt `3.0.0` expose
  only provider transport factories/sessions over the neutral SPI.
- MQTT Composition `3.0.0` separates resource indexing, validation,
  conversion, registration, and component factories.

## DI And Ownership

- Standard DI, explicit `IServiceCollection` composition, keyed services, and
  exact resource addresses are the registration foundation.
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
- A fresh complete local source contains all 58 current packages. A fresh
  net8.0 consumer with 58 direct references restores from that source plus the
  public feed and builds in Release with zero warnings and zero errors.

## Verification

- Final cross-cutting sweep: Data 32, Nodes 41, Composition 109, Composition
  Hosting 29, Fluent 21, Engine 55, Designer 112, Configuration 40,
  FileSystem 43, HTTP 22, Timers 72, Routing 51, Routing Composition 13, and
  MQTT 48 passed with zero warnings.
- Final Release tests: 99 passed with zero warnings.
- Controlled Debug and Release solution confirmation builds each completed 129
  projects with zero errors and zero warnings.
- Routing and Routing Composition release preflight and local-source dry-runs
  passed. SDK checks against published `4.0.0` and `2.2.0` baselines reported
  only documented intentional removals.
- Earlier bounded family commits contain their focused test counts, package
  compatibility reports, preflight/dry-run results, and complete-source
  consumer evidence in numbered memory notes 243 through 260.
- The final requirement audit is recorded in
  `memory/261-canonical-vnext-cleanup-completion.md`; no cleanup blocker remains.

## Deferred Work

The cleanup does not implement supervision, polling/latest-value APIs, durable
mailboxes, broker clusters, automatic mapper insertion, custom containers,
cyclic graph execution, or hot-reload enhancements. Each requires a separate
plan and explicit behavior contract.

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
