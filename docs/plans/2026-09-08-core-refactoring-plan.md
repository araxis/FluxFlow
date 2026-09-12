# Core simplification development plan

Date: 2026-09-08  
Status: implementation authorized on 2026-09-08; execution in progress.  
Scope: deep architectural refactoring, capability preservation, and local release preparation. No public publication is included.

Companion: [detailed implementation prompt](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/docs/plans/2026-09-08-core-refactoring-implementation-prompt.md). Evidence: [framework simplicity review](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/docs/reviews/2026-09-08-framework-simplicity-review.md).

## 1. Outcome and approval boundary

Make FluxFlow a small, understandable execution library whose visible structure is Application → Workflows → Components → Ports. Components wrap existing processing/client libraries and Dataflow blocks. Ordinary component creation, linking, and execution must not require learning a registration framework, an editor model, or a live-update engine.

The central requirement added after the review is **instance-first construction**:

```csharp
workflow.Add("publish", new MqttPublishOperationNode(controller));

// A component-owned factory is also acceptable when it adds useful defaults.
workflow.Add("publish", MqttPublisher.Create(client, options));
```

These are alternative proposed usages, not statements that the workflow API or `MqttPublisher` class already exists. The existing MQTT type is `MqttPublishOperationNode`; no blanket component rename is required. A component does not need both a constructor and a redundant static factory.

This plan supersedes the review's suggestion that a basic component always needs a registration. **Direct use needs no DI registration or catalog entry.** DI, portable configuration, and designer integration each add only the opt-in information they genuinely require.

The user explicitly authorized execution on 2026-09-08. The separate implementation prompt now governs the implementation, subject to subsequent user decisions. Public release and external consumer coordination still require separate authorization. See the [execution ledger](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/docs/plans/2026-09-08-core-refactoring-progress.md) for verified progress.

Success is not a shorter facade over the same machinery. The implementation behind it must lose duplicate runtimes, repeated declarations, mandatory activation wrappers, and misplaced responsibilities while retaining the features listed below.

## 2. Decisions and constraints

### Firm design constraints

