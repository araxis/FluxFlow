> Historical API material: the authored definition-builder/handle layer is removed. Use [current registration](40-component-registration.md); old C# builder examples below are not current APIs.

> Historical API material: live revisions and the update host are removed from the minimum framework. See [current hosting](05-hosting-and-observability.md); old snippets below are not current setup instructions.

> Historical coordination material: PendingExchangeCoordinator and its public models were removed on 2026-09-22. Component request/reply uses [FlowRequestReply](../Code/src/FluxFlow.Core/Coordination/README.md); the tracker migration below is historical.

# Code-First Major Migration

Current registration API: [component registration](40-component-registration.md).
The development API removes ComponentActivationContext and ComponentInstance, including
the obsolete physical port wrappers. Ordinary and revision builds share native DI construction.
Later sections retain historical migrations for modules absent from the minimum baseline.

## Direct component registration

```csharp
services.AddComponent("sample.uppercase", (registrations, address, definition) =>
{
    registrations.AddComponentFactory(address, _ => new UppercaseNode())
        .AddInputPort<UppercaseNode>(address, "Input", node => node.Input)
        .AddOutputPort<UppercaseNode>(address, "Output", node => node.Output)
        .AddEventPort<UppercaseNode>(address, "Diagnostics", node => node.Events);
});
```

ComponentFactory receives only IServiceProvider and returns the node synchronously.
The ValueTask factory overload is removed; use startup for asynchronous activation. Registration callbacks receive
IServiceCollection, the component address and ComponentDefinition. ApplicationSetup's
callback property is now ComponentRegistrations. It holds registrations, not constructors.
Bind configuration in the component's registration method. If supporting processing profiles,
resolve them with services.ResolveProcessing(reference, componentAddress, capabilities),
then apply explicit component settings over the returned defaults. The document-based
BindConfiguration helper is removed.
Node Completion/DisposeAsync own component work and cleanup. No SetLifetime replacement exists.

## Component simplification and observability checkpoint

This remains an unpublished breaking development preview. In addition to the
Definition rename below, the FileSystem, Storage, Sessions, Metrics and Projections
packages no longer expose their thin request materializer classes. Component-owned
parsing now handles those requests locally. Input-shape metadata and its helper classes have also been removed.
Custom components read FlowValue.Json and deserialize locally; the materializer interface and result/base classes are removed. Port registration uses native blocks.
Code that constructed materializers must migrate to local System.Text.Json conversion; no
compatibility wrappers are retained. This does not resolve the deferred Sessions
design concern.

`MqttPublishOperationNode` now derives from `FlowTransformNode` rather than
`FlowNode`; consumers depending on its previous base type must update. The
component remains a one-result operation. `FlowValueTransformNode` is an optional
payload-only base over the same transform runtime; use `FlowTransformNode` when
processing needs the original message.

Observability additions are additive: local materialization failures can retain
an original Exception, and Core exposes diagnostics and metric identity. Optional
host adapters register the OpenTelemetry SDK and the net10 SSE endpoint. See
[observability setup](46-observability-host.md). None requires telemetry boilerplate
inside ordinary component operations. Private/internal helper declaration changes
in the source baseline are not public binary compatibility claims.

## Definition namespace rename

The optional application and workflow construction API now uses
`FluxFlow.Composition.Definition` instead of `FluxFlow.Composition.Authoring`.
Update imports and explicit references to the renamed `*DefinitionExtensions`
classes. `ApplicationDefinitionBuilder` and `WorkflowDefinitionBuilder` retain
their names and behavior. Package names remain unchanged.

Source projects now live under `src/Definition`; component definition helpers
and the Composition namespace folder also use `Definition`. This is a breaking
source/API rename in the unpublished development version, with no compatibility
aliases. Historical qualification reports describe the earlier names and binaries.

This guide covers the breaking definition changes in the code-first
simplification prerelease. It is for applications and component packages moving
from the previous Composition, Engine, Fluent, and durability lines.

Portable JSON remains supported. The major change is the compiled C# definition
surface and the removal of duplicate declarations and registrations.

## Direct construction for operation components

### Documents and existing contracts can build Core directly

Use `await setup.BuildApplicationAsync(services)` (or the same overload on an
`ApplicationDefinition` or `ApplicationDefinitionBuilder`) when optional definition
uses existing component/resource contracts. Embedded descriptors and registered
catalog entries share the existing activation path; resource registrars are reused
and deduplicated by identity. No second constructor-binding registry or update
runtime is required. The returned value is an ordinary `FlowApplication`.

