# FluxFlow.Engine

Host integration for FluxFlow applications. Ordinary hosting runs an existing
Core graph. Definition loading, transactional revision replacement, stable
addressable ports and operational services are an explicit update-hosting option.
`FluxFlow.Json` owns the portable application model. `FluxFlow.Composition`
owns executable authoring setup, component descriptors and link contracts;
Core owns addresses and canonical transport.

Update-host shutdown preserves component failures together with any revision-drain
failure. Inspect the flattened cleanup exception for these causes; asynchronous
failure observation does not replace the initiating component error with a later
input-detachment error. Owned components are still disposed after termination.

Addressable event bridges finish after component control processing. Shutdown
orders source production and source control completion separately using the
existing dependency ordering. Control writers drain before their target controls
close; event outputs drain before their linked consumers close. Unlinked event
bridges cannot hold the earlier business-production stage open.

Component packages remain Engine-independent. They expose standalone nodes and
optional Composition adapters; an application host chooses Engine when it needs
configuration-driven activation or direct port access.

## Direct graph hosting

Construct components normally, add their instances to a Core graph, and register
that graph. No definition source, component creator or revision snapshot is needed:

```csharp
using FluxFlow.Core;
using FluxFlow.Engine;

await using var application = new FlowApplication();
var workflow = application.AddWorkflow("main");
var receiver = workflow.Add("receiver", receiverInstance);
var publisher = workflow.Add("publisher", publisherInstance);
receiver.Output.LinkTo(publisher.Input);
publisher.Output.DiscardUnmatched();

builder.Services.AddFluxFlow(application);
using var host = builder.Build();
await host.RunAsync();
```

The host starts the actual Core graph, observes its completion/failure, and drains
it on shutdown. The caller retains disposal ownership (`await using` above);
disposing the service provider does not dispose an externally supplied graph.
Register one application per host. Direct and update hosting cannot be mixed in
the same service collection. Build asynchronous authoring adapters before registering
their resulting graph. Context-aware component creation remains deferred.

## Explicit update hosting

Use `AddFluxFlowUpdates(...)` when the application currently needs definition-based
activation, stable revision ports or operational services. These overloads were
previously named `AddFluxFlow(...)`; this is an intentional breaking change with
no compatibility alias. Update hosting uses Core's canonical components,
`FlowOutput` dispatcher, delivery batch and bounded exchange coordinator. It owns
the additional stable-port queues, revision staging, capture/selection policy and
transactional lifecycle; those are not part of an ordinary direct graph.

Register the application and component families explicitly in one service
collection. There is no assembly scanning or secondary runtime registration.

```csharp
using FluxFlow.Components.Mapping.Composition;
using FluxFlow.Engine;

services
    .AddFluxFlowUpdates(configuration)
    .AddMapping();

var application = provider.GetRequiredService<FluxFlowApplication>();
```

`AddFluxFlowUpdates(...)` supports an `IConfiguration` root or named section, a direct
`ApplicationDefinition`, an `ApplicationSetup`, a source instance, or a source type:

```csharp
services
    .AddFluxFlowUpdates(configuration, sectionName: "CustomFluxFlow")
    .AddMapping();
```

```csharp
services.AddFluxFlowUpdates<MyDefinitionSource>(options =>
{
    options.InitialRevisionId = "deployment-42";
    options.StartWithHost = true;
    options.StopWithHost = true;
    options.InputCapacity = 256;
    options.OutputCapacity = 512;
});
```

A C# `ApplicationSetup` built from complete `ComponentContract` values carries
its own executable descriptors alongside its portable `Definition`. Register it directly; do not repeat those
components in the service-registration chain:

```csharp
var definition = applicationBuilder.Build();

services.AddSingleton(orderStore);
services.AddFluxFlowUpdates(definition);
```

JSON and low-level string definitions intentionally carry no executable C#.
Register their required family extensions or individual complete contracts on
the host. Dynamic/plugin descriptors use the visibly advanced
`AddFluxFlowComponents().Advanced.AddDynamicComponent(...)` escape hatch.

The registered hosted service resolves the same singleton
`FluxFlowApplication` that direct callers resolve. Set `StartWithHost` to
`false` when the application will be started explicitly.

`InputCapacity` and `OutputCapacity` belong to the Engine's stable addressable
ports. Component DSL `BoundedCapacity` values and standalone node options remain
component-owned; registration does not overwrite them.

