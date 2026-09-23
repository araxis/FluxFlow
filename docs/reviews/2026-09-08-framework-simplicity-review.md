# Framework simplicity review

Date: 2026-09-08  
Status: investigation and proposal; no implementation approved by this report.

## 1. Conclusion

The concern is supported by the implementation: FluxFlow has a usable small Dataflow foundation, but its application-facing architecture makes callers and component authors understand too many overlapping models. This is not mainly a formatting problem, a shortage of interfaces, or a need for DDD.

The recommended change is a boundary redesign around **Application → Workflows → Components → Ports**, with **one immutable canonical message representation and one execution path**. JSON, fluent authoring, visual design, live revision management, transport integration, and durable delivery should compose around that execution path. They should not each introduce another version of it.

Do not discard every implementation. Preserve the existing external-library adapters, domain processing, ownership rules, failure behavior, and conformance tests where they remain useful. Do not judge success by a smaller facade hiding unchanged complexity.

The previous expression and fluent changes improved individual features but did not resolve these structural issues. More incremental API additions alone will not deliver the requested simplicity.

## 2. Scope and evidence

The review inventories all **62 source projects**, **750 C# files**, and **78,279 physical lines** under src, excluding bin/obj. Lines include comments and blanks; they are not logical LOC, complexity scores, or proof of poor quality.

| Area | C# files | Physical lines |
|---|---:|---:|
| Core | 122 | 12,425 |
| Engine runtime | 73 | 9,761 |
| Durable input | 29 | 7,423 |
| Durable output | 33 | 7,928 |
| Components | 481 | 39,716 |
| Authoring | 8 | 682 |
| Expression adapters | 2 | 188 |

The runtime's Ports folder alone is **24 files / 5,165 lines**, a subset of the runtime figure. The full Engine area also contains 156 lines outside the three listed Engine subareas. The large durability and component totals must not be misrepresented as the minimal execution core.

Deep source inspection covered message ownership, both node implementations, reliable fanout, application port admission/routing/observation/request-reply, component activation, revision preparation, JSON/fluent contracts, a serialization component's registration path, expression evaluation, event conversion and runtime instrumentation. MQTT and storage were inspected for dependency/policy boundaries, not audited line by line.

This is a framework-wide architectural review with selected deep execution traces, **not a claim to have manually audited every line**. No new unit tests, load tests, or performance benchmarks were run for this review. Source-path deductions below are identified separately from existing test evidence.

Graph analysis:
- User-selected scope: src/Core, 132 supported files, approximately 32,931 words by detection.
- Structural extraction: 1,493 nodes / 3,531 edges; semantic extraction: 33 nodes / 38 edges plus two grouped relationships from five READMEs.
- After graph normalization: **1,508 nodes / 2,591 edges / 112 communities**.
- The graph is a navigation aid, not a compiler-resolved dependency proof. Documentation assertions describe intent, not guaranteed runtime behavior. Inferred edges remain marked.
- The initial Windows extraction helper lacked a process-entry guard and produced an empty result; it was corrected and extraction rerun successfully before generating the graph.
- Semantic-agent token usage was not exposed; cost is recorded as unavailable, not zero.
- Generated analysis and reproducible scan recipes: [source measurements](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/graphify-out/source-measurements.json), [measurement script](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/graphify-out/measure-source.ps1), [graph audit](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/graphify-out/GRAPH_REPORT.md), [interactive graph](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/graphify-out/graph.html).

## 3. Ranked findings

P1 means resolve the contract/boundary before another major architecture release. P2 means address within the simplification program. These priorities are not claims of measured speedups.

### F1 — P1: request/reply is not one canonical core capability

**Evidence.** [ApplicationPortRuntime.SendAndReceiveAsync](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/FluxFlow.Engine/Ports/ApplicationPortRuntime.cs:411) registers a receiver by request.TraceId. [GetOutput<T>](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/FluxFlow.Engine/Ports/ApplicationPortRuntime.cs:929) requires ApplicationOutputPort<T>, not FlowValueApplicationOutputPort. The public ApplicationPorts facade delegates to this path.

Consequences directly visible in the source:

1. A canonical output cannot use this request/reply path, even when the desired response type is FlowValue: the concrete output-port implementation check fails.
2. Two concurrent typed requests on the same output and trace register separate waiters that both accept the first matching message. [Receive waiter](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/FluxFlow.Engine/Ports/ApplicationOutputSubscriptions.cs:30) filters only by trace; [Output dispatch](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/FluxFlow.Engine/Ports/ApplicationOutputPort.cs:454) offers messages to every waiter.
3. Separately, [RequestReplyCoordinator](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/Components/RequestReply/FluxFlow.Components.RequestReply/RequestReplyCoordinator.cs:20) and CorrelatedRequestTracker use correlation-based pending exchanges. The lower-level PendingExchangeCoordinator already supplies bounded pending state and a deadline timer.

