# Core simplification implementation prompt

Date: 2026-09-08  
Status: implementation explicitly authorized on 2026-09-08; execution in progress. Public release and external consumer coordination remain outside this authorization.

Development plan: [core simplification plan](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/docs/plans/2026-09-08-core-refactoring-plan.md). Evidence: [architectural review](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/docs/reviews/2026-09-08-framework-simplicity-review.md).

The following is the authorized implementation assignment, not a request for another general review. Any new user decisions override the defaults below. The context-aware Add overload was explicitly deferred before execution and is not a requirement or checkpoint.

---

## Assignment

Refactor FluxFlow deeply into a small, cohesive Dataflow-based library organized around Application → Workflows → Components → Ports. Preserve its approved capabilities while reducing actual implementation complexity, component-authoring effort, duplicated runtime mechanisms, and mandatory dependencies.

The objective is not to add a fluent facade over the current engine. Simplify the code that actually executes. Reuse good existing block wrappers, immutable ownership logic, processing libraries, provider implementations, and conformance tests. Remove obsolete duplicated paths after migration.

The primary developer experience must be:

```csharp
workflow.Add("publish", new MqttPublishOperationNode(controller));

// An equally acceptable alternative when the component offers it:
workflow.Add("publish", MqttPublisher.Create(client, options));
```

The second name illustrates a component-owned factory, not a mandatory rename or a currently implemented type. The first uses an existing component class with a public constructor. Neither path may require a component creator service, factory interface, registration builder, activation descriptor, or nested action callback to manufacture an ordinary component.

Implement all approved phases and verify the result. Do not stop at the easy demonstration and declare the whole framework migrated. Conversely, do not migrate every package before proving the small public API and delivery kernel.

## 1. Authority, environment, and working discipline

- Begin only after explicit implementation authorization. Create a goal only if the user explicitly requests a goal; do not infer one from this file or invent a token budget.
- Current repository location at drafting time: `C:/Users/meisa/.codex/worktrees/9851/FluxFlow`. If executing elsewhere, resolve the actual repository and read its current instructions rather than assuming this checkout still exists.
- Respond in English. Start each new request with a short `Skills/tools:` line. Send concise progress updates and lead final reports with outcomes and remaining risks.
- Read applicable AGENTS instructions and the local command-routing instructions. Use the .NET architecture anchor, the existing graph index for navigation, and the focused testing/performance/telemetry/build skills when those tasks begin. Do not load unrelated skills or add their example frameworks by habit.
- Read the full development plan and review. Verify current source paths: the review is static evidence, not a promise that the checkout is unchanged. The existing code graph covers Core only, not the entire repository.
- Use `rg` first for searches and `apply_patch` for edits. Follow the current environment's required command prefix. On this host commands are `rtk`-prefixed; use `rtk proxy pwsh -NoProfile -File ...` for PowerShell scripts.
- Record the initial dirty worktree. Existing source, docs, package and sample changes belong to the user. Preserve them and integrate carefully. Do not reset, overwrite, stage, commit, push, or rename branches merely for convenience.
- Do not modify frozen release checkouts or rewrite generated historical release evidence. Create fresh evidence in a scoped location. Validate exact paths before destructive removal.
- Do not create new tasks, send messages to another task, publish packages, accept software licenses, change public releases, or perform destructive data migrations without the relevant explicit authorization.
- Do not spawn agents unless the user or an applicable skill/instruction explicitly authorizes the relevant delegation.
- Keep new project-visible names neutral. Use existing technical API/library names when describing actual dependencies, not external tool branding in new product names or generated titles.

## 2. Non-negotiable architecture

### One transport and one runtime

Use non-generic `FlowMessage` with `FlowValue` or `FlowError` as the sole inter-component transport. Do not also carry `FlowMessage<FlowValue>` internally. Typed C# inputs/results are local materialization helpers over this runtime, not a second node/source/queue/port/receive/coordinator hierarchy.