Hosted inputs now use one bounded serial `ActionBlock` for scheduling and capacity.
`InputCapacity` includes the active target admission as well as queued messages.
Rejected work stays in the active slot, ahead of later messages, while the stable
port waits for a replacement target. Revision pauses and generation checks remain
host-owned policy; the port no longer owns a separate queue, retry slot or wake-up
semaphore. Graceful completion reports each retained item if no target remains;
otherwise it drains admission and waits for target completion. Abort releases
pending revision waits and cancels queued work, not arbitrary noncooperative code.
Direct host `SendAsync` now awaits native bounded admission; `TrySend` provides
the explicit immediate alternative. Waiting producers do not consume another
input queue slot. Drain closes new admission and includes sends already started;
completion releases waiting producers while draining accepted work. Caller
cancellation cancels waiting admission, not a message already accepted by the
port.

Hosted business links now default to reliable awaited admission into that same
stable input. Every selected sibling is started before failures are observed;
failure cancels and joins pending admissions. This acknowledges input ownership,
not completion of component processing. Conditions are all selected before any
business delivery; a reliable condition error faults the output with the original
exception. There is no extra per-link queue.

Advanced compiled-link callers can use `link.WithDelivery(...)` with the shared
`FlowDeliveryPolicy`: `RejectWhenFull` faults the output on immediate rejection;
`IsolateRejection` reports rejection or condition failure through the host's existing
rejection stream and keeps the route alive for later messages. Isolation is an
explicit opt-in, not the default. Unlinking cancels admission that has not yet been
accepted; it does not recall a message already owned by an input. These policies
also flow through portable `Delivery` declarations and optional fluent `delivery`
arguments; the default remains reliable when the property is absent.

Revision gates freeze capture and route selection, then release before Core joins
target admission. An in-flight message retains its selected
routes, even if a subsequent revision removes them; stable inputs own its eventual
attachment generation. Later messages select the new snapshot. Output dispatch
remains serial and bounded, and drain waits for outstanding admission. Core's
`FlowOutput` performs the actual dispatch and fanout for both paths; the update
host supplies selected routes and post-admission notifications. Prepared revision
outputs also have bounded staging buffers before entering the stable output.
These revision boundaries are additional queues, not a second fanout algorithm.

Before a hosted candidate drains component inputs, it cuts off its own current
revision's system-event/diagnostic routes and waits for already selected system
deliveries. Later system messages remain available to external observers. Retiring
an older lease on a shared port surface does not detach its successor's routes.

Condition-only route replacement also uses the existing revision gates. A newly
compiled conditional link is treated as changed even when its expression text is
unchanged: bound parameters and host-native configuration are executable behavior.
Reusing the same compiled link keeps its identity; equivalent unconditional links
remain unchanged. This conservative preparation-time check does not attempt to
compare arbitrary predicates or add per-message work. A held capture completes
selection under its current condition before replacement becomes active.

Hosted system events, diagnostics and activity use Core's `FlowOutput` directly;
there is no separate signal fanout pump. System-event publication waits for bounded
capacity; diagnostics/activity use immediate best-effort admission into the same
output implementation. The signal capacity remains 256 queued items plus one active
delivery. A broken subscriber is detached without faulting healthy subscribers;
ordinary Core outputs retain strict reliable-delivery failure behavior.

Links see only messages accepted while registered, matching Core's live-output
contract. A late subscriber does not replay the waiting queue. Native predicates,
message limits and reservation callbacks use the actual exposed source. Graceful
completion drains accepted messages. Disposal detaches subscribers and abandons
queued messages without completing subscriber-owned blocks; a target must release
any native reservation it owns before disposal can finish. Signal observation
policy remains distinct from reliable business delivery and revision ownership.

Hosted output dispatch uses Core's bounded serial `FlowOutput` dispatcher.
`OutputCapacity`
now includes active dispatch, not an additional waiting queue of that size.
For example, capacity 2 permits one active capture and one waiting item.
Internal drain barriers share that bound without wrapping ordinary messages or
entering capture, routing, receive, observation or activity streams. Output
`PendingMessages` is a concurrent work-count snapshot: queued work plus active
dispatch, including transient drain barriers. It is not a business-message metric.

A drain barrier waits for previously accepted output work and honors revision
pauses. It does not wait for future sends from a still-running source. Stop or
drain the producer first when draining a whole revision. Capture failure faults
pending drains with the original exception; caller cancellation abandons only
that drain wait. Abort releases drain waits and prevents a late successful
capture from emitting more output.

## Application Lifecycle

`FluxFlowApplication` is the sole owner of lifecycle state and revision
mutation:

```csharp
var started = await application.StartAsync();
var reloaded = await application.ReloadAsync("deployment-43");
var applied = await application.ApplyAsync(
    "deployment-44",
    nextDefinition);
await application.StopAsync();
```

