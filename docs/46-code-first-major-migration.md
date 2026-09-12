# Code-First Major Migration

This guide covers the breaking authoring changes in the code-first
simplification prerelease. It is for applications and component packages moving
from the previous Composition, Engine, Fluent, and durability lines.

Portable JSON remains supported. The major change is the compiled C# authoring
surface and the removal of duplicate declarations and registrations.

## Direct construction for operation components

### Documents and existing contracts can build Core directly

Use `await setup.BuildApplicationAsync(services)` (or the same overload on an
`ApplicationDefinition` or `ApplicationDefinitionBuilder`) when optional authoring
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
exists without the component/registrar. `FlowJson.BuildAsync` with constructor
bindings remains the smaller resource-free path; rich resource documents use the
optional Composition overload above. The composition sample demonstrates both
C# and JSON without an Engine project dependency. Live update/revision behavior
remains an optional, separately tracked migration—not a requirement for construction.

### Explicit terminal and reply outputs

An emitted message must have a matching business link, a fallback, an explicit
discard policy, or an explicit reply role. Zero configured links do not imply
discard. Capturing or observing an output does not count as business delivery.
A sink that exposes no output requires no additional declaration.

For optional fluent definitions, declare filtering and reply intent before Build:

```csharp
source.Output.DiscardUnmatched();
applicationBuilder.RepliesFrom(responder.Output);
```

The corresponding flat component properties are:

```json
{
  "source": { "Type": "sample.source", "DiscardUnmatched": ["Output"] },
  "responder": { "Type": "sample.responder", "RepliesFrom": ["Output"] }
}
```

Both lists contain exact local output names, not addresses. Empty arrays configure
nothing. Wrong casing, duplicate entries, non-output names and differently cased
duplicate policy keys are rejected. These two property names are reserved in
optional component port, option and resource metadata. Direct Core component
construction does not require this document vocabulary.

Discard applies only when no ordinary link or fallback handles the message. Reply
outputs must be declared before request/reply calls; late, duplicate or unsolicited
reply traffic does not select a fallback. Matching ordinary links still deliver.
Named event outputs used only for observation can explicitly discard unmatched
messages as well. System event and diagnostic outputs retain their best-effort
host policy.

The same declarations apply to direct document/contract activation and optional
update hosting. Revision preparation retains immutable policy sets; a policy-only
change becomes effective at activation, not when the candidate is prepared.
The composition sample now carries discard intent in both its fluent definition
and JSON, without post-construction patching.

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

These declarations belong to optional authoring. They are not required by direct
`FlowWorkflow.Add(name, component)` construction. Event ports, preflight shapes,
presentation metadata, revision binding and business/control lifetime distinctions remain.

### Instance-first operation components

In the current development version, all six serialization nodes, file read/write,
all four storage operation nodes, and session recorder/query implement
`IFlowComponent`. Construct them normally and pass the instance to
`FlowWorkflow.Add(name, instance)`. Their `Ports` property exposes the same native
Input/Output/Events blocks as their existing properties. No creator, port-selector
registration, authoring package or host is required for this direct Core path.
Explicitly supplied storage/session dependencies remain caller-owned.

Serialization now forwards an upstream error as the exact original `FlowMessage`.
It does not create a new message ID or causation edge for a conversion it skipped.
Successful conversions and newly generated conversion errors still derive results.
Update assertions or consumers that expected an extra envelope on pass-through.

This checkpoint does not claim that every special wrapper has migrated.
Optional definition-based authoring below remains available
and is still being consolidated; it is not required for these direct instances.

## Canonical retry component

Replace `FlowRetryNode<T>` with `FlowRetryNode`. Input and Output now carry
canonical `FlowMessage`; create input with `FlowMessage.Create(FlowValue.From(value))`
and materialize successful output values locally as `RetrySignal<T>`.
The internal duplicate wrapper and copied input envelope/transform queue are removed.
The same public class is constructed by direct code and optional document activation.

Ack, Nak and Cancel are bounded native `ITargetBlock<FlowMessage>` inputs.
Replace custom signal-target calls/handles with native sends/links and canonical
`InputPortHandle<FlowValue>` authoring handles. Preserve trace and attempt headers
when deriving feedback, for example `attempt.With(FlowValue.Null)`.
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
send `FlowMessage.Create(FlowValue.From(value))`. The interval and schedule source
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
produce replies with `request.With(FlowValue.From(reply))`. Manual bridge/trigger
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
`LinkCardinality`, and optional `InputShape` for inspection instead of registering
a type-dispatch callback. Canonical hosted business ports bind directly to
`ITargetBlock<FlowMessage>`/`ISourceBlock<FlowMessage>`; control inputs retain their
separate kind and forward the original canonical envelope through DI views.
Signal outputs and typed business runtime blocks remain unsupported by the host.