These are static execution-path findings, not newly executed reproductions. Existing request/reply tests inspected exercise typed ports; they do not establish canonical-path parity.

**Proposal.** One request/reply service owned by the core application, using one pending-exchange implementation. Register before sending; settle and remove exactly once on reply, timeout, cancellation, failed admission, or shutdown. Transports adapt their protocol identifiers at the boundary rather than implement a second workflow broker.

Trace identity groups an execution lineage; it is not necessarily a unique request. Use a unique exchange key. The existing CorrelationId can serve that role if its semantics are made explicit. Preserve that key across all intermediate derived messages; use a fresh attempt discriminator when retries must reject old responses. MessageId identifies each message; CausationId identifies its immediate predecessor, so neither an arbitrary downstream MessageId nor the final message's immediate CausationId alone identifies the original request.

A reply port and an explicit reply operation make the intended response unambiguous. First-response semantics should be the simple default; collecting replies from multiple branches is an optional aggregator. No claim of exactly-once external side effects follows from settling one local waiter.

### F2 — P1: identical-looking links have different delivery guarantees

**Evidence.** [FlowOutput](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/Core/Foundation/FluxFlow.Nodes/FlowOutput.cs:267) awaits delivery to its selected targets. A directly connected node therefore propagates capacity pressure. However, [canonical application routing](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/FluxFlow.Engine/Ports/FlowValueApplicationOutputPort.cs:516) calls target.TrySend and does not await capacity or retry rejection. [Input admission](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/FluxFlow.Engine/Ports/ApplicationInputPortCore.cs:81) returns Full when capacity is exhausted.

This is intentional current behavior, not a newly discovered accidental violation of its tests: [Full_target_does_not_block_healthy_fanout_sibling](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/tests/Unit/FluxFlow.Engine.Tests/ApplicationPortRuntimeTests.cs:380) expects a full sibling to reject while the other continues. It nevertheless conflicts with a simple universal expectation of reliable Dataflow linking.

Current canonical application hop:

```text
component ActionBlock
  → node reliable FlowOutput
  → stable output BufferBlock and dispatch pump
  → TrySend into stable input's custom queue
  → component ActionBlock

The TrySend boundary can reject; upstream acceptance is not end-to-end delivery.
```

**Proposal.** Distinguish the two useful policies explicitly:
- Workflow data: bounded, awaitable delivery by default.
- Optional observation: non-blocking with a documented loss/overflow policy.
- Preserve reject-on-full admission as an explicit option for consumers that need isolation; do not silently remove it.

For a permanently slow subscriber, bounded memory, lossless delivery, and producer independence cannot all hold indefinitely. Choose backpressure, explicit loss/rejection, or durable storage. Calling the operation “broadcast” does not solve that tradeoff.

### F3 — P1: canonical data was added alongside a second runtime type system

**Evidence.** Both FlowMessage<T> and non-generic FlowMessage coexist in a 600-line file. Both [typed FlowNode](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/Core/Foundation/FluxFlow.Nodes/FlowNode.cs:12) and [canonical FlowNode](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/Core/Foundation/FluxFlow.Nodes/FlowValueNode.cs:12) own similar processors, output handling, lifecycle, error handling, and disposal. FlowSource has the same pairing. Component and application ports repeat typed/canonical variants.

ApplicationOutputPort.cs is 621 lines; FlowValueApplicationOutputPort.cs is 585. This is not just harmless generic convenience at the edge: concrete type selection reaches routing, revision bindings, receive/observe adaptation, and request/reply.

**Proposal.** Choose one internal and inter-component contract: the current non-generic FlowMessage carrying FlowValue is a reasonable starting point. Do not also standardize FlowMessage<FlowValue> as a second equivalent transport. Typed C# ergonomics should be thin boundary helpers that materialize input/output values over that same runtime.

Retain the ability to write strongly typed C# processing, but stop maintaining a second scheduler/port/lifecycle family to provide it. Breaking API changes are preferable here to permanent compatibility branches.

### F4 — P1: runtime registration is coupled to visual design

**Evidence.** **19 component composition projects** directly reference FluxFlow.Components.Designer. [Serialization package references](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/Components/Serialization/FluxFlow.Components.Serialization.Composition/FluxFlow.Components.Serialization.Composition.csproj:16) is representative.

[Serialization registration](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/Components/Serialization/FluxFlow.Components.Serialization.Composition/SerializationServiceCollectionExtensions.cs:13) uses AddDesignedComponent and mixes factories, input shapes, port binding, icons, labels, option editors, sections, and display ordering. [Serialization fluent authoring](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/Components/Serialization/FluxFlow.Components.Serialization.Composition/SerializationAuthoringExtensions.cs:7) also depends on the designed contract.

