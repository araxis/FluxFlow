# FluxFlow.Engine

Optional canonical executable runtime for applications that need transactional
revision assembly, stable addressable ports, system events, and diagnostics.
The application model, component registrations, addresses, and links come from
`FluxFlow.Composition`; revision ownership comes from
`FluxFlow.Composition.Hosting`; `ApplicationRuntimeAssembler` supplies the
concrete executable candidate.

For new component packages and host composition, start with `FluxFlow.Nodes`,
`FluxFlow.Composition`, and `FluxFlow.Composition.Hosting`. Component packages
do not need this package to expose standalone nodes, composition adapters, or
Designer metadata.

## When To Use It

Use `FluxFlow.Engine` when a host needs:

- canonical `ApplicationDefinition` activation through explicit DI
- transactional component and link revisions
- stable typed and signal port addresses
- direct send, receive, observe, and request/reply operations
- application system events, best-effort diagnostics, and runtime status

Use `FluxFlow.Engine.Ports` when a host needs stable canonical port addresses,
compiled Composition links, revision-safe component attachment, or direct
send/receive/observe/request-reply interaction.

Use `FluxFlow.Engine.Signals` for canonical runtime status, system-event, and
diagnostic payloads. These contracts are transport-safe and travel in normal
`FlowMessage<T>` envelopes.

Use `FluxFlow.Engine.Hosting` with `FluxFlow.Composition.Hosting` when the
canonical `Resources`/`Workflows` document should be assembled into provider
snapshots, executable components, compiled routes, and stable direct ports.

If a host only needs a code-first graph, use `FluxFlow.Fluent`. Component
packages remain Engine-free and expose standalone nodes through
`FluxFlow.Nodes`.

System-event fanout is bounded and reliable for accepted events. Diagnostic
fanout is bounded to 256 pending items and rejects immediately on overflow;
accepted diagnostics remain ordered.

## Canonical Runtime Assembly

The assembler resolves component registrations through canonical aliases and
uses the normalized definition selected by the revision host. Every component
registration exposes a traced `Workflow.Component.Events` output in addition
to its package ports. Component events are ordinary stable output data and are
not duplicated into the application-level `System.Events.Output` stream.

The assembler also materializes nested `processing.profile` resources as
revision-owned keyed services. A flat component `Processing` reference is
mapped through `ICompositionProcessingProfileMapper`; unsupported concurrency
is rejected before the component factory runs. Defaults need no profile.

`UseRuntimeAssembler(...)` is the concrete candidate factory for canonical
application hosting. Registration is explicit: node contributors populate a
`CompositionNodeRegistry`, while service contributors map resource definitions
into a candidate-owned `IServiceCollection`. There is no assembly scanning,
reflection-based node activation, or fallback to arbitrary host services.

```csharp
services
    .AddFluxFlowApplication(configuration)
    .UseRuntimeAssembler(runtime => runtime
        .RegisterNodeContributor<ApplicationNodeContributor>()
        .RegisterServicesContributor<ApplicationResourceContributor>());

var host = provider.GetRequiredService<IApplicationRevisionHost>();
await host.StartApplicationAsync();

var ports = provider.GetRequiredService<IApplicationRuntimeAccess>()
    .GetRequiredPorts();
await ports.SendAsync(
    ApplicationAddress.WorkflowPort("Orders", "Validate", "Input"),
    FlowMessage.Create(order));
```

The assembler builds one resource-revision provider and one workflow-revision
provider per workflow, validates every factory descriptor against its explicit
registration, and activates all port attachments plus one compiled-link
snapshot transactionally before starting source components. Eager source
output therefore reaches already-active downstream workflow links. Rejected
preparation or activation disposes all
partial nodes, providers, and unadopted port generations; a successful
replacement drains and disposes the old candidate after the new one is current.

Revisions with the same addresses, directions, kinds, and payload types retain
the current `ApplicationPortRuntime`, so direct handles stay stable while
implementations and routing change. A revision that adds, removes, or retypes a
port prepares an isolated generation and publishes it atomically after
activation. `IApplicationRuntimeAccess.Ports` and `GetRequiredPorts()` then
return the new generation. A previously acquired runtime completes after its
candidate drains, so long-lived callers should reacquire the current runtime
after applying a surface-changing definition.

## Stable Ports

`ApplicationPortRuntimeBuilder` registers canonical input and output addresses.
Component targets and sources attach behind those addresses, so a host can
replace a component revision without replacing link or direct-API addresses.
Inputs are bounded mailboxes; outputs broadcast each message independently to
workflow links, one-shot receivers, and bounded observations.

Payload-independent inputs use `AddSignalInput(...)`. They retain the stable
address, bounded mailbox, direct-send result, compiled conditions, and revision
semantics of message inputs while accepting `FlowMessage<T>` for any `T`.

```csharp
var ackAddress = ApplicationAddress.WorkflowPort("Orders", "Receive", "Ack");

await using var ports = new ApplicationPortRuntimeBuilder()
    .AddSignalInput(ackAddress)
    .Build();

await using var attachment = await ports.AttachSignalInputAsync(
    ackAddress,
    acknowledgementTarget);

var accepted = await ports.GetSignalTarget(ackAddress)
    .SendAsync(FlowMessage.Create("payload is ignored"));
```