The application owns constructed components and their construction resource scope.
Disposal releases components and additional activation cleanup before resources;
host services remain borrowed. Native source startup, terminal controls, named
event outputs, compiled conditions, rejection policies and Core request/reply are
preserved. Typed caller predicates materialize their argument from the canonical
value, without replacing the transported message. Explicitly discard unmatched
messages on outputs where filtering is intentional.

Preflight checks declared ports, links, required option presence and resource-key
shape. It cannot validate arbitrary domain options or prove an external resource
exists without the component/registrar. The former `FlowJson.BuildAsync` constructor
binding path is removed: use DI ComponentFactory registrations and LinkFactory with
FlowJson.ApplyLinks, then provider.BuildApplicationAsync. Rich resource documents still
use the older context-based Composition overload pending migration. The composition sample demonstrates both
C# and JSON without an Engine project dependency. Live update/revision behavior
remains an optional, separately tracked migrationâ€”not a requirement for construction.

### Terminal outputs

Application-level request/reply and the built-in function component have been
removed. Components own their request/reply exchanges through ordinary links.
There is no special reply classification or automatic fallback suppression.

The workflow-level `DiscardUnmatched` policy and its C#/JSON/revision declarations
are removed. Messages with no ordinary or fallback route retain the existing routing
error. No new build-time unconnected-output rule or automatic error collector is
introduced by this removal. Use real terminal consumers where the application needs
them; components that expose no output need no output declaration.

The older low-level ApplicationPortRuntimeBuilder output-drain option remains separate
from workflow declarations, including its system-output observation behavior. Its
future policy belongs in the deferred error/output discussion.

### Canonical optional port declarations

For optional Composition/Designer bindings, rename `HasFlowValueInput` and
`HasFlowValueOutput` to `HasInput` and `HasOutput`. Input shape and link cardinality
overloads remain. Instance declarations use `HasInput(name)` and `HasOutput(name)`
without type parameters. Shape omission remains valid; an explicit null shape fails
before registration can add that port.

`ComponentInputPort` and `ComponentOutputPort` are sealed, directly constructible
holders of native canonical blocks. Replace `ComponentPorts.FlowValueInput` and
`FlowValueOutput` with `Input` and `Output`. `SignalInput` uses the same input class
with terminal-control kind. Its semantic metadata remains `object`; its physical
block is still `ITargetBlock<FlowMessage>`. Typed physical port classes are removed,
not adapted through extra queues. Existing typed caller conversions remain at the edge.

These declarations belong to optional definition. They are not required by direct
`FlowWorkflow.Add(name, component)` construction. Event ports, preflight shapes,
presentation metadata, revision binding and business/control lifetime distinctions remain.

### Instance-first operation components

In the current development version, all six serialization nodes, file read/write,
all four storage operation nodes, and session recorder/query implement
`IFlowComponent`. Construct them normally and pass the instance to
`FlowWorkflow.Add(name, instance)`. Their `Ports` property exposes the same native
Input/Output/Events blocks as their existing properties. No creator, port-selector
registration, definition package or host is required for this direct Core path.
Explicitly supplied storage/session dependencies remain caller-owned.

Serialization now forwards an upstream error as the exact original `FlowMessage`.
It does not create a new message ID or causation edge for a conversion it skipped.
Successful conversions and newly generated conversion errors still derive results.
Update assertions or consumers that expected an extra envelope on pass-through.

This checkpoint does not claim that every special wrapper has migrated.
Optional definition-based definition below remains available
and is still being consolidated; it is not required for these direct instances.

## Canonical retry component

Replace `FlowRetryNode<T>` with `FlowRetryNode`. Input and Output now carry
canonical `FlowMessage`; create input with `FlowMessage.Create(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))`
and materialize successful output values locally as `RetrySignal<T>`.
The internal duplicate wrapper and copied input envelope/transform queue are removed.
The same public class is constructed by direct code and optional document activation.

Ack, Nak and Cancel are bounded native `ITargetBlock<FlowMessage>` inputs.
Replace custom signal-target calls/handles with native sends/links and canonical
`InputPortHandle<FlowValue>` definition handles. Preserve trace and attempt headers
when deriving feedback, for example `attempt.WithValue(JsonFlowValue.Null)`.
Do not treat a successful send as successful settlement: it means admission.
Stale/missing-attempt feedback is admitted then reported as ignored in Events;
matched feedback produces the existing retry result on Output. Rejected admission
means the target is closed; a canceled pending send reports cancellation.