This does **not** mean Nodes or Engine directly reference Designer; they do not. The coupling is at the ordinary module registration/authoring boundary.

**Proposal.** A component's executable registration must work with zero designer references. Put visual presentation in optional metadata providers or sidecars. Designer may read structural component information; structural registration must not require Designer. Do not force every module into three extra packages if a small common optional metadata package is sufficient.

### F5 — P1: the portable model, fluent model, and executable composition model overlap

**Evidence.** Composition includes JSON conversion, parsing, link compilation, fluent builders, dependency registration, factories, resource contracts, and runtime ownership. [ApplicationDefinition](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/FluxFlow.Core/Configuration/ApplicationModel/ApplicationDefinition.cs:8) has a JSON converter attribute while also carrying executable ComponentDescriptors and ApplicationResourceContracts.

A separate Fluent project exists, but it references Engine and Composition; a substantial part of fluent authoring lives inside Composition and module composition files. The folder names therefore do not express the actual dependency boundaries.

[ApplicationRuntime.Create](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/FluxFlow.Core/Configuration/Runtime/ApplicationRuntime.cs:51) also synthesizes “flow/node-N” identities for directly composed nodes. The user's Application/Workflow/Component language is not consistently the runtime model.

**Proposal.** JSON and fluent construction should produce the same small runtime graph/definition contract. Keep executable predicates/factories outside portable JSON documents. It is reasonable to have a serialized document and a compiled graph; it is not necessary to introduce a separate application model at every stage. Graph compilation means validating and binding a topology once, not inventing an interpreter for normal message delivery.

Keep runtime composition independent of JSON serializer attributes, editor contracts, and fluent builder types. A code-only delegate is not serializable: export must clearly reject it or require a named registered expression, never silently omit behavior.

### F6 — P2: basic execution inherits live-revision machinery

**Evidence.** [ApplicationRuntimePreparation](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/FluxFlow.Engine/Hosting/ApplicationRuntimePreparation.cs:15) creates resource snapshots, components, runtime ownership, event attachments, drain plans, port generations, revision builders, and rollback cleanup. [Component activation](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/FluxFlow.Engine/Hosting/ApplicationRuntimeComponentActivator.cs:16) traverses every workflow/component in the next definition. The inspected preparation path rebuilds all component instances, even when it reuses the stable port surface.

Live updates are a valuable feature. The problem is that the principal hosted application model makes this machinery part of ordinary startup/composition, and “affected workflow” planning should not be confused with selective component reuse.

**Proposal.** A normal application owns a graph and can start, stop, drain, and dispose it. An optional update controller owns candidate preparation, swaps, generations, and rollback. Preserve stable-address/revision capabilities for applications needing them, without requiring every direct link to pass through a swap-capable proxy.

Moving folders alone is insufficient. Both static and update-capable execution must use the same delivery kernel.

### F7 — P2: component extension requires repeated declarations

The runtime JsonParseNode is already small: about 40 lines. That is evidence that thin wrapping is possible today. The problem emerges when making that component available throughout the product.

The six serialization operations are repeated in the family registration list, contract properties, option-name declarations, designer metadata, factories, fluent overloads, and the builder's options projection. The same options appear as runtime options and builder properties. This spreads one extension across unrelated authoring concerns.

**Proposal.** Standard input/output/events ports should have one convention and default registration. A new simple component needs its processing implementation and one explicit registration; optional configuration binding and editor metadata are separate opt-ins. Non-standard ports should be declared locally once, not taught to a central switch.

An interface implementation alone is not automatically discovered by a DI container. Explicit registration remains correct and predictable. Optional assembly scanning can be offered later if there is demonstrated demand; it is not required to satisfy the open/closed principle.

Use normal constructor dependencies and explicit resource ownership. Do not pass a service locator into every ProcessAsync method. Resolve component instances and port addresses at graph creation, not per message.

### F8 — P2: multiple custom fanout/queue mechanisms need consolidation

[FlowOutput](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/Core/Foundation/FluxFlow.Nodes/FlowOutput.cs:14) has its own Queue<Delivery>, semaphores, pump, lifecycle state, link snapshots, completion, and source-block implementation. [FlowFanoutSource](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/FluxFlow.Engine/Core/FlowFanoutSource.cs:5) implements another fanout pump over BufferBlock, with different admission and failed-target behavior. ApplicationInputPortCore introduces another queue/pump to support stable input replacement.

There are legitimate policies here: reliable fanout, best-effort diagnostic isolation, revision boundaries. But separate implementations multiply race conditions and make their differences hard to see.

**Proposal.** Reuse Dataflow's bounded queues, scheduling, cancellation and completion. Retain only the policy that Dataflow does not provide directly. For reliable fanout, evaluate a small bounded ActionBlock-based dispatcher that awaits each selected target, sharing the same message reference. Freeze topology for the ordinary runtime; let the optional update layer manage link-set transitions.