Signal targets are component-owned. Stable-port completion, detachment, and
revision replacement never call `Complete` or dispose the attached target.

```csharp
var inputAddress = ApplicationAddress.WorkflowPort("Orders", "Validate", "Input");
var outputAddress = ApplicationAddress.WorkflowPort("Orders", "Validate", "Output");

await using var ports = new ApplicationPortRuntimeBuilder()
    .AddInput<Order>(inputAddress)
    .AddOutput<ValidationResult>(outputAddress)
    .Build();

await using var inputAttachment = await ports.AttachInputAsync(
    inputAddress,
    validator.Input);
using var outputAttachment = ports.AttachOutput(
    outputAddress,
    validator.Output);

var request = FlowMessage.Create(order);
var result = await ports.SendAndReceiveAsync<Order, ValidationResult>(
    inputAddress,
    outputAddress,
    request,
    TimeSpan.FromSeconds(10));
```

`SendAsync` returns `Accepted`, `Full`, `Unavailable`, or `Completed` instead of
turning expected intake state into exceptions. `ReceiveAsync` is a broadcast
tap and never steals workflow delivery. `ObserveAsync` uses a caller-selected
bounded buffer; an overflowing observer is removed without blocking links or
other observers. `SendAndReceiveAsync` registers its response waiter before
sending and matches the response by `TraceId`.

`Rejections` remains a bounded low-level stream for intake, condition, target,
source, and observation failures. The runtime also maps those outcomes into the
canonical system-event and diagnostic surfaces described below.

## Transactional Port Revisions

`CreateRevision(...)` prepares a batch of stable input replacements, staged
output sources, and one complete compiled-link snapshot. Output sources feed
bounded staging buffers but remain invisible to routing until activation.

```csharp
await using var builder = ports.CreateRevision("orders-8")
    .ReplaceInput(inputAddress, replacement.Input)
    .AttachOutput(outputAddress, replacement.Output)
    .SetLinks(compiledLinks);
await using var revision = builder.Build();
await using var lease = await revision.ActivateAsync();
```

Activation serializes with other revisions, pauses only affected dispatchers,
waits for work already claimed by old input targets, activates staged outputs,
and swaps one immutable routing pointer. Queued stable-mailbox work then moves
to the replacement target. `CurrentRevision` identifies the committed sequence;
disposing an older lease cannot detach a newer input generation.

Revisions use stable addresses and exact payload types already registered with
`ApplicationPortRuntimeBuilder`. Adding an unregistered runtime port, changing
a payload type, or migrating queued payloads is rejected or remains a later
host-level capability; no implicit mapper is inserted.

## Runtime Status And Signals

Every `ApplicationPortRuntimeBuilder` registers these outputs automatically:

- `System.Events.Output` carries `FlowMessage<ApplicationSystemEvent>`.
- `System.Diagnostics.Output` carries `FlowMessage<ApplicationDiagnostic>`.

Pass `ApplicationPortRuntimeBuilder.SystemOutputs` to
`ApplicationLinkCompiler` so definitions can link either system output to an
exactly typed workflow input. Direct receive and observe APIs work with the same
addresses. Host subscribers can also link to
`ApplicationPortRuntime.SystemEvents` and `.Diagnostics` before publishing
activity.

`PublishSystemEventAsync` writes to a bounded, ordered, reliable fanout. When
its capacity is exhausted, publication waits; cancellation remains caller
cancellation. A returned `Accepted` result means the event entered the reliable
stream. A rejecting subscriber is detached and reported through diagnostics.
`ApplicationPortRuntime` also implements `IApplicationRevisionEventSink`,
mapping revision phases into `flow.revision.changed` events on the same reliable
stream.

`TryPublishDiagnostic` is intentionally best effort. Its bounded queue rejects
immediately with `false` when full or completed, while accepted diagnostics stay
ordered. Port input/output activity, direct request timing, link failures, and
component source/target faults use this surface. Faults detach only the affected
port attachment; `ApplicationRuntimeStatus` continues to describe the rest of
the runtime.

Accepted diagnostics integrate with `ILogger`, `ActivitySource`, `Meter`, and
`DiagnosticSource`. Supply an `ILogger` with `UseLogger(...)`; use the names on
`ApplicationRuntimeInstrumentation` to configure the other standard .NET
listeners. Host provider failures are isolated from runtime processing.

## Public Surface

The package exposes these public namespaces:

- `FluxFlow.Engine.Hosting`
- `FluxFlow.Engine.Migration`
- `FluxFlow.Engine.Ports`
- `FluxFlow.Engine.Signals`

Engine version 3 does not expose a second application definition, node base
class, factory registry, runtime builder, or lifecycle host. Use
`LegacyEngineApplicationDefinitionMigrator` only to convert compatible old
Workflows/Nodes JSON before canonical loading. Executable resource nodes and
non-default phases require explicit host migration.

`FluxFlow.Mapping` owns expression and mapping contracts. The engine consumes
those contracts for link conditions but does not own concrete expression
languages.

## Component Boundary

Normal component packages should remain engine-free. Reusable node behavior
belongs in packages built on `FluxFlow.Nodes`; composition-facing registration
and design metadata belong in optional `.Composition` packages.

See `docs/15-engine-compatibility.md` for the compatibility policy and
`docs/12-component-composition.md` for the standalone-first component model.