Framework-owned message, value, error, and event data must be immutable records with deliberate ownership of arrays, dictionaries, JSON and binary content. Stateful components/runtime owners remain classes. Do not confuse a record declaration with deep immutability. For `FlowValue`, explicitly document equality semantics and avoid implicit deep JSON comparison or hashing on the delivery path.

Preserve the same message/value reference through pass-through nodes and fanout. Derived processing results may allocate new envelopes and identities. Keep ownership copies needed when caller-owned JSON documents or mutable content enter the graph; do not remove a safe ingress clone just to claim zero copying. Preserve the payload/error invariant and exact binary-content behavior.

### Instance-first components

- A component is independently constructible and usable without an application host, DI container, catalog, editor, or JSON document.
- Constructors take actual dependencies and ordinary options. A static factory, if useful, belongs to the component and returns that component directly.
- `workflow.Add(name, instance)` supplies graph naming/ownership. Do not require a component name as a processing constructor parameter unless it is genuinely part of that component's domain.
- Defer the context-aware `Add` convenience. Do not introduce a context record or constructor callback without a later concrete requirement.
- Do not create `IComponentCreator`, creator-of-creator, factory-mode, or activation-wrapper hierarchies for normal usage. Do not preserve the current hierarchy behind a new `Add` facade.
- Use a small reusable wrapper for standard ports and lifetime. Do not move the old engine into a huge base class. Custom/source/sink/multi-port components must be possible through small contracts and composition, without dummy ports or inheritance from unrelated behavior.
- Declare actual ports locally once. Structural validation and runtime binding share those facts; optional designer presentation does not redefine them. No central type switch or master class edit when a new component is added.
- Normal DI registration is optional and explicit. Direct construction requires none. Do not resolve services or discover ports by reflection per message.

### Separate capabilities without excessive layers

Core owns transport, component/block execution, named graph ownership and address lookup, link semantics, lifecycle, one request/reply primitive, and small standard instrumentation points.

Core must not reference workflow JSON documents/converters, fluent builders, Designer, protocol clients, evaluator implementations/options, durable providers, or live-revision transactions. Payload JSON support inside `FlowValue` is allowed and is not workflow-configuration coupling.

Use separate JSON and Fluent projects. JSON parsing/type binding and fluent construction converge on one executable Core graph. Keep only one portable document model; do not invent an application model per layer. Reuse the existing real module/provider boundaries and avoid adding Abstractions/Contracts/Infrastructure projects for every small concept.

`FluxFlow.Core` and `FluxFlow.Json` are proposed destination names, not a preapproved instruction to rename every assembly. Finalize the current-project-to-target map before moving files. Preserve target frameworks unless the user approves a change.

Workflows are named ownership scopes, not another scheduling layer. Application is a small facade/owner using cohesive internal parts, not the implementation of routing, telemetry, persistence, resource binding, retries, expressions and revision transactions in one object.

### Use Dataflow honestly

Reuse Dataflow blocks for bounded buffering, execution, ordering, cancellation, and completion. Retain only the graph/delivery policy the blocks do not provide. Do not invent a general subscription API or pub/sub broker to implement application data, events or request/reply.

Do not use BroadcastBlock as if it guarantees every message reaches a slow bounded subscriber. It is appropriate for explicit latest-value/best-effort observation. Multiple links from a BufferBlock are not automatically reliable broadcast. Prototype a small bounded ActionBlock-based fanout dispatcher, but only adopt it after testing required semantics.

If exposing Dataflow interfaces, provide valid protocol behavior or expose the actual supporting block. Do not pretend a partial custom implementation supports reservations/consumption when it does not.

## 3. Behavior contracts

Use these proposed defaults unless the user changes them when approving the plan.

### Construction and ownership