This is a prototype recommendation, not a proven drop-in replacement. Existing acceptance-time link snapshots, unlink cancellation, MaxMessages, ordering, target failure, and completion guarantees must be specified and checked before replacing FlowOutput.

**Important correction:** BroadcastBlock is not a lossless bounded multi-subscriber queue. It retains a current value and offers it to targets; postponed/declining targets are not an assurance that every historical value is retained for them. Use it when latest-value/best-effort observation fits. Multiple links from BufferBlock also do not mean every target receives each message. [Dataflow documentation](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.dataflow.broadcastblock-1), [runtime implementation](https://source.dot.net/System.Threading.Tasks.Dataflow/Blocks/BroadcastBlock.cs.html).

### F9 — P1/P2: observability exists, but the central conversion path needs correction

**Positive evidence.** [ApplicationRuntimeInstrumentation](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/FluxFlow.Engine/Signals/ApplicationRuntimeInstrumentation.cs:8) already uses ActivitySource, Meter, DiagnosticListener and ILogger. This is not a case of having no platform instrumentation.

**P1 capacity risk.** CreateTags includes flow.trace_id in counter/histogram tags. A new trace can produce a new metric attribute combination. Remove per-message/per-request identifiers from metric dimensions; retain them in logs/traces or exemplars. [Metric cardinality guidance](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#multi-dimensional-metrics).

**P2 semantic/cost issue.** Diagnostic events are serialized into FlowValue, subsequently read back as FlowEvent, and may generate an Activity while the diagnostic is being processed. That does not, by itself, measure the original operation or establish its parent span. A custom flow.trace_id tag is not distributed context propagation.

**Proposal.** Instrument the actual central processing/admission boundaries using standard tracing, metrics and logging APIs, with lightweight opt-out/no-listener behavior. Propagate real tracing context across queued work and transport boundaries. Host applications own the telemetry SDK, sampling, exporters and dashboards. Domain events remain usable workflow data; telemetry is not a replacement for business events, and business events should not need to be JSON-serialized solely to increment a counter. [Official .NET instrumentation guidance](https://opentelemetry.io/docs/languages/dotnet/instrumentation/).

The present application event aggregator is already small and keeps reports/history outside the runtime. Preserve that separation. Its BroadcastBlock-based stream is best effort, not a sufficient foundation for exact audit/accounting reports without a stronger delivery path.

### F10 — P2: expression support is correctly optional, but JSON bridging is expensive

The dependency-free Mapping contracts and separate expression adapters are good boundaries. Keep them. The host-native configuration hooks also preserve evaluator power without putting every native option into fluent syntax.

[Condition evaluation](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/FluxFlow.Engine/Ports/FlowValueApplicationOutputPort.cs:543) creates a dictionary per conditional route; FlowMapContext copies it. BoundCondition creates and copies another dictionary when parameters are present. [Expression adapter](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/FluxFlow.Expressions.Jsonata/JsonataFlowExpressionEngine.cs:45) then builds another variables dictionary, serializes the full variables object, serializes each variable again, parses engine-native JSON, renders the result to text, and parses/deserializes it again.

**Proposal.** Retain compile-once behavior. Give evaluation a clear canonical input plus immutable bindings; adapt to each engine once per evaluation, without duplicate root/variable serialization where the native API permits. Do not invent a universal rich expression AST or a large engine-options switch.

No Scriban dependency or implementation was found by the source/project scan. Treat a template mapper as an outstanding optional adapter, not an already completed feature. Template rendering and boolean condition evaluation are different capabilities; do not force every renderer to implement every evaluator interface. [Template engine documentation](https://github.com/scriban/scriban), [JSON expression engine documentation](https://github.com/mikhail-barg/jsonata.net.native), [C# expression engine documentation](https://github.com/dynamicexpresso/DynamicExpresso).

For dynamic user-authored workflows, host-provided functions/types are a trust boundary. In-process evaluation is not a security sandbox or a reliable way to stop arbitrary blocking host functions. The current C# adapter disables assignment by default and documents trust/concurrency requirements; preserve this. The reviewed dependency is 2.19.5, above the 2.19.4 fix listed for the specific late-binding advisory checked; that is not a complete vulnerability audit. [Maintainer advisory](https://github.com/dynamicexpresso/DynamicExpresso/security/advisories/GHSA-5jx8-p6q2-455g).

### F11 — P2: actual feedback loops need an explicit execution contract

[ValidateAcyclic](C:/Users/meisa/.codex/worktrees/9851/FluxFlow/src/FluxFlow.Core/Configuration/ApplicationLinks/ApplicationLinkCompiler.cs:573) rejects data-link cycles; signal targets are excluded from that check.

If “loop” means a workflow graph, there is no new requirement here. If it means feedback/iteration, arbitrary cyclic data links are not supported by the present compiler. Simply deleting cycle validation risks bounded-capacity deadlocks, unending execution and ambiguous draining.

Prefer an explicit iteration/feedback component with termination, cancellation and iteration/concurrency limits, or a separately specified safe feedback-link model. Resolve this distinction before promising dynamic JSON loops.

## 4. Message and event design

### One canonical envelope; immutable payloads

Use an immutable record envelope for runtime messages/events, carrying:
- A FlowValue payload or explicit FlowError, preserving the one-case invariant.
- Message identity and causation.
- An optional exchange-correlation key.
- Trace context and timestamp.
- Immutable headers.

Not every message is semantically an “event”: commands, requests, replies and facts can all travel through the same envelope. Event-driven execution does not require one CLR subclass per JSON business event, nor a payload-wide switch on event type. A stable event type identifier plus structured FlowValue is sufficient for dynamic graphs; optional typed records are local conveniences.

FlowValue is currently a sealed immutable **class**, not a record. That is not inherently a safety defect. A record declaration provides only shallow immutability for referenced values. Arrays, dictionaries and exposed mutable objects still need ownership rules. Converting it to a sealed record for consistent API language is reasonable only with deliberate equality semantics; it does not create a performance improvement by itself. [Record semantics](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record#immutability).

### Fanout should not clone the message per branch

The inspected FlowOutput already sends the same reference to its targets. Keep that. A derived processing result is a new message and is a different operation from fanout.

FlowValue detaches JsonElement ownership at ingestion. Do not blindly remove Clone: it protects against the original JsonDocument being disposed. Clone of an already cloned element or its child does not allocate additional storage according to the platform contract. [JsonElement.Clone](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonelement.clone).

FlowMessage.CopyHeaders also already reuses ordinal immutable dictionaries. It should not be reported as copying every header dictionary on every hop.

### Keep materialization local; avoid mandatory conversion

A component can inspect JsonElement directly for a few fields, deserialize a private input record, or use a module-local materializer for a special contract. A new materializer class should not be compulsory for trivial DTO deserialization.

Serialization into the canonical value may be necessary at an external/typed boundary. Serializing/deserializing merely to forward the same value should not be necessary. Pass-through nodes preserve the envelope; transforming nodes produce a derived envelope.

Opaque binary content deserves a separate measurement case. FlowContent already owns immutable bytes; its FromBytes path uses bytes.ToArray followed by ImmutableArray.CreateRange. Ownership copies at ingress are different from branch cloning. Avoid pooled mutable buffers in shared messages unless their lifetime is explicitly owned; accidental reuse is worse than an honest copy.

## 5. Proposed project boundaries

Arrows show dependency direction, not runtime message flow. Names below are proposed roles, not a finalized package rename list.

```text
JSON authoring ───────────┐
Fluent authoring ─────────┼──→ Core
Direct C# composition ────┘      Application
                               └─ Workflows
                                  └─ Components
                                     └─ addressable Ports

Component modules ──────────→ Core + existing client/processing libraries
Optional visual design ─────→ authoring metadata/documents
Optional live updates ──────→ Core graph/lifetime operations
Optional durable delivery ─→ Core ports + selected persistence provider
Optional host integration ─→ Core + DI/hosting/telemetry configuration
```

Start with one cohesive Core project, one JSON project, and one Fluent project. Keep existing module/provider projects where they provide a real optional dependency boundary. Do not create an Abstractions, Contracts, Application, Domain, Infrastructure and Implementation assembly for each tiny feature.

Core responsibilities:
1. Immutable runtime message/value/error contracts.
2. Component execution over Dataflow and explicit lifecycle.
3. Workflow/application graph ownership and address lookup.
4. Linking with documented delivery/completion semantics.
5. One request/reply coordinator.
6. Small standard instrumentation points.

Core must not know editor layout, JSON property names, expression-engine brands/options, broker configuration, SQL schema, report definitions, or live-revision transaction mechanics.

A workflow should initially be a named graph/ownership scope, not another message scheduler. Application is a cohesive owner and small facade, not the implementation of routing, persistence, telemetry, expression evaluation and every state transition.

### What authoring a component should feel like

Illustrative target API only; this code is **not an implemented API**:

```csharp
public sealed record NormalizeInput(string Text);
public sealed record NormalizeOutput(string Text);

public sealed class NormalizeComponent(ITextNormalizer normalizer) : FlowComponent
{
    protected override async ValueTask<FlowMessage> ProcessAsync(
        FlowMessage message, CancellationToken cancellationToken)
    {
        var input = message.Value.Deserialize<NormalizeInput>();
        var text = await normalizer.NormalizeAsync(input.Text, cancellationToken);
        return message.With(FlowValue.From(new NormalizeOutput(text)));
    }
}
```

The common wrapper handles standard port construction, default error propagation and lifecycle once. It must remain small and composed from blocks; moving a thousand-line runtime into a base class would not satisfy this proposal. Multi-output and source components remain supported through small explicit port composition, not a mandatory universal node shape.

A code-only user can instantiate and link this component directly. DI registration supplies it to an application. JSON authoring optionally adds one local type/configuration binding. Visual metadata is optional and does not enter this source file.

### Configuration and fluent structure

Keep the same visible nouns and addresses: application, workflows, components, links, source, target, condition. Flat should mean readable and minimally nested, not stripping away meaningful ownership.

Both input-side LinkFrom and output-side LinkTo describe one link declaration. They must not implement different routing. Native C# predicates compile directly; persisted conditions name a registered engine and contain portable expression text/parameters. Engine setup stays in host DI, with application defaults and workflow overrides.

No configuration load should construct the same component repeatedly to discover its schema. Validate portable configuration and registered contracts before activation, then instantiate once.

### DI and ownership

Use address-keyed ports as normal service registrations or a small application-owned lookup. The built DI container is not a mutable graph registry: arbitrary runtime additions need a clearly owned graph snapshot or update controller.

Component lifetime belongs to its application/workflow. Shared transport clients can be explicitly host-owned. A borrowed client is not disposed by every component using it. Async cleanup belongs to the component/runtime ownership boundary; a port view should not accidentally own its target.

## 6. Preserve functionality by relocating responsibility

| Capability | Intended home | What to preserve |
|---|---|---|
| Message/value/error transport | Core | Canonical representation, immutability, identity and conversion helpers |
| Addressable workflows/components/ports | Core | Lookup, direct linking, DI integration |
| Request/reply | Core | Correlation, bounded pending requests, cancellation, deadlines, shutdown |
| JSON configuration | JSON authoring | Validation, flat documents, named type bindings |
| Fluent authoring | Fluent authoring | LinkTo/LinkFrom, native predicates, readable structure |
| Expression/template evaluation | Optional adapters | Native configuration, compilation, typed results, limits |
| MQTT/HTTP/file/storage processing | Related component modules | Mature library reuse and protocol/domain-specific policy |
| Join/window/correlation/timers | Optional component modules | Required state, timeouts, deterministic clocks |
| Live graph replacement | Optional update layer | Stable addresses, rollback, drain and ownership |
| Durable intake/output | Optional delivery layer/providers | Leases, deduplication, replay, recovery, acknowledgements |
| Domain events | Core data/explicit ports | Structured immutable events and chosen delivery guarantees |
| Logging/tracing/metrics | Standard instrumentation + host setup | Full context propagation, low-cardinality metrics, exporter choice |
| Dashboards/reports/history | Consumer application | Filters, storage, visualization, aggregations |
| Designer/editor | Optional authoring tooling | Structural metadata, validation, layout and persistence |

Storage already uses the platform SQLite provider; MQTT already delegates protocol calls to client libraries. Replacing these with “use a library” is not a new solution. SQLite supplies a database, not application delivery leases, retry semantics or acknowledgement policy. Keep necessary domain logic outside the minimal Core.

Also, an Async suffix does not guarantee non-blocking I/O: the SQLite provider documents synchronous execution of its async ADO.NET methods. Bound and measure that work at the storage boundary; do not create Task.Run wrappers everywhere or complicate all components because one provider is synchronous. [Provider async limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async).

## 7. Simplification sequence

### Stage 1 — agree on behavior before changing implementation

Write a short architecture contract covering:
- One canonical wire type; typed helpers are adapters.
- Reliable versus reject/drop delivery and what acceptance means.
- Unique request/reply exchange key, explicit reply, timeout and retry semantics.
- Application/workflow/component ownership and addresses.
- Graceful drain versus forced cancellation/fault.
- Whether “loop” means a graph or actual feedback.

Record the existing behavior that must remain available. Revise tests that intentionally describe a changed policy; do not accidentally preserve contradictory defaults.

### Stage 2 — prove a small vertical slice

Build a demonstrator of one source → transformation → conditional two-way fanout → reply sink. Instantiate it directly in C#, through the fluent layer, and from JSON, all over one kernel. Include a custom component authored without Designer, custom registries, materializer boilerplate or engine knowledge.

Use the existing kernel pieces where suitable. A temporary prototype is reasonable; shipping two indefinitely supported runtimes is not. The acceptance criterion is that an ordinary developer can understand the full path and add a component without editing Core.

### Stage 3 — extract authoring and extension boundaries

Move JSON parsing/conversion out of Core. Move fluent builders out of runtime contracts. Remove the 19 ordinary module→Designer dependencies. Collapse duplicate type/option/port declarations. Migrate one simple module first, then a real transport module to expose ownership/request-reply issues early.

Do not begin with the hardest durable store. First prove the common extension path, then preserve its provider-specific policies.

### Stage 4 — consolidate execution and coordination

Replace duplicated typed/canonical runtime implementations with one path. Consolidate fanout using Dataflow primitives plus minimal explicit policy. Route request/reply through the one coordinator. Separate simple startup/drain from optional revisions.

Every old path removed must have an explicit replacement and a migration entry. Avoid compatibility aliases that perpetuate the dual system.

### Stage 5 — migrate advanced features and verify consumers

Reattach durable delivery, advanced routing, expressions, templates, diagnostics, hosting and live updates as opt-ins. Run focused lifecycle/concurrency conformance tests, the entire relevant solution suite, package validation and actual local-package consumer acceptance before release.

Use FluxMQ-NG as a real consumer after the candidate package set is internally consistent. Do not send it half-migrated packages or publish another release merely because the old test suite is green.

## 8. Acceptance criteria

### Structural

- A direct C# application works with Core plus its chosen component module.
- Core has zero project references to JSON authoring, fluent authoring, Designer, protocol adapters or persistence providers.
- A simple component needs its implementation and one registration; extra metadata is opt-in.
- No parallel typed/canonical queue, port or request/reply implementation.
- One port/type declaration feeds runtime binding; editor metadata cannot redefine runtime facts.
- Runtime address lookup and component activation occur at build/start boundaries, not on every message.
- Document a reason for unusually large classes; a line limit alone is not an architecture rule.

### Correctness/concurrency

- Canonical request/reply works without typed-port casts.
- Concurrent requests in one trace return their own replies.
- Fast reply-before-send-return, timeout/reply races, cancellation, shutdown and late/duplicate replies settle correctly.
- Fanout preserves reference identity; mutations to caller-owned data after ingestion cannot affect queued payloads.
- Slow/full/faulted/detached targets follow the selected policy; no silent loss on a reliable route.
- Multi-source fan-in completes after all required producers, not the first.
- Cancellation releases blocked sends and pending waiters; graceful drain and forced stop are distinct.
- Unmatched conditional messages have an explicit sink/drop/error policy so a head-of-line value cannot stall a route indefinitely.
- Host-owned resources are never double-disposed.
- Every supported authoring path produces equivalent topology and behavior.

### Performance

Measure, do not promise a throughput multiplier from static code:
- 1, 8 and 32 hops; 1, 2 and 8 fanout targets.
- Small structured payloads, representative large payloads and opaque content.
- Conditions disabled/enabled; each evaluator; native predicate.
- Telemetry disabled/enabled, including slow exporters/listeners.
- Slow consumers, saturation, cancellation and revision swaps.
- Messages/second, p50/p95/p99 latency, allocations/message, retained heap, queued work and pending requests.

The hard goals are bounded memory, no payload-size-proportional copy per fanout branch, no repeated parsing of compiled expressions, and no hidden extra scheduler at each named scope. Set numeric targets from a reproducible baseline and the consumer's workload, not an arbitrary “thousands of messages” claim.

## 9. Static performance scan appendix

These recipes ran across the same 750 C# files. Counts are **matching source lines**, including possible comments and non-hot paths; they are not defect counts. The complete matching locations are in source-measurements.json.

### Scan execution checklist — before performance classification

| Recipe | Hits |
|---|---:|
| Literal IndexOf without comparison / Substring | 0 / 0 |
| Literal StartsWith or EndsWith / literal Contains | 0 / 0 |
| async void | 0 |
| Result/Wait/GetResult candidates | 5 |
| ValueTask lines for manual-review discovery | 651 |
| stackalloc / explicit new byte array / ArrayPool | 0 / 3 / 0 |
| string.Format / ContainsKey / IndexOfAny | 0 / 20 / 0 |
| JsonSerializer Serialize/Deserialize calls | 103 |
| Parameterless ToLower or ToUpper | 0 |
| Three chained Replace calls / char LINQ | 0 / 0 |
| params declarations | 5 |
| Static readonly Dictionary / FrozenDictionary | 0 / 0 |
| new List excluding static/readonly lines | 79 |
| new Dictionary excluding static/readonly lines | 204 |
| CurrentCulture comparer | 0 |
| Select/Where/Cast/Take/Aggregate lines | 160 |
| Explicit new HttpClient | 0 |
| new JsonSerializerOptions excluding static/readonly lines | 3 |
| Regex/GeneratedRegex signal | 0; regex-specific recipes not applicable |
| Sealed class / unsealed concrete declaration candidates | 443 / 20 |

Inverse check: **443 of 463 matching non-abstract/non-static class declaration candidates are sealed (95.7%)**. This is a lexical declaration count, not a compiler count of unique leaf types. Several unsealed classes are intentionally extended. No blanket sealing recommendation follows.

Manual false-positive checks:
- ApplicationPortRuntime reads Task.Result only after IsCompletedSuccessfully: not a blocking wait.
- Input admission's SemaphoreSlim.Wait(0) is non-blocking.
- RetryDirective.Wait is a domain factory, not Task.Wait.
- One match is event-name text, not a task access.
- FlowFanoutSource.Dispose does synchronously wait for its pump; async disposal exists. Treat as a shutdown-path review item, not proof of deadlock.
- All three explicit JsonSerializerOptions construction sites inspected are cached through static initialization. No per-message options-construction defect was found there.
- Source-generated serialization is not automatically possible for every arbitrary host-provided DTO. Keep serializer metadata/options extensibility; do not trade library usability for an unproven micro-optimization.
- ValueTask reuse, repeated enumeration and pooled-buffer ownership require control-flow/type analysis. Discovery was performed, but this review does not certify every such use.

### Prioritized performance opportunities

**Moderate P-A — repeated condition-context materialization.**  
Impact: avoidable dictionary allocations on every matched conditional route, compounded by optional parameters and engine bridging.  
Files: FlowValueApplicationOutputPort.cs:543, FlowMapContext.cs:17, ApplicationConditionCompiler.cs:80, JsonataFlowExpressionEngine.cs:45.  
Fix: immutable parameter bindings plus a small per-evaluation context; avoid duplicate input/root conversions.  
Caveat: benchmark each engine and preserve evaluation isolation.

**Moderate P-B — telemetry/event serialization and observation adaptation.**  
Impact: diagnostic creation and decoding can do substantial work even when the purpose is only logging/counting. Canonical output observations also construct typed envelopes; application event enrichment copies headers even when there may be no new attributes.  
Files: FlowEvent.cs:76, ApplicationEventStream.cs:26, ApplicationRuntimeInstrumentation.cs:29, FlowValueApplicationOutputPort.cs:400.  
Fix: direct standard instrumentation, immutable shared event metadata, and one observation boundary.  
Caveat: preserve event identity and consumer-owned reporting; do not remove domain events.

**Moderate P-C — fanout scheduling and topology snapshots.**  
Impact: delivery records, cancellation sources, task collections and per-message subscriber snapshots add work on a transport hot path.  
Files: FlowOutput.cs:54, FlowOutput.cs:267, FlowFanoutSource.cs:197, FlowValueApplicationOutputPort.cs:400.  
Fix: one Dataflow-backed dispatcher and immutable topology snapshots updated when links change.  
Caveat: reliability and dynamic-link semantics come before allocation reductions.

**Info P-D — content boundary copies.**  
Impact: duplicate temporary byte arrays may matter for large transport payloads.  
Files: FlowContent.cs:32, BoundedFileReader.cs:20, HttpClientNode.cs:369.  
Fix: benchmark a single ownership copy and reuse only private temporary buffers where safe.  
Caveat: never share mutable pooled storage with unconstrained fanout consumers.

**Info P-E — synchronous pump disposal.**  
Impact: a calling thread blocks while cleanup waits for the pump.  
File: FlowFanoutSource.cs:161.  
Fix: prefer the existing async disposal path in async owners; narrow/remove synchronous ownership requirements during consolidation.  
Caveat: no deadlock was reproduced.

Positive practices:
- Shared immutable headers already have a no-copy fast path.
- Canonical values own detached JSON storage.
- Expression compilation already has a reusable compiled form.
- Bounded execution and injected clocks are widespread.
- No async-void, explicit per-call HttpClient construction, or uncached serializer-options issue was confirmed in the inspected paths.

| Performance classification | Findings | Top issue |
|---|---:|---|
| Critical, experimentally confirmed | 0 | No runtime benchmark/reproduction performed |
| Moderate static opportunities | 3 | Repeated per-message context/event work |
| Informational candidates | 2 | Content ownership copies and sync disposal |

These results are AI-assisted static analysis. They may miss issues or include false positives. Verify performance changes with reproducible benchmarks, concurrency tests and human review. This appendix does not reduce the priority of the separate correctness and architecture findings.

## 10. Decisions recommended for the next implementation request

1. Approve one canonical runtime/envelope, with typed C# convenience only at the edges.
2. Approve separating JSON/fluent/designer/update responsibilities from Core.
3. Make reliable backpressure the ordinary workflow-link default; preserve rejection/observation behavior explicitly.
4. Make request/reply a canonical application capability with unique exchange correlation.
5. Prove the small end-to-end slice before migrating every module or publishing packages.

The objective is not “maximum abstraction” or “zero if statements.” Small guards and closed enum mappings are often clearer than another strategy hierarchy. The objective is that extending a module does not require editing unrelated code, and that an ordinary component author can understand exactly what executes, owns data, and owns lifetime.