`StartAsync` and `ReloadAsync` load through `IApplicationDefinitionSource`;
`ApplyAsync` accepts an already loaded complete definition. All three return
`ApplicationUpdateResult` with `Applied`, `Unchanged`, or `Rejected` status,
the requested revision ID, active and previous snapshots, and staged
diagnostics. Expected source, validation, preparation, or activation failures
are rejected results. Cancellation remains `OperationCanceledException`.

Lifecycle mutations share one synchronization boundary. A candidate is fully
prepared before it can replace the active revision. Failed candidates are
disposed and cannot damage the previous revision. A successful replacement
switches stable ports atomically, then drains and disposes the old revision.
Revision-owned providers and candidates are disposed exactly once.

`State`, `Current`, `CurrentDefinition`, and `LastUpdate` expose the current
host view. A rejected reload keeps the application `Running` while the previous
revision continues serving work and records a rejected `LastUpdate`; a later
successful update replaces that result. A failed initial start with no active
revision is `Degraded`. The result returned by a successful replacement reports its
`PreviousRevision`; the retained `LastUpdate` copy omits that retired snapshot
so application state does not keep executable definitions and captured
predicates alive. A stopped application cannot be restarted.

## Stable Ports

`ApplicationPorts` is a stable facade over the active runtime generation. A
compiled-C# caller can retain component handles or select an address directly.
Physical message ports carry canonical `FlowMessage`; typed client results are
local projections, not separate runtime ports. For example, project the result
to the host's `OrderResult` model:

```csharp
var receive = application.Ports.ReceiveAsync<OrderResult>(
    finalResult.Output.Address,
    TimeSpan.FromSeconds(10));

var send = await application.Ports.SendAsync(
    validateOrder.Input.Address,
    FlowMessage.Create(order));

var reply = await application.Ports.SendAndReceiveAsync<Order, OrderResult>(
    validateOrder.Input.Address,
    finalResult.Output.Address,
    FlowMessage.Create(order),
    TimeSpan.FromSeconds(10));
```

`SendAsync` waits when the bounded input is full and reports accepted,
unavailable, or completed. Pass a cancellation token when the caller must stop
waiting. Acceptance means the local port owns the message, not that downstream
processing or a durable external effect has completed. Bound producer concurrency
as well: a bounded input does not limit how many callers can wait outside it.

This is an intentional behavior change. Callers that require the former
non-waiting behavior should use `application.Ports.TrySend(address, message)`
(or the string-address overload), which can also return `Full`. A component
handle supplies its `.Address`. The internal `ApplicationPortRuntime` implements
these operations; consumers use `ApplicationPorts`. Signal input `SendAsync` also waits for
capacity and preserves the original typed signal envelope.

`ReceiveAsync` is a broadcast tap and does not steal workflow
delivery. Receive waiters retain the original canonical envelope until the
client result is projected. `ObserveAsync<T>` uses one caller-bounded native
projection block. A slow observation that exceeds its capacity fails without
blocking healthy observations or workflow routes; a projection error fails
only that observation. Disposal and normal completion drain accepted items.
`SendAndReceiveAsync` registers before sending and uses the shared Core
exchange service, matching a fresh exchange correlation and expected reply
address, not `TraceId` alone. Its deadline includes admission and reply waiting.

The `ApplicationPorts` object remains stable across revisions and resolves the
current runtime generation for each operation. `Metadata`, `CurrentRevision`,
`Status`, `Rejections`, `SystemEvents`, `Diagnostics`, and `Completion` expose
the current generation. Access before the first successful activation is
rejected.

Canonical system outputs remain:

- `System.Events.Output` with canonical `FlowMessage` lifecycle activity.
- `System.Diagnostics.Output` with canonical `FlowMessage` diagnostic activity.

System-event delivery is bounded and reliable for accepted events. Diagnostic
delivery is bounded and best effort; overflow rejects immediately while
accepted diagnostics remain ordered. Component `FlowError` values remain
ordinary workflow data and are not replaced by operational diagnostics.

Normal application ports intentionally remain in-process. Hosts that require
crash recovery before Engine accepts an input can add the separate
`FluxFlow.Engine.DurableInput` package and a host-owned `IDurableInputStore`.
That adapter preserves `MessageId` and provides leased at-least-once delivery;
it does not change Engine revisions, port capacities, or normal send semantics.
Local hosts can add `FluxFlow.Engine.DurableInput.SqlFile` for a production
SQLite provider without adding a dependency from Engine itself. Shared hosts
can instead add `FluxFlow.Engine.DurableInput.TSql` for a production networked
relational provider with atomic multi-host leasing. Capable providers may also
expose `IDurableInputDeadLetterStore` for bounded inspection and explicit
compare-and-set replay without changing Engine configuration.