- Distinguish naming: `application.AddWorkflow("main")` names a workflow; `workflow.Add("publish", ...)` names a component within it.
- Keep actual services/options explicit. Do not reuse the current broad `ComponentActivationContext` under a shorter name. Async I/O remains startup work.
- Successful direct `Add(name, instance)` transfers disposal ownership to the graph. Failed addition does not.
- A component resolved from DI can be explicitly added as borrowed; its scope owns disposal and must outlive graph execution. The graph still coordinates start/stop/drain. A small ownership option is sufficient; do not create an ownership framework.
- Borrowed dependencies such as a shared transport client are never disposed by every component using them. Graph-created components may borrow host-owned dependencies.
- Reject accidental addition of the same stateful instance under multiple graph owners. Named port views do not own their component.
- Constructors are synchronous and cheap. Async network setup/subscriptions belong in explicit startup. On partial failure, release newly owned resources and preserve the original error plus relevant cleanup failures.

### Linking and routing

- Default business-data links to bounded, awaitable reliable admission. Preserve reject-on-full/isolation behavior as an explicitly selected policy. Document best-effort observation separately.
- Define producer acceptance, selected-target admission, processing completion and durable acknowledgement distinctly. Do not claim delivery is atomic across a fanout or that local completion implies exactly-once external effects.
- Offer to all selected independent siblings without serially waiting for the first slow one before offering to healthy targets. Await overall admission under the reliable policy; sustained slow targets still exert backpressure.
- Share references across fanout. Handle target rejection/fault visibly. Do not retry an entire partially delivered fanout blindly.
- Keep `LinkTo` and `LinkFrom` symmetric, with one underlying link representation. Native predicates and compiled expression predicates use the same routing semantics.
- Specify and test ordering, capacities, target failures, unlink, link snapshots, `MaxMessages`, and runtime link transitions. Freeze ordinary topology at startup; retain supported dynamic linking as an explicit optional capability using the same kernel.
- If no condition matches, report a routing failure unless an explicit discard/fallback route is configured. Ensure unmatched messages cannot indefinitely occupy the head of a queue.
- Fan-in completion waits for all required upstream producers. Cancellation/fault releases blocked sends, coordination waiters, and graph-owned work.

### Request/reply

One bounded application-level exchange implementation must serve canonical and typed-convenience APIs. Inspect and simplify/reuse `PendingExchangeCoordinator`; do not add a third broker beside current application receive waiters and the RequestReply module.

Register before sending. Correlate by a unique exchange key, not TraceId alone. Preserve that key through transformations and validate the expected reply route/shape. Two concurrent requests sharing a trace must never both consume the first reply. MessageId identifies a message; CausationId identifies its immediate predecessor; neither replaces exchange identity.

The deadline includes admission and reply waiting. Settle and remove local pending state once on reply, failed admission, timeout, cancellation, exception or shutdown. Handle fast replies, late/duplicate replies and retry-attempt identity. First-reply behavior is the normal API; multi-reply aggregation remains separate. Protocol-specific identifiers are adapted by transport modules.

### Lifecycle and optional updates

Configure and link first; validate; start resources and consumers; start sources last. For graceful drain, stop ingress, drain accepted work, complete outputs, and dispose owned resources. Forced cancellation/fault is separate. No sync-over-async shutdown in the ordinary async path.

Normal execution must not prepare revision snapshots, stable generations or rollback transactions. The optional update layer owns candidate preparation, swaps, stable address proxies, in-flight policy, drain and rollback. It uses the same graph/kernel. Preserve supported live-update behavior, but do not claim selective component reuse merely because addresses remain stable.

### Portable authoring and extension

Keep the existing flat JSON conventions where possible: workflows contain named components with a `Type`, local options and readable Input/Output links. Do not rename casing or add deep wrappers without a concrete requirement and migration fixtures.

The portable fluent surface should resemble those facts. Avoid a duplicated options-builder model for every component. Code-only native `When(predicate)` needs no expression engine. Export must explicitly reject nonportable delegates or require a named expression; never silently omit them.

JSON needs a small trusted type-id→constructor binding outside Core. A local registration taking options and host services and calling `new Component(...)` is acceptable there. It is not a mandatory runtime creator, must not require a dedicated factory class per component, and must not permit arbitrary CLR type loading from documents. Resolve dependency services only at the composition boundary, not during processing.