All four ports are business inputs, because feedback may emit another result.
Completion drains admitted feedback before stopping attempts; infrastructure faults
close sibling inputs and remain observable on component Completion. Direct native
input Completion is its own block's lifetime, not an alias for component Completion.
Existing cycle rejection remains; this change does not add arbitrary feedback loops.

## Canonical timer components

Replace `TimerDelayNode<T>`, `TimerThrottleNode<T>` and `TimerDebounceNode<T>` with
their non-generic names. These are now canonical `FlowNode` implementations, not
typed specializations. Construct them with the same settings and optional clock;
send `FlowMessage.Create(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))`. The interval and schedule source
nodes also implement `IFlowComponent`, with Output/Events only. All five can be
passed directly to `workflow.Add(name, instance)` without registration or creators.
Optional document registration constructs these same public classes.

Successful timing operations forward the original envelope and payload, including
binary content. They do not create a new message ID or causation edge. Upstream
errors bypass timing and retain exact identity. New timing failures still derive
an error result. Update consumers that expected a new envelope for a pure delay.

Operator input capacity now counts the executing delegate as well as queued input.
The extra general input buffer is gone; delay and debounce retain only the bounded
queues needed for scheduling/emission. Graceful completion drains accepted work
and flushes the latest debounce value. After fault/disposal, owned timers and
scheduled work are released/joined by disposal. Supplied clocks remain borrowed.

## Direct file sources and inbound request bridges

Directory enumeration and file watching now inherit `FlowSource`. Construct them
with their existing options, add the instances to a workflow and link Output.
The application starts them; no input placeholder or source-pipeline wrapper is
required. Watcher resources remain owned by the source's production lifetime.

`RequestReplyCoordinator<TRequest, TResponse>` and `HttpTriggerNode` are directly
addable source/control components. The transport's `IRequestContext` remains typed,
but graph Output and Responses now use canonical `FlowMessage` values. Replace
typed graph handlers with `FlowValue` materialization at the handler boundary and
produce replies with `request.WithValue(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(reply, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))`. Manual bridge/trigger
use must call `StartAsync()` after linking; application and keyed hosted startup
do this automatically. A custom response converter is optional and runs once in
the bounded caller task after exchange matching, without an adapter queue.

For a direct graph, link `handler.Output` to `bridge.Inputs["Responses"]` on the
component returned by Add. `StopProduction()` stops ingress without closing that
terminal response control; it stays open until business work drains. `Complete()`
is a full bridge shutdown that settles remaining callers, not a production-only
stop. Hosts retain ownership of their external request source and submissions.

The bridge now uses Core's `FlowRequestReply`. Every graph exchange gets a fresh
correlation and expected reply route; the external context keeps its original ID.
Reused or concurrent transport IDs no longer cross-match replies. Always build
responses from the graph request envelope. Fire-and-forget retains its previous
optional external correlation behavior. Core's `Reply` returns whether it settled
an exchange; late, duplicate and wrong-route replies return false.

Replace `new HttpTriggerNode(source, options)` with `new HttpTriggerNode(options)`
and submit contexts to `trigger.Incoming`. The redundant `HttpTriggerSource` and
its keyed registration are removed. If you explicitly supply another source,
you own its linking and lifetime. Hosted stop closes the actual input and awaits
accepted caller notifications. Admission rejection returns 503; accepted callers
failed by shutdown retain the existing 500 response mapping.

Unexpected ingress extraction/serialization errors settle all accepted queued
callers. Caller task capacity includes slow reply/failure callbacks, not only
pending exchange matches. Completion joins callbacks; diagnostics cannot trigger
a second terminal notification. This is not full release qualification.

## Retired correlation tracker and sweep option

The development major removes `CorrelatedRequestTracker<TContext, TResponse>`,
`CorrelatedRequestTrackerOptions`, `CorrelatedRequestStartResult`,
`RequestReplyOptions.SweepInterval`, and `RequestReplyErrorCodes.DuplicateCorrelationId`.
The tracker is no longer used by the bridge. SweepInterval did not affect deadline
scheduling. Remaining diagnostic numbers are unchanged, not renumbered.