1. One canonical inter-component envelope: non-generic `FlowMessage` carrying `FlowValue` or `FlowError`. Do not retain `FlowMessage<FlowValue>` as a second transport.
2. Framework-owned transport/event data use immutable records with defensively owned referenced state. Stateful components and runtime owners remain classes. A record alone is only shallowly immutable; payload ownership is the actual safety property. [Record semantics](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record#immutability).
3. Ordinary forwarding and fanout share the original message/value references. A transformation may create a derived message. Ingress ownership copies remain when required.
4. Components accept their actual dependencies in public constructors, or expose their own simple static creation method. No mandatory creator class, factory interface, activation context, descriptor builder, or nested callback ceremony on the direct path. The context-aware `Add` overload is deferred by the user's latest scope decision (section 5.1.1).
5. A workflow accepts a name and an instance. The name supplies graph identity/addressing; it is not automatically a processing dependency.
6. Dataflow owns queues, scheduling, cancellation, and block completion. Retain only necessary graph/delivery policy around it. No home-grown general pub/sub framework.
7. One execution path for direct C#, fluent authoring, JSON, and optional live updates. Typed C# processing is an edge convenience, not a second scheduler/port hierarchy.
8. Runtime, JSON authoring, fluent authoring, designer, and update mechanics have enforceable dependency boundaries.
9. Preserve named workflows, ports, conditions, request/reply, module features, lifecycle, durability, and consumer observability. Relocation is allowed; silent removal is not.
10. Prefer small concrete classes and explicit dependencies. Interfaces are justified by a real substitution boundary. Small guards and closed enum checks are acceptable; replacing every `if` with a strategy class is not the goal.
11. Breaking APIs are acceptable. Do not maintain permanent legacy aliases or duplicate implementations merely to preserve source compatibility.
12. Do not upgrade unrelated dependencies, redesign storage protocols, or introduce a new plugin framework as part of simplification.

### Proposed defaults to review with this plan

| Decision | Proposed default | Consequence |
|---|---|---|
| Ordinary workflow links | Bounded, reliable admission with awaitable backpressure | Full targets no longer silently behave like an optional rejection route |
| Isolation/drop behavior | Explicit per-route policy, preserving current reject-on-full use cases | Existing intentional isolation tests move to that policy |
| Request identity | Unique exchange correlation independent of trace identity | Several concurrent requests may share a trace safely |
| Component ownership | Successful direct `Add(name, instance)` transfers ownership to the graph | Failed addition leaves ownership with the caller |
| DI-resolved component | Explicit borrowed ownership; its scope disposes it | Graph stops/drains it but does not double-dispose it |
| Topology | Built and validated before ordinary startup | Runtime link mutation/revisions remain explicit optional capabilities |
| Package shape | One cohesive Core; separate JSON and Fluent projects | Reuse real optional module/provider boundaries; avoid assembly proliferation |
| Persistent schema | Preserve current flat conventions unless a proven conflict requires migration | No gratuitous casing, wrapper, or address-format rewrite |

Three issues need explicit resolution during the API checkpoint, not silent assumptions:

- Does the earlier word “loop” mean a workflow graph or actual bounded iteration/feedback? Retain current cycle rejection until an execution contract is approved. Arbitrary cyclic Dataflow links are not an automatic feature of this refactor.
- A template mapper was requested earlier but no Scriban implementation was found. Track it as a proposed new optional capability, not an already-existing feature. Its inclusion and public rendering contract need confirmation before implementation.
- Confirm the final public package/assembly map before source moves. `FluxFlow.Core` and `FluxFlow.Json` below are proposed destination names; do not rename every module simply for symmetry.

These decisions do not prevent preparing the plan. An executing agent must stop only at the affected boundary if a material choice is still unresolved; it may complete independent approved work.

## 3. Current evidence and scope map

The review measured 62 source projects and 750 C# files. It deeply traced selected paths, not every module implementation. The existing graph index covers `src/Core` only. Neither those counts nor graph degree is a runtime benchmark or a proof of design quality.

| Finding | Starting locations | Required disposition |
|---|---|---|
| F1: split request/reply | Engine `Ports/ApplicationPortRuntime.cs`, `ApplicationOutputSubscriptions.cs`; Coordination `PendingExchangeCoordinator.cs`; RequestReply module | Consolidate pending exchanges; remove typed-port restriction and trace-only matching |
| F2: delivery changes by path | Nodes `FlowOutput.cs`; Engine `Ports/FlowValueApplicationOutputPort.cs`, `ApplicationInputPortCore.cs` | One explicit contract for direct and application links |
| F3: dual runtime | `FlowNode.cs` / `FlowValueNode.cs`, `FlowSource.cs` / `FlowValueSource.cs`, typed/canonical port families | Keep one canonical execution implementation |
| F4/F7: construction and designer coupling | Composition `Registration` / `Factories`; 19 component composition projects | Direct instance addition; optional local JSON binding; no ordinary Designer dependency |
| F5: model overlap | Composition `ApplicationModel`, `Authoring`, `ApplicationLinks`; Authoring projects | Portable document outside Core; one executable graph |
| F6: mandatory revision machinery | Engine `Hosting/ApplicationRuntimePreparation.cs`, activator, `Internal/Revisions` | Normal execution independent of candidate/swap/rollback machinery |
| F8: overlapping pumps | Nodes `FlowOutput.cs`; Engine `Core/FlowFanoutSource.cs`, application ports | Dataflow-based delivery with one small, tested fanout policy |
| F9: telemetry semantics | Engine `Signals/ApplicationRuntimeInstrumentation.cs`, Composition `Events/ApplicationEventStream.cs` | Actual operation spans; bounded metric dimensions; events remain available |
| F10: expression allocations | Composition `ApplicationConditionCompiler.cs`; Mapping context; expression adapters | Compile once, reduce duplicate bindings/conversions, retain native configuration |
| F11: feedback ambiguity | `ApplicationLinkCompiler.ValidateAcyclic` | Preserve safety until a separately explicit iteration contract exists |

The construction problem is concrete: [RuntimeComponentRegistrationBuilder](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/Core/Application/FluxFlow.Composition/Registration/RuntimeComponentRegistrationBuilder.cs:6) retains factory modes, activation wrapping, and several binding collections. In contrast, [JsonParseNode](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/Components/Serialization/FluxFlow.Components.Serialization/Nodes/JsonParseNode.cs:11) and [MqttPublishOperationNode](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/Components/Mqtt/FluxFlow.Components.Mqtt/Nodes/MqttPublishOperationNode.cs:10) already demonstrate ordinary constructor-based runtime components. Preserve that strength and make application composition equally direct.

## 4. Target dependency structure

Arrows mean project dependencies, not message flow.

```text
Direct C# consumer ────────────────────────┐
Fluent authoring ──────────────────────────┼──> Core
JSON authoring ────────────────────────────┘      Application
                                                  Workflows
Component modules ────────────────────────────>   Components
       └── existing processing/client libraries   Ports / messages / request-reply

Optional host integration ──> Core + DI / hosting setup
Optional updates ──────────> Core graph and lifecycle
Optional durable delivery ─> Core ports + provider-specific stores
Optional expressions ─────> small mapping contracts + chosen evaluator
Optional designer ────────> portable documents / structural metadata
```

Core owns immutable transport, small block-based components, named graph/address ownership, link semantics, lifecycle, one exchange coordinator, and standard instrumentation points. Workflows are named ownership scopes, not extra schedulers. Application delegates to cohesive collaborators; it does not implement every capability itself.

Core has no dependency on workflow JSON documents/converters, fluent builders, editor types, evaluator-specific options, transport clients, durable providers, or update transactions. **Using `System.Text.Json` inside `FlowValue` is allowed**: canonical payload representation is not the same concern as parsing an application configuration. Do not add an abstraction merely to hide `JsonElement` from a value intentionally designed around it.

Keep one portable document model and one executable graph. The JSON package owns document parsing, serialization, configuration validation, and trusted type bindings. The Fluent package supplies readable graph/definition construction without owning execution. It may offer portable-document extensions through an explicitly optional integration surface; Core and the basic fluent path must not acquire a mandatory JSON-authoring dependency. No independent fluent runtime or third application model.

Before moves, produce a table for every current source/package project: retain, merge into a stated destination, move responsibility, or retire after migration. Include package IDs, supported target frameworks, project references, manifest aliases, samples, tests, and downstream dependencies. Preserve existing framework targets unless separately approved.

## 5. Public API acceptance sketches

All code in this section is a design sketch. Phase 1 must turn the accepted sketches into compile-checked examples before mass migration. Reuse reasonable existing names instead of implementing every illustrative name literally.

### 5.1 Direct composition is the primary experience

```csharp
await using var application = new FlowApplication("orders");
var main = application.AddWorkflow("main");

var source = new OrderSource(sourceOptions);
var normalize = new NormalizeComponent(normalizer);
var publish = new MqttPublishOperationNode(controller);

main.Add("source", source);
main.Add("normalize", normalize);
main.Add("publish", publish);

source.Output.LinkTo(normalize.Input);
normalize.Output.LinkTo(publish.Input, message => IsPublishable(message));

await application.StartAsync(cancellationToken);
await application.DrainAsync(cancellationToken);
```

The controller is supplied by the caller; this example does not transfer ownership of that dependency to the publisher. Sources must be wired before they start producing. Application drain first stops ingress, then drains accepted work.

`main.Add("publish", MqttPublisher.Create(client, options))` is equally acceptable. Do not transform it internally into the existing creator-builder-factory-mode pipeline. An optional factory returns the actual component, not another builder that eventually creates it.

Graph registration must derive bindings from the component's small port contract. Standard wrapper implementations declare Input/Output/Events once. A source, sink, multi-input, or multi-output component declares only its actual ports; it must not create dummy ports to satisfy a universal shape. Custom components must be implementable through a small interface/composition path without inheriting a large mandatory base class.

If native `ISourceBlock`/`ITargetBlock` views are exposed, honor their full protocol or expose the actual supporting block. Do not claim reservation/consumption semantics through a partial imitation. Link ownership and graph shutdown must also account for directly created links; do not require a second hidden registration of the same connection.

### 5.1.1 Deferred idea: context-aware construction

The user clarified that this was an optional idea, not a requirement. It is deferred from the current refactor: do not implement or prototype it, and do not make it an acceptance gate. The following notes preserve the discussion for a future concrete need; their callback and cleanup requirements apply only if the idea is separately reintroduced.

`application.AddWorkflow("main")` names the workflow; `main.Add("publish", ...)` names a component inside it. Sometimes construction needs that graph-supplied identity. Permit a small optional overload alongside direct instance addition:

```csharp
main.Add("audit", context =>
    new AuditComponent(store, context.ComponentAddress));

// A component may instead explicitly accept the small context itself.
main.Add("audit", context => AuditComponent.Create(store, context));
```

These are alternative illustrative calls, not two registrations of the same name. Conceptually the overload accepts `Func<ComponentContext, TComponent>` and returns/registers the actual component through the ordinary path. Prefer one small immutable `ComponentContext` over introducing ApplicationContext, WorkflowContext and ComponentContext layers solely for this overload. It carries only construction-time application identity, workflow identity and component address; final names are settled at the API checkpoint.

Keep this context data-only: no `IServiceProvider`, `GetService`, configuration binding, component catalog, mutable graph, subscription methods or general state bag. Normal dependencies remain explicit constructor arguments captured from the composition root. Passing only the needed field is preferable when the component does not need the full context. Message trace/correlation and per-execution cancellation are not construction identity.

Validate the name and graph state before invoking the synchronous callback, invoke it once during that `Add` construction attempt, and register the result through the same binding/lifetime implementation. Do not retain it as a second runtime activation plan, call it per message, retry it implicitly, or require it for components that already construct directly. Async external work still belongs in startup.

Unlike caller-supplied instances, a newly constructed graph-owned result must not leak if subsequent binding fails. Use the graph's existing owned-resource cleanup, including awaited async cleanup at the ownership boundary; do not introduce blocking disposal. Borrowed results keep their external owner. A throwing constructor/callback is responsible for resources it acquired but never returned. Test these failure paths in the API slice.

This historical sketch does not widen the current scope or any component constructor.

### 5.2 DI remains ordinary and optional

```csharp
services.AddScoped<NormalizeComponent>();

// In a composition root, with a scope that outlives the graph:
var normalize = scope.ServiceProvider.GetRequiredService<NormalizeComponent>();
main.Add("normalize", normalize, ownership: ComponentOwnership.Borrowed);
```

The optional ownership argument is a proposed minimal escape hatch, not a new factory system. Directly constructed graph-owned components and DI-owned borrowed components must not share an ambiguous disposal path. The container owns the components it creates; components do not dispose borrowed injected clients. [DI lifetime guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines).

For multiple differently configured instances, the composition root may call constructors with shared dependencies and transfer the resulting instances to the graph. Avoid resolving disposable transients from the root provider indefinitely. Resolve named port views once through an application-owned lookup or keyed DI adapter. Do not mutate a built service collection to simulate runtime graph updates.

### 5.3 JSON activation is a narrow optional boundary

A JSON document cannot contain a live client or a delegate. It needs a trusted type identifier mapped to a local constructor call. A simple registration may therefore look like:

```csharp
jsonComponents.Register<PublishOptions>("mqtt.publish",
    (options, services) => new MqttPublishOperationNode(
        services.GetRequiredService<IMqttClientController>(),
        options.MaximumPendingRequests));
```

This is allowed **only in the JSON/host composition boundary**. It is one binding, not an `IComponentCreator` implementation per component, not a factory-of-factories, and not a required step for direct use. If a component-owned `Create(options, dependencies)` method already fits, use it here.

No arbitrary CLR type loading from configuration. Validate unknown types/options/resources and structural ports before starting external work. Do not construct disposable components just to inspect their ports. Standard port contracts can be reused statically; non-standard structural declarations live locally once and feed validation/binding. Designer hints decorate these facts rather than redefine them.

Constructors and DI factories perform cheap synchronous initialization. Network connection, subscriptions, and other asynchronous startup belong in explicit lifecycle methods. A genuinely asynchronous component-owned factory may be offered when needed, but is not mandatory for all components. Partial construction/start failure must release only newly owned resources.

### 5.4 Fluent authoring should read like its saved document

Preserve flat component properties and local addresses. For example, the portable C# surface may extend the document itself:

```csharp
var document = new ApplicationDocument("orders");
var main = document.AddWorkflow("main");
main.Add("source", new ComponentDocument("sample.source")
    .Set("messages", new[] { "alpha", "beta" }));
main.Add("normalize", new ComponentDocument("sample.normalize"));
main.Output("source").LinkTo(main.Input("normalize"))
    .When("input != null");
```

The matching part of a proposed saved document, retaining existing casing/link conventions:

```json
{
  "Workflows": {
    "main": {
      "source": {
        "Type": "sample.source",
        "messages": ["alpha", "beta"],
        "Output": { "Port": "normalize.Input", "Condition": "input != null" }
      },
      "normalize": { "Type": "sample.normalize" }
    }
  }
}
```

The expression engine is resolved from host/application/workflow configuration. Document construction does not construct runtime components; loading it calls the registered constructors and builds the same Core graph as direct C#.

Keep `LinkTo` and `LinkFrom` as symmetric authoring conveniences with one link representation and one evaluator path. Native C# `When(predicate)` requires no expression engine. Exporting a native delegate must fail clearly unless explicitly replaced by a registered portable expression; never serialize a weakened graph.

Avoid a separate options-builder class for every component when its options record is already readable. Strongly typed module helpers are optional local extensions over the same document/graph facts, not a central list that must be edited for every new component.

## 6. Runtime contracts to settle before replacement

### Messages and event data

- Enforce payload/error exclusivity, immutable headers, stable message identity, causation, exchange identity, timestamp, and transportable trace context.
- Adopt the requested record-based transport language, including a deliberate `FlowValue` record/equality design. Do not introduce implicit deep-JSON equality, hashing, or cloning on the hot path. Document how separately ingested equivalent JSON values compare; routing never relies on payload equality.
- Preserve detached JSON ownership, exact binary-content behavior, serializer extensibility, null/error cases, and local typed materialization. Mutable caller DTOs are snapshotted at ingress; opaque pooled memory may not escape without ownership guarantees.
- Events, commands, requests, and replies share the canonical envelope. Optional typed event records do not require one new CLR subtype for every dynamic JSON event.

### Delivery and links

- State separately what producer acceptance, target admission, processing completion, and durable acknowledgement mean. A successful send is not automatically completed processing.
- Reliable fanout sends the same reference to every selected target and awaits each admission. Offer to independent siblings without waiting for the first slow target before offering to the others; overall pressure still applies. Partial delivery cannot be rolled back, so retrying a whole fanout may duplicate side effects.
- Evaluate a small bounded ActionBlock-backed dispatcher before replacing custom pumps. Keep one implementation, reuse Dataflow scheduling, and measure it. BroadcastBlock is appropriate for latest/best-effort observation, not reliable delivery of every value to a bounded slow subscriber. [Block contract](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.dataflow.broadcastblock-1?view=net-10.0).
- Specify ordering, capacity, parallelism, predicate failure, zero matching links, full/faulted targets, unlink, `MaxMessages`, and link snapshots. Retain supported behavior explicitly or document an intentional breaking policy change.
- Default unmatched data to a visible routing failure unless an explicit discard/fallback link exists. A rejected head message must not stall the pipeline indefinitely. Do not recreate filtering infrastructure already provided by link predicates.
- Completion through fan-in waits for all required upstream producers. Blindly propagating the first upstream completion is insufficient. Faults and cancellation unblock sends and release waits.
- Runtime link changes belong to an explicit capability using the same kernel; they are not silently deleted when ordinary topology is frozen.

### Request/reply

- One bounded pending-exchange implementation, preferably simplifying/reusing existing Coordination logic. No general event-subscription broker to implement each request.
- Register the waiter before sending; use a unique correlation/exchange key and validate the intended reply endpoint/type/shape. Multiple requests within one trace must remain independent.
- Preserve correlation through transformations. MessageId is per message; CausationId is the immediate predecessor, not a replacement for exchange identity.
- The deadline covers admission and reply waiting. Clean up on rejection, exception, timeout, cancellation, shutdown, or reply; settle locally once. Late/duplicate replies cannot complete another request. Attempts must be distinguishable when retry semantics require it.
- Canonical requests and replies use the same service; typed overloads only serialize/materialize at the boundary. First reply is the default. Multi-reply aggregation remains a separate explicit capability.

### Lifecycle, updates, and resource ownership

- Configure/link → validate → start sinks/processors/resources → start sources. Stop ingress → drain accepted work → complete outputs → dispose owned resources. Forced cancellation/fault is a separate path.
- No constructor performs hidden network work. No synchronous blocking over asynchronous disposal in normal async ownership paths.
- One component instance has one graph owner. Reject accidental reuse under different names/graphs; use explicit shared services instead. Port views and borrowed dependencies never acquire ownership implicitly.
- Optional live updates own candidate preparation, admission handoff, generations, stable address proxies, drain, rollback, and cleanup. They use Core's graph contract, not another runtime. Do not claim selective instance reuse unless implemented and tested.

### Observability and evaluator boundaries

- Instrument actual send/process/transport operations centrally with standard tracing, metrics, and logging. Carry real parent context across queues, create appropriate links for fan-in, and avoid allocating rich diagnostic payloads when disabled. [Tracing guidance](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs).
- Keep request/message/trace IDs out of metric dimensions; use bounded component/operation/status dimensions. Host applications own SDK configuration, sampling, exporters, dashboards, and report storage.
- Retain an application event stream composed from component event ports. Label best-effort observation honestly. Exact audit/business events must have an explicit reliable/durable route; diagnostics are not their replacement.
- Preserve JSONata and C# expression adapters, application defaults, workflow overrides, named engine selection, portable parameters, and host-native configuration. Compile during graph preparation; separate immutable bindings from per-evaluation data. Do not invent a universal engine-options hierarchy.
- Keep template rendering separate from boolean conditions. Treat trusted host functions/types as a security boundary; cancellation of a task does not sandbox arbitrary synchronous user code.

## 7. Feature-retention ledger

Phase 0 expands this table to every public component and source package. Each row must end as migrated and verified, or explicitly approved as a changed contract. Unexplained deletion is not simplification.

| Capability | Target owner | Required proof |
|---|---|---|
| Canonical values, errors, binary content, typed conveniences | Core | Ownership/identity/materialization tests |
| Sources, transforms, sinks, multi-input/output, signals | Core wrapper contracts + modules | Representative standalone components, not dummy universal ports |
| Named application/workflows/addresses and cross-workflow links | Core | Direct, lookup, and keyed-DI parity; collision validation |
| Conditions, both link directions, fallback and isolation policies | Core predicates + authoring/adapters | Topology and delivery parity across authoring routes |
| Request/reply and correlated stateful patterns | Core exchange primitive + specialized modules | Canonical concurrency, retries, races, bounded state |
| JSON, fluent, resources and input-shape validation | Optional authoring/host boundary | Round trips, validation-before-start, constructor-once behavior |
| MQTT, HTTP, files, serialization, storage, sessions | Component families | Preserve public operations, cancellation, domain errors and library delegation |
| Routing, joins, windows, timers, retries, projections, assertions and remaining families | Component families | Existing behavior mapped to canonical runtime without central type switches |
| JSONata / C# engines and native host configuration | Optional evaluator adapters | Scope isolation, compilation, result typing, failure behavior |
| Template rendering | Proposed optional adapter | Scope decision first; separate rendering contract/tests if approved |
| Live updates and dynamic links | Optional update layer | Stable addresses, in-flight work, drain/rollback/disposal tests |
| Durable intake/output and providers | Optional delivery packages | Recovery, leases, deduplication, acknowledgement, retry/dead-letter/replay conformance |
| Logging, tracing, metrics, event observations, health | Standard instrumentation + optional host/health integration | Real context, bounded tags, disabled overhead, existing health semantics |
| Designer documents, layout and metadata | Optional tooling | Round trips and runtime independence |
| Dashboards, report archives and query/filter UIs | Consumer responsibility | Live data remains usable; no new reporting engine in Core |
| Actual iteration/feedback | Unresolved requirement | No claim of support until limits/termination/drain semantics are approved |

## 8. Ordered development phases

These are dependency gates, not calendar estimates. Do not migrate the entire repository before proving the component API and delivery semantics.

### Phase 0 — establish the migration baseline

Tasks:

1. Snapshot current git status/diffs and record pre-existing changes without reverting, staging, or committing them. Protect frozen release directories and generated historical artifacts.
2. Inspect solution, all source/package projects, test routing, manifests, public API checks, and consumer fixtures. Update the current-to-target package/feature ledger.
3. Run a fresh controlled Release build and available existing suites. Separate existing failures/warnings from new failures. Record discovered/executed/skipped test counts; zero selected tests is not a pass.
4. Capture reproducible current runtime measurements for the agreed representative workloads. Do not reuse old passing counts as current evidence.
5. Resolve public package placement, ownership/delivery defaults, record/equality contract, and any new-feature scope decisions.

Deliverables: baseline evidence, migration ledger, exact project map, short architecture contract. Exit: enough evidence to compare behavior and overhead honestly; no production refactor yet disguised as baseline collection.

### Phase 1 — prove the instance-first kernel slice

Tasks:

1. Consolidate the minimum canonical envelope, block wrapper, port contract, graph owner, addressing, and exchange primitive needed for one runnable slice.
2. Compile the direct-constructor and component-owned-factory examples. Demonstrate a standalone custom component without DI, metadata, or an external creator. Do not add the deferred context-aware overload.
3. Build source → transformation → conditional two-target fanout → reply sink. Include an error route, borrowed resource, and a second workflow/address lookup.
4. Add the smallest JSON and fluent paths for the same scenario, outside Core. They must build the same kernel, not simulate parity by comparing JSON only.
5. Implement focused conformance tests for reference fanout, slow targets, canonical same-trace request/reply, lifecycle, and direct/JSON constructor counts.
6. Examine the actual code path and allocation baseline. Remove prototype-only abstraction experiments that did not earn their place.

Deliverables: runnable examples, focused tests, before/after path diagram, proposed public API. Exit: the full path can be read without creator/factory-mode/editor/revision knowledge. **Review checkpoint:** show this slice before broad migration; proceed after implementation authorization unless the API materially departs from the approved plan.

### Phase 2 — consolidate runtime semantics

Tasks:

1. Replace duplicated typed/canonical node, source, port, and observation execution with the canonical implementation; keep only thin typed conversions.
2. Consolidate fanout/admission onto Dataflow-backed policy. Implement reliable and explicit reject-on-full routes, consistent predicates, failure/fallback, completion, unlink, and cancellation.
3. Route every request/reply entry point through the one bounded coordinator. Remove trace-only waiter matching and concrete typed-output assumptions.
4. Make normal startup/drain independent of revisions. Expose only the small lifecycle/address capabilities the update layer needs.
5. Move operation instrumentation to these actual boundaries without making every component duplicate telemetry code.

Deliverables: one runtime path and coordination path, lifecycle/concurrency tests, old-to-new API migration entries. Exit: no migrated consumer uses an old runtime path; no hidden old engine underneath the new facade. Temporary migration scaffolding has an explicit deletion owner and cannot ship.

### Phase 3 — finish authoring and construction boundaries

Tasks:

1. Move application-document JSON conversion/validation to its own project. Keep Core graph types free of workflow-serialization attributes and executable objects out of portable documents.
2. Move fluent builders to their proper project; preserve flat syntax, `LinkTo`, `LinkFrom`, `When`, resource declarations, and scoped conditions.
3. Replace mandatory runtime registration builders with instance registration and local optional configuration bindings. Collapse repeated option/port facts.
4. Remove normal registration/authoring dependencies on Designer in all 19 composition packages; place visual metadata behind optional tooling references.
5. Preserve existing document formats where practical; document and fixture-test any necessary breaking schema changes. Never drop a delegate/condition during export.

Deliverables: independent package boundaries, JSON/fluent parity tests, simple component-authoring guide. Exit: direct Core/module consumer restores/builds without JSON authoring, Designer, or an evaluator package; portable authoring does not require live component creation for schema inspection.

### Phase 4 — migrate component families

Order:

1. Serialization and the component template: smallest proof of authoring cost.
2. MQTT publish/trigger/client and HTTP: expose async startup, shared resource ownership, cancellation, and protocol correlation early.
3. Mapping/routing/validation/assertions and other stateless families.
4. Stateful families: timers, sessions, joins/windows, retries, projections, state, expectations, storage/file operations, and every remaining manifest family.

For each family: preserve the processing library and domain logic; migrate ports/envelopes; remove redundant wrappers/registration facts; update tests/samples/docs; run focused conformance; record API migration and package dependencies. Adding a new third-party component must require no Core, central enum, switch, or master authoring-class edit. Normal DI/module registration is still explicit.

Deliverables: complete component ledger, revised template, representative examples for standard and unusual ports. Exit: all families accounted for, not just the easy sample. No forced serializer/materializer class for trivial local DTO access, and no large base class holding unrelated family behavior.

### Phase 5 — integrate optional advanced capabilities

Tasks:

1. Reattach live updates using Core graphs. Preserve stable address behavior, in-flight ownership, admission transitions, drain deadlines, candidate rollback, and cleanup.
2. Migrate durable intake/output and each provider. Preserve store formats where possible; if schema changes are unavoidable, supply migration/recovery tests and obtain approval for destructive alternatives. Preserve acknowledgement, leases, deduplication, retries, replay, and dead letters.
3. Finish evaluator adapters and eliminate proven duplicate per-evaluation conversion work; preserve host-native options and trust rules. Implement the optional template adapter only if its inclusion was approved.
4. Verify event stream, host integration, health, standard logging/tracing/metrics, and designer persistence independently of Core.
5. If iteration was approved, implement it as a bounded, explicit feature with termination/cancellation tests; otherwise keep current cycle validation and state the limitation.

Deliverables: optional-package conformance results and persistence/operational migration notes. Exit: ordinary execution has no dependency on these capabilities; enabling them does not introduce another delivery runtime.

### Phase 6 — close the refactor and prepare a package candidate

Tasks:

1. Delete only obsolete migrated source/project scaffolding with a recorded replacement. Remove stale references, old examples and accidental compatibility paths. Do not delete history or unrelated artifacts.
2. Run architecture checks, the full non-empty test matrix, realistic integrations, repeated concurrency tests, and performance comparisons on all supported target frameworks.
3. Review API changes and deliberate major-version/package-map decisions. Update manifest, dependency constraints, readmes, migration docs and API baseline together; do not disable release validation to obtain a pass.
4. Pack a coherent, uniquely versioned local candidate feed. Test actual package consumption with a fresh isolated package cache so old artifacts cannot mask missing references.
5. Run repository package-consumer acceptance and restart/durability cases. Prepare a consumer migration brief with feed path, exact versions, API/JSON changes, known limits, and commands.
6. With separate authorization to coordinate that task, ask **FluxMQ-NG**, not the original FluxMQ project, to consume and test this candidate. If authorization/access is absent, report internal readiness and the outstanding external acceptance gate; do not call it release-ready.

Deliverables: acceptance evidence, before/after architecture and performance report, local package feed, migration brief, remaining-risk list. Exit: all approved capabilities verified and external consumer acceptance obtained where required. **Stop before public publication**, tags, pushes, or release creation; those require a subsequent explicit decision.

## 9. Verification matrix and measurable gates

Use the repository's current test framework and focused testing skills during implementation. Prefer deterministic synchronization and injected clocks over sleeps.

| Area | Required cases |
|---|---|
| Construction | `new` and useful static factory; zero direct registration; rejected instance addition leaves ownership with the caller; successful addition respects owned/borrowed lifetime; no network I/O before start; invalid JSON does not activate components |
| Extension | Custom standard, source, sink, and multi-port components added without central edits; DI optional; no Designer reference |
| Ownership | Add success/failure, borrowed DI component, shared client, partial startup failure, repeated stop/dispose, rejected double ownership |
| Values | Caller mutates original DTO/headers/bytes; original JsonDocument disposed; null/error; exact binary round trip; derived identity; reference-identical pass-through/fanout |
| Links | Both directions, native/named condition parity, no match/fallback, predicate exception, full/faulted/slow/unlinked target, ordering, link limits, partial fanout failure |
| Completion | Multiple producers, fault/cancel, blocked admission, drain timeout, source stop, no accepted work stranded silently |
| Exchanges | Canonical success; many requests on one trace; reply before send returns; timeout/reply/cancel races; full admission; duplicate/late/wrong reply; retry attempt; shutdown; bounded pending state |
| Authoring | Direct/fluent/JSON equivalent output/errors/events; local/cross-workflow addresses; export rejection of delegates; duplicate names/types; unknown options/resources/ports; round trips |
| Engines | Application/workflow scope; parameters isolated; native configuration retained; compile once; non-boolean result/error; concurrent evaluations; trusted host-function limitations |
| Optional operations | Revision swaps/rollback/drain; durable restart/dedup/leases/replay/dead letters; health; designer round trip; real protocol integrations |
| Telemetry | Parent context across queues/transport; fan-in links; error span boundaries; bounded metric tags; disabled path; slow/failing observer isolation |
| Packaging | All manifest projects/targets; no stale package cache success; coherent dependency closure; package-only consumer and restart acceptance |

Structural hard gates:

- Zero mandatory creator/activation descriptors for direct component construction and registration.
- Zero parallel typed/canonical transport runtimes, hidden second fanout kernel, or duplicate pending-exchange implementation.
- Zero Core references to workflow JSON authoring, fluent, Designer, evaluator implementations, transport clients, durability providers, or revision transactions.
- Zero ordinary module registration dependencies on Designer.
- One source of structural port facts; optional editor metadata cannot change runtime behavior.
- No per-message DI resolution, application-address parsing, reflection discovery, or re-compilation of unchanged expressions.
- One component implementation plus direct `Add` is sufficient for a custom direct component. Configuration binding is an optional additional local step.

Performance gates:

- Baseline and candidate use the same machine, framework, payload, topology, capacity, parallelism and telemetry settings; compare after warmup and repeat runs.
- Workloads: 1/8/32 hops; 1/2/8 fanout targets; small/large structured and binary payloads; conditions off/native/each adapter; telemetry off/on; slow consumers; saturation; cancellation; update swaps.
- Record throughput, p50/p95/p99 latency, allocated bytes/message, retained heap, queue depth and pending exchanges. No payload-size-proportional copy per fanout branch. Retained state must stabilize under sustained bounded load and return toward baseline after drain.
- Determine numeric latency/throughput/allocation budgets from Phase 0 and the consumer workload. Do not invent a percentage improvement or accept an unexplained statistically meaningful regression. A necessary reliability tradeoff must be measured and explicitly reviewed.
- Track deleted duplicate implementations, runtime dependencies, authoring files touched per added component, and actual hot-path stages. Lines of code alone are not a gate and must not be gamed by compressed code.

## 10. Existing verification entry points

These commands are for the future implementation run. They were inspected, not executed for this planning request. Re-check paths/parameters after package moves.

```powershell
rtk proxy dotnet build FluxFlow.sln --configuration Release
rtk proxy pwsh -NoProfile -File eng/test.ps1 -Suite Unit -Configuration Release -NoBuild
rtk proxy pwsh -NoProfile -File eng/test.ps1 -Suite Acceptance -Configuration Release -NoBuild
```

`eng/test.ps1` also has `Integration` and `Smoke` routes. The `All` route does not include external Smoke. Container, external-service, and licensed-database tests need their actual prerequisites and permissions; do not silently skip them or accept licenses automatically.

Package checks start from `eng/package-consumer-acceptance.ps1`, `eng/package-release-preflight.ps1`, `eng/package-release-dry-run.ps1`, `eng/package-binary-compat-preflight.ps1`, and `eng/packages.json`. Read scripts before using them. Inspect `tests/Unit/Infrastructure/Release/FluxFlow.Release.Tests` for the current API tests; the path shown in the public-API README is stale. Updating an API baseline means reviewing intended changes, not suppressing a failed compatibility gate.

Use the existing benchmark project at `benchmarks/FluxFlow.Engine.Benchmarks`; inspect available scenarios and the benchmark skill before adding/running workloads. Source scans and previous test logs are not fresh runtime evidence.

## 11. Risks, checkpoints, and migration policy

| Risk | Mitigation / stop condition |
|---|---|
| Another facade hides the old engine | Trace the executed slice; remove corresponding old implementations before completion |
| Constructor simplicity causes ownership leaks | Explicit successful-add transfer, borrowed DI mode, rollback and shared-client tests |
| Reliable fanout changes latency/isolation | Explicit policies, slow-sibling tests, measured tradeoff; no claim of atomic delivery |
| Package relocation expands into wholesale renaming | Approve project map first; rename only where responsibility/dependency clarity improves |
| Existing dirty changes are lost | Record baseline, scope every edit, preserve unrelated work, no reset/checkout cleanup |
| Temporary parallel runtime becomes permanent | Per-path migration owner and deletion gate; no candidate package may expose both runtimes |
| Reduced code removes durable guarantees | Provider conformance and restart tests; no casual schema rewrite |
| Fluent/JSON diverge or erase delegates | One portable document, one kernel, executable parity tests and explicit export errors |
| Broad promises outrun available environment | Report unrun integrations and missing consumer evidence; do not label incomplete gates passed |
| New loops/templates/sandboxing dominate the refactor | Resolve these separately; no implicit feature expansion or false claim of existing support |

Use logical review checkpoints after the baseline/API contract, the end-to-end slice, runtime consolidation, component migration, and package acceptance. A checkpoint is evidence and a chance to adjust a material decision, not a requirement to ask permission after every ordinary edit.

Migration is a controlled breaking release. Preserve capabilities and persistent user data, not every old class name. Provide a concise old→new guide for construction, typed helpers, addresses, link policies, request/reply, JSON/fluent authoring, lifecycle, events and package references. If an old behavior is intentionally changed, say so explicitly and map its supported alternative.

Do not create a goal until the user chooses to execute. Do not publish simply because historical pipelines were green. The next decision is whether to approve the implementation prompt, including the proposed defaults and the stated new-feature exclusions.