Validate registered types, resource references, options and structural ports before starting components. Do not instantiate components repeatedly or perform network work for schema discovery. If a non-standard component needs structural metadata, declare it locally once and reuse it for both validation and binding.

### Expressions, events and telemetry

Preserve JSONata and C# expression adapters, application defaults, workflow overrides, named engines, portable condition parameters, and host-native configuration. Native C# predicates remain the simplest code-only route. Compile during graph preparation and isolate per-evaluation state; remove redundant dictionary/root serialization only with measured and tested equivalence. Do not create a universal rich expression AST or engine-options switch.

Keep logging, distributed tracing and metrics on standard platform APIs. Instrument actual operations centrally, propagate real context across queues and transports, and make disabled instrumentation cheap. Keep per-request/message/trace identifiers out of metric dimensions. Hosts own telemetry SDK configuration, sampling/exporters and dashboards.

Keep live application event aggregation small and Dataflow-based. Events are usable consumer data; reporting, historical archives, dashboard filters and aggregate reports are not Core responsibilities. Best-effort diagnostics cannot silently replace reliable business/audit events. Preserve an explicitly reliable/durable route for consumers needing exact delivery semantics.

In-process evaluators are not security sandboxes. Host functions/imported types are trusted capabilities; cancellation cannot forcibly stop arbitrary synchronous user code. Do not promise isolation that has not been implemented.

## 4. Known starting points to inspect

Read the review for supporting source locations, then verify them in the current checkout:

1. `src/Core/Foundation/FluxFlow.Nodes`: generic/canonical FlowMessage, FlowNode and FlowSource pairs, FlowOutput, FlowValue, FlowContent and IFlowNode.
2. `src/FluxFlow.Composition`: `Registration/RuntimeComponentRegistrationBuilder.cs`, `Factories`, executable `ApplicationModel`, JSON converters, `Authoring`, `ApplicationLinks`, runtime composition and event aggregation.
3. `src/Core/Execution/FluxFlow.Coordination`: bounded pending exchange primitives; compare RequestReply module usage.
4. `src/FluxFlow.Engine/Ports`: canonical versus typed output ports, custom input admission, receive subscriptions and the typed-only request/reply path.
5. Engine `Core/FlowFanoutSource.cs`, `Hosting/ApplicationRuntimePreparation.cs`, activator, `Internal/Revisions`, and `Signals/ApplicationRuntimeInstrumentation.cs`.
6. Serialization and MQTT composition projects, `JsonParseNode`, `MqttPublishOperationNode`, and `samples/FluxFlow.ComponentPackageTemplate` for representative authoring/construction.
7. `src/Authoring`, evaluator adapters, all component families, durability providers and Designer persistence.
8. `eng/packages.json`, solution/central package files, release tests, public API baseline, benchmarks and package-consumer scripts.

The review found 19 ordinary component composition projects depending on Designer. It also found different backpressure behavior between direct FlowOutput links and application routing. The existing full-target/healthy-sibling test describes intentional isolation behavior: retain that capability under an explicit policy instead of quietly deleting the test or treating both policies as equivalent.

## 5. Execution sequence and exit gates

### Phase 0: current baseline and decisions

Record current changes, project/package/target-framework map, every public component/capability, available tests, and dependency prerequisites. Produce a migration ledger with owner/destination, replacement API, tests, compatibility or persistence impact, and status for every row.

Run a fresh controlled Release build and available existing suites before broad changes. Record discovery/execution/skips and existing warnings. Capture the representative performance baseline under documented settings. Historical passing logs are context, not current proof.

Finalize the package destination map and the approved defaults. Resolve these only if they remain open:

- Does “loop” require actual bounded iteration/feedback? Keep current cycle validation until termination, capacity, cancellation and drain semantics are approved.
- Is a new template-rendering adapter included? No Scriban implementation was found in the review. Treat it as a new optional capability, not a migrated feature; rendering need not implement boolean evaluation.
- What numeric performance budgets follow from the measured baseline and consumer workload?

Do not let unresolved optional scope block unrelated baseline/kernel work. Do stop before silently expanding scope or claiming these capabilities are complete.

