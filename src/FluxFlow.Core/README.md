# FluxFlow.Core

This is the framework package; optional components depend on it. Core includes
application hosting and native diagnostics. Health, OpenTelemetry/SSE and persistence
adapters are absent. Optional expression-language packages live under src/Expressions
and depend on Core. See the [repository layout](../../README.md).

Core owns DI-based application construction and resource access in
`FluxFlow.Core.Application`. ResourceRegistrationExtensions registers owned factories
or externally owned instances; FlowResourceExtensions resolves resource addresses.
Neither reads configuration. Reading
IConfiguration belongs to the optional FluxFlow.Configuration package through
AddFluxFlowApplicationAsync. It traverses sections directly and registers factories,
ports and links. No intermediate document model or document serializer remains.

Core also owns the mapping/expression contracts (`FluxFlow.Core.Mapping`).
Retry behavior belongs to the optional `FluxFlow.Components.Resilience` component,
which uses Polly. The unused custom retry policy/planner/executor in Core has been
removed; there is no parallel Core retry engine or forwarding package.

Message contracts live together in `Messaging/`: `FlowMessage`, `FlowValue`,
`JsonFlowValue`, `FlowError`, their JSON converters and message
identifiers. Processing blocks remain in `Nodes/`. These are all part of this
project. Namespaces follow folders: `FluxFlow.Core.Messaging` and
`FluxFlow.Core.Nodes`. Application, Execution, Coordination, Mapping and Addressing
likewise use their folder paths beneath `FluxFlow.Core`. There is no Core.Configuration
folder or namespace; configuration-specific exceptions belong to FluxFlow.Configuration.

Ordinary composition uses the host service collection and one standard provider:

Build requires a registered FlowApplication (component/workflow registration adds it).
To customize capacity, register `services.AddSingleton(_ => new FlowApplication(256))`
before registering configuration/components. Build without a capacity preserves that
setting and returns the same application on subsequent calls. DI owns disposal.

```csharp
services.AddComponentFactory("main.node", provider => new MyNode())
    .AddInputPort<MyNode>("main.node", "Input", node => node.Input)
    .AddOutputPort<MyNode>("main.node", "Output", node => node.Output);
```

Component factories are synchronous and lazy. Native keyed IFlowNode singletons own
component identity; native input/output singleton registrations select their actual
blocks once. Named events resolve the existing bridge output with address headers.
No component factory receives Application or Workflow, and components never receive
a service locator. Asynchronous activation belongs in startup.

BuildApplicationAsync resolves the provider's single Application and composes it once.
It creates workflows, resolves components/ports and invokes LinkFactory with the same
host provider. Repeated Build calls return that same Application without rerunning
factories or links. StartAsync connects prepared routes and starts sources.
Application.Workflows exposes real workflow instances in a read-only snapshot.

Stop/dispose Application before disposing the host provider, even if never started.
Application drains processing; native DI owns component/resource disposal, including
after failed composition. Standard DI stops disposal at its first disposal failure.
There is no custom aggregate-disposal manager. Component dependencies must have
lifetimes compatible with singleton components. Resource factories are registered
transactionally during Add, not during Build. Constructors that throw must clean up
resources acquired outside DI before returning.

LinkFactory accepts IServiceProvider. The native blocks and components are available
by full address, alongside host dependencies. Link and LinkFallback
prepare routing with fanout, delivery policies and coordinated completion. Each output
retains its routes. There is no temporary port provider or ordinary resource provider.
Hot reload, revision candidates, stable revision ports and isolated candidate providers
are removed. Ordinary startup, cancellation and failed-build cleanup remain.

FlowInputEndpoint, FlowOutputEndpoint, ComponentActivationContext, ComponentInstance
and application/workflow GetInput/GetOutput are removed. FlowLink exposes native
Input/Output blocks with InputAddress/OutputAddress. GetComponent is a composition
extension; DI-backed applications resolve the actual node from their host provider.
Component contracts contain no RegisterPorts method. TerminalInputs carries lifecycle
identity, not application naming. Workflow input/output dictionaries and manual
Add/Register APIs are removed. DI is the only address lookup; Workflow retains
components, link declarations and attached event observations.

Registered input/output blocks must not implement IDisposable or IAsyncDisposable.
Their component owns any cleanup; the provider owns the component. Port selectors
reject disposable blocks before DI captures them, preventing duplicate disposal through
aliases. Named events expose the existing bridge's non-disposable output. No ownership
wrapper or custom disposal manager is involved.