This is an intentional breaking prerelease change, not a compatibility shim.
It does not complete the remaining instance-first authoring/runtime migration.

## Choose the authoring path first

Use typed C# when the application is compiled with its workflow definition and
should use C# types, variables, predicates, factories, and dependency injection.
Use portable JSON when the definition must be stored, designed, edited, loaded,
or hot-reloaded as data.

The paths converge on the same Engine, but they do not serialize into each
other. A C# definition may own executable delegates and contract instances; a
JSON definition never does.

## Application registration

The normal code-first host no longer repeats every runtime component after the
definition is built.

Before:

```csharp
var definition = definitionBuilder.Build();

services.AddFluxFlowComponents()
    .AddRuntimeComponent("sample.uppercase", component =>
    {
        component
            .UseFactory(static _ => new UppercaseNode())
            .HasInput("Input", static node => node.Input)
            .HasOutput("Output", static node => node.Output)
            .HasEvents("Events", static node => node.Events);
    });

services.AddFluxFlowUpdates(definition);
```

After:

```csharp
var application = new ApplicationDefinitionBuilder();
application.AddWorkflow("main", out var workflow);

workflow.AddComponent(
    "upper",
    SampleComponents.Uppercase,
    out var upper);

var definition = application.Build();
services.AddFluxFlowUpdates(definition);
```

`SampleComponents.Uppercase` is a complete `ComponentContract`. It owns the
portable component type, runtime factory, typed bindings, and typed handle.
`ApplicationDefinition` retains the selected contract, so
`AddFluxFlowUpdates(definition)` is the only normal host registration.

## Component package declarations

Replace split descriptor/authoring declarations with one package-owned complete
contract. The runtime binding vocabulary describes existing node members:

- `HasInput`, not `AddInput`;
- `HasSignalInput`, not `AddSignalInput`;
- `HasOutput`, not `AddOutput`; and
- `HasEvents`, not `AddEvents` or an implicit global event port.

Events are explicit named output ports. A component handle should expose them as
`OutputPortHandle<FlowValue>` when the component supports events. `FlowEvent` is
only an optional C# construction and projection helper; runtime transport is the
same non-generic `FlowMessage` contract used by dynamic workflows.

`ComponentAuthoringContract` has been replaced by `ComponentContract`.
Consumers should select the package's exported complete contract rather than
constructing a second runtime registration.

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

Keep the handles captured during authoring. `ApplicationPorts` accepts typed
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
    .AddFluxFlowComponents()
    .AddSources();

services.AddFluxFlowUpdates(definition);
```

Keep explicit family registration here. It is the trusted catalog boundary for
portable type names and does not represent duplicate code-first registration.
JSON persistence and hot reload remain data-only and never embed CLR delegates,
component contracts, resource contracts, or service providers.

## Advanced dynamic registration

Raw runtime registration is an escape hatch for dynamic catalogs and low-level
integration packages. Move normal direct calls to the explicit advanced
surface:

```csharp
services
    .AddFluxFlowComponents()
    .Advanced
    .AddDynamicComponent("sample.dynamic", component =>
    {
        component
            .UseFactory(static _ => new DynamicNode())
            .HasInput("Input", static node => node.Input)
            .HasOutput("Output", static node => node.Output)
            .HasEvents("Events", static node => node.Events);
    });
```

Do not use this path merely to make a normal code-first definition executable.

## Fluent applications

`FluxFlow.Fluent` is now a concise facade over a canonical
`ApplicationDefinition` and `FluxFlowApplication`. Continue using the fluent
syntax, but host and observe the canonical definition/application exposed by the
graph. Replace `FlowGraph.Runtime` with `FlowGraph.Definition` or
`FlowGraph.Application` according to whether the caller needs the immutable
blueprint or the running Engine application. There is no separate Fluent
runtime lifecycle.

## Readiness

Application readiness is optional. Add the health package and call its standard
registration only when a host needs a readiness check. The adapter observes
existing application state; it adds no worker, polling, storage query, or
endpoint by itself.

## Native terminal-control inputs

`IFlowSignalTarget` is removed. Expose the existing native
`ITargetBlock<FlowMessage>` from a component and pass it directly to
`HasSignalInput`, `ComponentPorts.SignalInput`, or control-target registration.
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
2. Replace `ComponentAuthoringContract` with package-owned complete
   `ComponentContract` values.
3. Rename declaration mappings from `Add*` to `Has*` and declare Events
   explicitly.
4. Build applications with captured typed handles and typed connections.
5. Remove repeated code-first component and resource registration.
6. Keep explicit package registration for JSON and dynamic catalogs.
7. Move raw registrations to `Advanced.AddDynamicComponent`.
8. Use typed runtime and durability overloads where the authoring handle is
   available.
9. Update Fluent hosting to the canonical graph definition/application.
10. Run unchanged, rejected-reload, retained-route, shutdown, and durability
    recovery tests before promoting from the prerelease.