### Phase 1: runnable instance-first vertical slice

Build the smallest accepted canonical Core slice: directly constructed source → transform → conditional two-target fanout → reply sink, with errors, a borrowed dependency, and cross-workflow addressing.

Make the same scenario run through direct C#, fluent authoring and JSON on that exact kernel. Test actual outputs/errors/identity/completion, not only equality of generated documents. Include one custom component authored without DI, creator classes, editor metadata, materializer boilerplate or engine/revision knowledge.

Compile API examples, demonstrate constructor-once activation, and expose the full executed path. Keep the context overload deferred. Show this evidence before broad migration. If the API requires the forbidden creator machinery or introduces a large universal base class, simplify the design before continuing.

### Phase 2: canonical execution and coordination

Consolidate the dual node/source/port/output families; retain only edge typed conversions. Replace overlapping manual queue/pump implementations with the proven Dataflow-backed delivery policy. Complete backpressure/isolation, predicates, fallback, failure, fan-in completion, unlink and cancellation behavior.

Unify every request/reply API on the same bounded coordinator, fixing canonical-output support and trace-only matching. Separate ordinary startup/drain from revisions. Place standard operation instrumentation at the real execution boundaries.

Temporary migration scaffolding is allowed only as internal, tracked work. Each replaced path has a deletion gate. No candidate can ship with a second runtime indefinitely hidden underneath it.

### Phase 3: portable authoring and registration separation

Move workflow JSON conversion/validation and binding to the JSON project; move fluent builders to their actual project. Remove executable descriptors from portable documents. Keep sensible existing JSON format and local/cross-workflow address conventions, conditions and resources.

Replace mandatory creator registration with direct instance addition and narrow optional JSON bindings. Remove runtime/module registration dependencies on Designer across all affected packages. Collapse duplicate option/port declarations and provide independent optional visual metadata.

Prove a Core+module direct consumer restores/builds without JSON authoring, Designer, or evaluator implementations. Prove schema inspection does not construct runtime instances. Test delegate-export errors and JSON/fluent executable parity.

### Phase 4: all component families

Migrate in this order: serialization/template; real MQTT and HTTP; stateless mapping/routing/validation and related families; stateful timers/sessions/joins/windows/retries/projections/state/storage and all remaining manifest families.

Per family: retain the underlying mature library and domain behavior, migrate canonical ports and lifetime, simplify registration and metadata, update samples and tests, run focused conformance, and update the ledger/package map. Do not implement protocol clients or storage engines anew. Do not force a separate materializer class for trivial DTO access.

Acceptance includes adding a new third-party standard component and a custom multi-port component without editing Core or a central registry/switch. The user's explicit normal DI registration does not violate this rule.

### Phase 5: optional operations

Reattach live updates and dynamic links to Core, preserving stable-address behavior, candidate failure/rollback, in-flight policy and disposal. Do not create another execution engine for live graphs.

Migrate durable intake/output and all current providers without losing recovery, leases, acknowledgements, idempotency/deduplication, retry, replay or dead-letter behavior. Preserve stored data formats where possible. If a schema change is necessary, provide tested migration/rollback handling; stop for authority before destructive alternatives.

Complete scoped expression adapter integration and verified allocation improvements. Implement templates or bounded iteration only if explicitly included. Verify host/health integration, event streams, standard telemetry, and Designer persistence independently of Core.

### Phase 6: deletion, verification and local candidate

Remove migrated obsolete implementations/projects and temporary scaffolding, with recorded replacements; never delete unrelated work or release history. Update package dependency closure, intentional API changes, samples, migration docs and manifests together.

Run the complete architecture/behavior/concurrency/performance matrix below and actual package-only consumer acceptance. Use coherent unique local prerelease versions and an isolated restore cache; do not overwrite old artifacts under the same identity and trust a stale cache result.

Prepare the exact local feed path, versions, package/API/JSON migration brief and remaining risks. FluxMQ-NG is the intended real consumer, not the original FluxMQ project. Coordinate with its task only if separately authorized. Until that gate runs, distinguish internally verified candidate from release-ready consumer acceptance.