```csharp
services.AddSingleton<LinkFactory>(ports =>
{
    ports.Link("main.source.Output", "main.archive.Input");
    ports.Link("main.source.Output", "main.review.Input",
        when: message => NeedsReview(message));
});
await using var provider = services.BuildServiceProvider();
await using var app = await provider.BuildApplicationAsync();
await app.StartAsync();
```

Predicates are normal synchronous C# delegates. Build connects declarations to
registered native blocks; Start connects prepared routes and starts sources.
There is no manual application input lookup/send API. Tests may send directly
to the blocks they constructed; that is native Dataflow behavior, not a runtime
application ingress feature. Output route state remains under review separately.

Custom Dataflow components can use `FlowTracing.StartProcessing(message, component)`
for one message, or `FlowTracing.StartProcessing(messages.AsSpan(), component)` for
one aggregate operation. Dispose the returned scope in the same logical async
operation, across the awaited work it measures. The multi-message form starts a
root with links to valid input contexts; it does not choose one input as the parent.
`Failed`, `Canceled` and `Outcome` record operation results. `SuppressContext` clears
an inherited worker activity for non-message commands. On disposal it restores a
still-running saved activity, or clears the context if that activity has stopped.
The built-in processing wrappers already do this; do not wrap them a second time.
These helpers add no SDK/exporter and retain no input collection after starting.

Development preview of the instance-first application kernel. This project now owns the former foundation, keyed-exchange source and shared application addresses. No forwarding package or compatibility assembly is introduced.

Register ComponentFactory and native port selectors in DI. A LinkFactory calls
ports.Link. Configuration registers these delegates while reading each component section.
Build through IServiceProvider. The same node instance cannot be registered again under
another name, workflow or provider. A weak reference-identity guard rejects such aliases
before DI captures disposal; native DI alone caches instances.

One host provider owns the components and resources. Hot reload, revision candidates
and rollback are outside this minimum framework.

For standard components, graph routes share the component's existing `FlowOutput<FlowMessage>` dispatcher. There is no second per-output graph queue: the component's output capacity is authoritative. The application constructor's `linkCapacity` bounds only the adapter required for a custom native source; that adapter uses the same output implementation and counts active plus queued work. Standard output capacity retains its waiting-items-plus-one-active contract. Removing the extra graph queue intentionally tightens backpressure for standard components.

Graph predicates run once per emission, including repeated sends of the same message instance. Selected graph targets use native `SendAsync` admission; `RejectWhenFull` uses native immediate admission. The native sender owns postponement, reservation and cancellation, while public source links retain their native filtering and callback contract. Both kinds of links share the same dispatcher and fanout loop. Independent sibling deliveries start before a delivery failure is observed; native offer scheduling can still race cancellation, and fanout is not atomic. Fan-in lifetime coordination remains separate from route selection, and fallback is used only when no ordinary route matches.

An explicitly isolated route attempts immediate admission and reports each rejected
message or condition error without faulting healthy siblings:

```csharp
ports.Link("main.source.Output", "main.target.Input",
    delivery: FlowDeliveryPolicy.IsolateRejection,
    onRejected: (message, error) => RecordRejected(message, error));
```

`Link` and `LinkFallback` accept the same policy and required handler. Reliable
delivery remains the default; `RejectWhenFull` still faults the graph on rejection.
The isolated route stays linked for later messages, so temporary saturation can
recover. Rejected work is not retried, cloned, acknowledged as processed, or sent to
fallback after failed admission. An isolated condition error skips that route and
is reported; normal fallback selection may still apply. A condition returning false
is not an error and does not implicitly enable discarding unmatched messages.

The handler receives the original envelope and exception (or an admission error
naming both ports). It runs synchronously, must return promptly, and owns any
asynchronous work it starts. Handler exceptions are contained; Core does not add a
reporting queue or retry failed reporting. Actual component lifetime failures still
fault the graph, regardless of route policy. LinkFactory delegates can supply the
handler through Link or LinkFallback. There is no update-host rejection stream or
intermediate document builder in this package.

This development API adds an optional handler parameter to the address-based link methods
and exposes it on `FlowLink.OnRejected`. Existing source calls retain their default
behavior, but previously compiled callers must rebuild for the changed signatures.

Core provides addresses, routing/lifetime and the component-owned request/reply helper. Business components have no configuration or hosting dependency.