Hosts that need selected application outputs persisted before live Engine
dispatch can add `FluxFlow.Engine.DurableOutput`. Engine resolves one optional
typed capture operation per output port and otherwise keeps the current fast
path. The adapter uses explicit output addresses and `JsonTypeInfo<T>` metadata;
it adds no reflection discovery, provider setting, or transport dependency.
`ReceiveAsync` and `ObserveAsync` remain live taps rather than persistence
contracts. Hosts may independently enable the adapter's one-at-a-time leased
delivery dispatcher with one `IDurableOutputDeliveryStore` and one host-owned
`IDurableOutputDeliveryHandler`. This is fixed-retry at-least-once delivery;
handlers own destination idempotency. Local hosts can add
`FluxFlow.Engine.DurableOutput.SqlFile` for atomic idempotent SQLite capture and
independently initialized delivery state, or
`FluxFlow.Engine.DurableOutput.TSql` for shared networked capture, leases,
dead-letter operations, and replay. Other backends implement capture and,
optionally, the narrow delivery capability without changing Engine or workflow
definitions.

## Resources And Ownership

Composition adapters implement `IApplicationResourceRegistrar` from
`FluxFlow.Composition`. Registrars receive a revision-owned service collection
and register keyed resources in deterministic order. For compiled C#, the
resource contract that authored the definition carries that registrar into the
candidate revision; JSON/configuration definitions intentionally carry none and
still require explicit package registration. Engine merges exact host and
definition registrar identities idempotently, builds isolated resource and
workflow providers, resolves one effective catalog from host and
definition-owned descriptors, and owns only revision-scoped services it
creates. During activation, revision-owned services and keyed resources take
precedence; ordinary host services remain available as an explicit fallback and
keep host ownership.

Runtime generations, provider snapshots, revision candidates, binders, leases,
and port builders are implementation details. Normal consumers construct and
control only `FluxFlowApplication` through DI.

## Optional Readiness Health Check

Hosts that use standard .NET health checks can reference the separate
`FluxFlow.Engine.HealthChecks` package and register one application readiness
check:

```csharp
using FluxFlow.Engine.HealthChecks;

services.AddHealthChecks()
    .AddFluxFlowApplication();
```

The adapter observes existing in-memory application state only. It adds no
Engine dependency in the reverse direction, worker, polling, storage access,
or endpoint. See `docs/42-application-health-readiness.md` for the exact status
and bounded-data contract.

## Runtime Observation

Diagnostics retain their original canonical envelope through event delivery,
logging and metrics. There is no activity created merely because a diagnostic
was published. Hosts that used `ApplicationRuntimeInstrumentation.ActivitySourceName`
must remove that reference and enable the `FluxFlow.Core` activity source for
actual queued processing. The former name constant is intentionally removed in
this breaking candidate. Meter and diagnostic-source names remain unchanged.

Counter, duration, logger and diagnostic-source callbacks have independent
exception boundaries. Disabled log levels are not written. This does not make
host callbacks asynchronous or isolate listeners within the same provider;
hosts must keep callbacks short and configure export outside runtime processing.
See [processing context](../../../../docs/processing-trace-context.md) for the
span boundary and remaining instrumentation limits.

## Public Surface

The primary host-level contracts are:

- `FluxFlowApplication` and `FluxFlowApplicationOptions`.
- `ApplicationState`, `ApplicationSnapshot`, and `ApplicationUpdateResult`.
- `ApplicationPorts` plus result and metadata contracts in
  `FluxFlow.Engine.Ports`.
- the optional `IApplicationOutputCaptureResolver` and
  `IApplicationOutputCapture` extension seam used by durable-output adapters.
- `IApplicationDefinitionSource`,
  `ConfigurationApplicationDefinitionSource`, and
  `StaticApplicationDefinitionSource`.
- operational contracts in `FluxFlow.Engine.Signals`.
- application-lifetime canonical events exposed through `FluxFlowApplication.Events`,
  configured with `FluxFlow.Engine.Events.ApplicationEventOptions`.

Events use native Dataflow links and the existing `FlowEvent` / `FlowMessage`
representation. The library contains no report catalog, filter language, replay
journal or reporting snapshot. Live broadcast can lose observations at bounded
ingress or slow targets. Report definitions, filters, metrics, storage and
dashboard presentation remain consumer-owned. See
[the consumer guide](../../../../docs/45-reporting-consumer-guide.md) and
[the historical sample](../../../../samples/FluxFlow.ReportingSample/README.md).

Retired Engine and Composition document shapes are rejected. Convert stored
documents outside the runtime before loading them, then persist the canonical
`Resources` / `Workflows` shape and canonical component type names.

See `docs/05-hosting-and-observability.md` for lifecycle details and
`docs/15-engine-compatibility.md` for the current boundary policy.