Do not publish, tag, push, create a release, or send a consumer-task message merely because this prompt mentions those future gates.

## 6. Mandatory verification

Use current repository test conventions. Load the applicable testing skill before writing tests and the benchmark skill before benchmark work. Use real provider/client integration where substitutes would hide relevant behavior. Prefer deterministic clocks/barriers and bounded timeouts; no flaky sleep-based concurrency assertions.

### Structural acceptance

- Direct construction and `Add(name, instance)` use no creator hierarchy, no runtime registration descriptor and no editor.
- Exactly one canonical transport runtime, fanout policy implementation, and pending exchange implementation remain.
- Core has no forbidden outward project references. Ordinary module registration has no Designer dependency. Target frameworks are preserved unless changed by approval.
- Standard/custom ports are declared locally once; no new component requires central switch/enum/master-builder edits.
- No DI resolution, address parsing, reflection discovery, expression compilation or unnecessary JSON round trip occurs merely to forward each message.
- Core's meaningful responsibilities are visible in small cohesive collaborators, not a god Application or base class.
- A capability ledger covers every current source/package family, not just the demonstration. Every deletion has a replacement and verification entry.

### Behavior and concurrency acceptance

1. Standalone constructor/static-factory components; DI optional; failed instance addition preserves caller ownership; successful addition respects owned/borrowed lifetime; JSON constructs once; invalid configuration causes no external startup; sources begin only after links exist.
2. Owned/borrowed/shared resource lifetimes; failed addition, partial startup, repeated stop/dispose, scope disposal and accidental instance reuse.
3. Canonical null/error/materialization behavior; caller-mutated inputs and disposed JSON documents; exact binary bytes; immutable headers; reference-identical forwarding/fanout; correct derived identity.
4. Direct/fluent/JSON outputs, errors, events, links and completion agree; both link directions; local/cross-workflow addresses; duplicate/unknown types/options/resources/ports; nonportable delegate export errors.
5. Slow/full/faulted/unlinked targets, partial fanout failure, ordering, capacities, link limits, predicate exceptions and unmatched fallback. Reliable routes do not silently drop. Explicit isolation remains available.
6. All-upstream fan-in completion; cancellation/fault unblocks senders; graceful drain versus forced stop; no background tasks or accepted work silently stranded.
7. Canonical request/reply without typed-port casts; many requests on one trace; reply before send returns; timeout/reply/cancel races; rejected admission; late/duplicate/wrong replies; retry attempts; shutdown; bounded pending state.
8. Expression engine defaults/overrides/parameters/native options; compilation and evaluation isolation; non-boolean results; failed/untrusted expression behavior; no claim of sandboxing synchronous host code.
9. Optional updates with stable addresses, in-flight work, drain/rollback/cleanup; durability restart, leases, acknowledgements, deduplication, retries, replay/dead letters and each provider's conformance.
10. Real operation tracing across queues/transports, appropriate fan-in context, bounded metric dimensions, logging and event output; disabled telemetry overhead and slow/failing best-effort observer isolation.
11. Package-only fresh-restore consumer, supported target frameworks, health and Designer persistence, realistic protocol/storage integrations, and any explicitly approved new adapter/iteration behavior.

### Performance acceptance

Compare reproducible baseline and candidate with the same machine/framework/configuration, warmup, payloads, capacities, parallelism, and telemetry settings. Repeat measurements and state uncertainty.

Measure 1/8/32 hops, 1/2/8 fanout targets, small/large structured values and exact binary content, conditions off/native/each adapter, telemetry off/on, slow targets, saturation, cancellation and revision swaps.

Record throughput, p50/p95/p99 latency, bytes allocated per message, retained heap, queue depths and pending exchanges. Retained state must stabilize under sustained bounded load and recover after drain. Fanout must not clone payload-sized data per branch. Unchanged expressions must not be reparsed for each message.

Choose numeric targets from the baseline/workload; do not invent improvement percentages. Investigate meaningful regressions and explicitly review reliability tradeoffs. Track removal of duplicated execution stages and dependencies, not just file or line counts. Do not shorten code artificially to satisfy a metric.