Use application `RequestAsync` / Core `FlowRequestReply` for graph exchanges, or
the existing bridge plus `IRequestContext` for automatic transport callbacks.
Advanced consumers that require their own keys can use the existing Core
`PendingExchangeCoordinator<TKey, TContext, TOutcome>` directly:

| Previous tracker operation | Existing keyed primitive / consumer responsibility |
|---|---|
| TryAdd | TryStart; inspect Status, then await the accepted Completion. |
| TryCompleteAsync | TryResolve; Completion retains typed context and outcome. |
| TryFailAsync | TryFault; Completion retains the exact error. |
| TryRemove | TryCancel; inspect the returned completion context and handle or ignore Cancelled as appropriate. |
| FailAllAsync | FaultAll settles and returns the current batch without stopping new starts. |
| DisposeAsync | DisposeAsync settles pending entries and releases the deadline timer. |

The keyed primitive owns settlement, not consumer callbacks. Await your callback
after Completion with your chosen token; normal async exception propagation
replaces the old callback-wrapper API. Own and join those callback tasks explicitly,
also when FaultAll or timer disposal throws after settling entries. Independent
batches can complete notifications independently. The transport bridge continues
to provide automatic bounded callback ownership, so normal adapters do not need
to recreate that bookkeeping. No replacement callback broker is introduced.

Keyed operations require non-null keys and contexts; protocol-specific rules such
as rejecting an empty external ID belong at that consumer's boundary. Prefer the
graph exchange API when you want the library to mint the exchange identity.

Options remain a sealed immutable record with validating init accessors. Construct
or update Mode, Timeout and Capacity directly; remove SweepInterval assignments
and stored fields. Deadline behavior is unchanged by removing that no-op setting.

## Port metadata no longer dispatches runtime types

The current development major removes `IComponentPortTypeVisitor` and
`ComponentPortMetadata.SupportsTypeVisit`/`Accept`. Metadata is plain data;
constructors and `Create<T>` describe equivalent ports. Read `Kind`, `MessageType`,
`LinkCardinality` for inspection instead of registering
a type-dispatch callback. Canonical hosted business ports bind directly to
`ITargetBlock<FlowMessage>`/`ISourceBlock<FlowMessage>`; control inputs retain their
separate kind and forward the original canonical envelope through DI views.
Signal outputs and typed business runtime blocks remain unsupported by the host.

This is an intentional breaking prerelease change, not a compatibility shim.
It does not complete the remaining instance-first definition/runtime migration.

## Choose the definition path first

Use typed C# when the application is compiled with its workflow definition and
should use C# types, variables, predicates, factories, and dependency injection.
Use portable JSON when the definition must be stored, designed, edited, loaded,
or hot-reloaded as data.

The paths converge on the same Engine, but they do not serialize into each
other. A C# definition may own executable delegates and contract instances; a
JSON definition never does.

## Application registration

Builtin AddX methods embed their factory and return typed handles. Custom packages
can provide the same small extension over workflow.AddComponent(name, type,
factory, configure, createHandle). AddFluxFlowUpdates(setup) or
the revision host consumes those callbacks without a second registration. JSON hosts register factories explicitly in DI.

## Component package declarations

Register a deferred AddComponentFactory and explicit AddInputPort/AddOutputPort/AddEventPort
selectors. The selectors receive the actual node. Address names and optional signal/cardinality
settings belong to registration; the component owns native blocks and processing only.

## Typed application links

Replace string targets such as `"sink.Input"` with captured handles:

```csharp
workflow
    .AddComponent("source", SampleComponents.Source, out var source)
    .AddComponent("upper", SampleComponents.Uppercase, out var upper)
    .AddComponent("sink", SampleComponents.Sink, out var sink);

source.Output.LinkTo(upper.Input);
workflow.Connect(upper.Output, sink.Input);
```

Use `application.Connect(...)` for an explicit cross-workflow connection. A C#
condition may be supplied directly:

```csharp
upper.Output.LinkTo(
    sink.Input,
    when: static value => value.Length > 0);
```

Portable expression strings remain available for JSON-compatible conditions.

## Typed runtime and durability operations

Keep the handles captured during definition. `ApplicationPorts` accepts typed
input, signal-input, and output handles for send, receive, observe, and
request/reply operations. Durable input enqueue and durable output capture also
accept the corresponding typed handles.

These overloads use the same canonical addresses and runtime implementation;
they do not create a second routing or durability system.

## Application resources