Processing components share an internal output/lifetime owner. `FlowNode` retains
its action-style `ProcessAsync`, explicit emissions, error conversion, extra outputs
and final-input flush hook. `FlowTransformNode` supports zero or many results: override
`TransformAsync`, return a sequence of messages, and let native TransformManyBlock
preserve input order when ExecutionDataflowBlockOptions.EnsureOrdered is enabled.
Processing exceptions become error messages; explicit faults and delivery failures
remain visible through completion. Both wrappers expose actual ports and wait for active
work before releasing owned resources. They do not own borrowed client dependencies.
The shared helper creates no scheduler, subscription API or component creator.

Both processing wrappers emit optional `flow.process` activities from the
`FluxFlow.Core` source. New messages capture W3C context in existing immutable
headers; forwarding preserves the same envelope and creation context. Host sampling
and export stay outside Core. See [processing trace context](../../docs/processing-trace-context.md)
for exact duration, derivation, failure and coverage boundaries.

`FlowSource` has one finalization path for normal production, early stop and faults.
Stopping signals its token immediately; registered cancellation callbacks execute
asynchronously. `Completion` joins both the producer and those callbacks, then all
owned data outputs. Natural production end also signals the stopping token. Owned
resource cleanup runs afterwards, even when a callback fails; callback errors are
retained alongside an earlier production/output fault. This does not join detached
work started by user callbacks. A stopped or disposed source cannot restart.
Concurrent repeated disposal retains the existing first-caller behavior.

Application lifetime coordinates sequential source startup, idempotent graceful
stopping, first-failure propagation and completion observation. Controlled sources
stop alongside entry components; a composed completion cannot hide its underlying
node's fault or discard its additional work. Core faults routing before components
release reservations. Native DI remains the component disposal owner.

Graph failure stops business routing before component faults release held reservations. If a native source has already started graceful completion, a fault-only native discard link releases its unread output so that completion is not stranded. Discarded messages are failed work, not processed or acknowledged work. All registered components participate in shutdown and cleanup.

The existing ApplicationAddress and ApplicationAddressKind types now belong to Core under `FluxFlow.Core.Addressing`. Update the address namespace import and rebuild consumers. Absolute/local port strings, resource/system addresses, ordinal comparison and validation rules are unchanged. Direct graph and port naming use the same Core segment rules; no configuration or host dependency is needed to resolve an address.

`FlowRequestReply` is a bounded helper for components that own request/reply exchanges,
such as Retry and RateLimit. The component sends through its Request output and
receives through its Reply input. Registration precedes admission; each exchange
gets an independent correlation and a deadline covering admission and waiting.
Cancellation and component disposal settle pending exchanges.

Application owns lifecycle and links, not requests. MakeFunction, FlowFunction,
CallFunction, app.RequestAsync and ApplicationPorts.SendAndReceiveAsync are removed.
There is no automatic reply-output handling or fallback suppression. All output
messages use ordinary links and explicit unmatched-output policies.

This preview is not release-ready. Shared dispatch and minimal package execution
are verified, while the final capability audit, wider operational/telemetry and
performance qualification, provider prerequisites and migration handoff remain.
See the repository execution ledger for current evidence and limits.

The retained primitive documentation is in `Nodes/README.md`; exchange implementation documentation is in `Coordination/README.md`. Those implementation namespaces do not represent separately shipped packages in the new package train.

The canonical value converter is accessible to external generated JSON contexts. Scalar, array and object shapes are preserved; JSON `null` deserializes as `JsonFlowValue.Null`. This verifies canonical-value serialization, not whole-library trimming or ahead-of-time compilation compatibility.

FlowValue is an abstract immutable-value contract: Json is produced lazily by CreateJson and cached across concurrent readers. JsonFlowValue owns dynamic JSON. Components perform their own deserialization; the materializer interface/result/base classes are removed. HTTP/MQTT requests and FlowError can travel as typed FlowValue objects. Canonical error messages expose the error through the same Value slot, with IsError retaining failure semantics. See docs/20-flow-data-contracts.md for migration and serialization details.

## Link cycle validation

Application validates business links during composition and startup. The internal
FlowCycleDetection helper returns only whether a cycle exists; it does not construct
dependency layers or determine component execution order. Native Dataflow messages
drive execution. Links to a component's TerminalInputs are excluded from this cycle
check, preserving control/reply paths. Ordinary business cycles remain unsupported.

Runtime revisions, rollback and hot reload are absent from this minimum framework.
There is no Core.Runtime namespace or separate Engine/forwarding assembly. See the
current Application, Execution and Coordination folders for runtime responsibilities.