## 7. Existing command and evidence entry points

At drafting time the SDK selection is in `global.json`, and the solution is `FluxFlow.sln`. Preserve the current target framework matrix and re-check command paths after project moves.

```powershell
rtk proxy dotnet build FluxFlow.sln --configuration Release
rtk proxy pwsh -NoProfile -File eng/test.ps1 -Suite Unit -Configuration Release -NoBuild
rtk proxy pwsh -NoProfile -File eng/test.ps1 -Suite Acceptance -Configuration Release -NoBuild
```

Inspect `eng/test.ps1` for Integration and Smoke. `All` does not run external Smoke. Verify test discovery and actual counts. Report unavailable container/external/licensed prerequisites; never label unrun suites passed or auto-accept a license.

Read current package scripts before invoking them: `eng/package-consumer-acceptance.ps1`, `eng/package-release-preflight.ps1`, `eng/package-release-dry-run.ps1`, `eng/package-binary-compat-preflight.ps1`. Their manifests and fixture assumptions must migrate with package identities. Distinguish local pack/validation from public publication.

The current release tests are under `tests/Unit/FluxFlow.Release.Tests`; do not copy the stale path from the public-API README. Intentionally review/update API and package validation for the breaking release rather than disabling checks wholesale or manufacturing an unchanged baseline.

Use the existing `benchmarks/FluxFlow.Engine.Benchmarks` project as the starting point; inspect scenarios before choosing commands. Save fresh build/test/performance results with exact commands/settings and distinguish old failures from regressions. Historical counts and a graph retrieval benchmark do not prove runtime performance.

## 8. Scope limits and stopping rules

This assignment allows the approved refactor, tests, samples, documentation, project/package adjustments and local candidate preparation. It does not authorize public release actions, destructive data migration, editing a separate consumer repository, or coordination messages by inference.

Avoid new general-purpose mediators, service locators, universal repositories, reflection-driven plugin discovery, rich expression ASTs, report archives, custom subscription frameworks, and unneeded compatibility layers. Do not rewrite mature adapters because “use a library” sounds simpler when the current component already uses that library.

Do not silently remove advanced features because they are harder to migrate. Do not promise arbitrary cyclic graphs, an unimplemented template engine, or secure execution of untrusted in-process code. Track approved new features separately from retained existing features.

If a genuinely material choice conflicts with the approved API/semantics, present concrete evidence and ask for that decision before taking the conflicting action. Otherwise continue through safe, in-scope work; avoid asking after every routine edit. Missing environment prerequisites are a verification limit, not permission to claim completion.

## 9. Required progress and final handoff

At the API/slice checkpoint, show a compiled constructor/static-factory example, the matching JSON/fluent workflow, a short execution-path diagram, focused test results and the code removed or bypassed. This checkpoint must make it possible for the user to reject renewed complexity before repository-wide migration.

At subsequent checkpoints, report migrated capability families, removed duplicate paths/dependencies, verified behavior, measured tradeoffs, and remaining gates. Keep the migration ledger current.

Final handoff must include:

1. What the public construction/linking/request-reply API now looks like, with a minimal working example.
2. Final project dependency structure and which optional capabilities moved where.
3. Completed feature ledger and explicit breaking API/JSON/package migration guide.
4. Exact fresh build/test/integration results, discovered/executed/skipped counts, warnings and unrun prerequisites.
5. Before/after performance results with configurations and caveats; no unsupported speed claims.
6. Local candidate feed/versions and actual package-consumer acceptance status, including whether FluxMQ-NG has tested it.
7. Remaining risks, unresolved approved scope and the next external decision. State explicitly that nothing was published unless a later separate authorization permitted publication.

Mark the refactor complete only when all approved scope is migrated and verified, no temporary duplicate runtime remains, and required acceptance gates are actually satisfied. If an external gate is unavailable, report the precise partially completed state. A clean build, a beautiful sample, or a smaller facade alone is not completion.