Code-first resources use package-owned `ApplicationResourceContract` values.
The definition retains their explicit registrars and the Engine owns their
revision lifetime. A code-first MQTT client, for example, does not require a
second host-side MQTT registrar call.

JSON cannot carry executable registrars. A JSON host must continue to register
the package behavior that may be named in its data.

## JSON applications

The JSON path remains explicit:

```csharp
var definition = JsonSerializer.Deserialize<ApplicationDefinition>(json)!;

services
    .AddSources();

services.AddFluxFlowUpdates(definition);
```

Keep explicit family registration here. It is the trusted catalog boundary for
portable type names and does not represent duplicate code-first registration.
JSON persistence and hot reload remain data-only and never embed CLR delegates,
component contracts, resource contracts, or service providers.

## Custom factory registration

Use services.AddComponent(type, registration). Registration adds native factory and port
selectors; it returns no component. BuildApplicationAsync owns construction and rollback.
No ComponentInstance or activation context remains.

## Retired Fluent applications

Remove FluxFlow.Fluent. Construct FlowApplication, add workflows and component
instances, and connect endpoint objects with LinkTo. FlowGraph, FlowBuilder,
FlowTerminal, FlowSegment and Flow.From are removed without compatibility aliases.
Replace Then/Branch/Tap with explicit links; reuse ordinary C# methods to construct
repeated structures. Where To/Tap previously discarded terminal outputs, explicitly
connect those outputs to explicit terminal consumers. Preserve strict unmatched routing elsewhere.

Replace FluxFlow.Fluent.Hosting/AddFlowGraph with FluxFlow.Hosting/AddFlowApplication.
The factory now returns FlowApplication directly. For optional merged event
observation, construct FlowEventStream (FluxFlow.Observability) from selected
component Events sources and dispose it independently. Native Events.LinkTo remains
available. See samples/FluxFlow.DirectSample for linear and branching examples.

## Readiness

Application readiness is optional. Add the health package and call its standard
registration only when a host needs a readiness check. The adapter observes
existing application state; it adds no worker, polling, storage query, or
endpoint by itself.

## Native terminal-control inputs

`IFlowSignalTarget` is removed. Expose the existing native
`ITargetBlock<FlowMessage>` from a component and pass it directly to
`AddInputPort(..., signal: true)` or control-target registration.
Keyed DI consumers now resolve `ITargetBlock<FlowMessage>` for both business
and control inputs. Non-owning registration still leaves disposal with the
component or external owner.

Control handles, addresses and `Signal` metadata remain. Control classification
preserves terminal lifetime: retiring a hosted attachment or completing the
hosted input does not complete the component-owned control target. Business
input completion still completes and joins its target. This is not permission
to classify data-producing feedback as a terminal control or bypass cycle checks.

Both kinds now use the same canonical hosted admission, delivery and revision
path. Typed `SendAsync`/`TrySend` convenience converts at the caller boundary;
the original CLR payload instance no longer travels through a separate generic
signal envelope. Payloads must be representable as `FlowValue` just as for other
inputs. Send an existing canonical message to preserve its identity and immutable
payload/header references. Error messages retain their error and envelope data.
Admission success is not a promise that a component has finished processing.

The internal signal proxy and unused Composition link/forwarder implementation
are removed; use addressed application sends or the actual native component
target, according to the intended ownership boundary.

## Migration checklist

1. Upgrade the affected package closure to one consistent prerelease wave.
2. Replace old component contracts with direct context factories.
3. Rename declaration mappings from `Add*` to `Has*` and declare Events
   explicitly.
4. Build applications with captured typed handles and typed connections.
5. Remove repeated code-first component and resource registration.
6. Keep explicit package registration for JSON hosts.
7. Move raw registrations to `AddComponent`.
8. Use typed runtime and durability overloads where the definition handle is
   available.
9. Replace Fluent hosting with `FluxFlow.Hosting` and `AddFlowApplication`, supplying the direct Core application.
10. Run unchanged, rejected-reload, retained-route, shutdown, and durability
    recovery tests before promoting from the prerelease.


## Component registration cleanup complete

All 45 builtin registrations and all custom examples now use context factories.
All 44 builtin typed AddX methods remain. The legacy component contract, binding
selector, activation wrapper and catalog APIs are removed globally. C# definitions
and DI share the same callback, including async owned-resource cleanup and HTTP
parallel capability. Resource contracts remain unchanged. The execution ledger
retains the intermediate migration history.
