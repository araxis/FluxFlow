# FluxFlow.Engine

Optional advanced executable runtime. The package contains the established
`ApplicationDefinition` runtime plus additive canonical stable ports, status,
system events, and diagnostics.

For new component packages and host composition, start with `FluxFlow.Nodes`,
`FluxFlow.Composition`, and `FluxFlow.Composition.Hosting`. Component packages
do not need this package to expose standalone nodes, composition adapters, or
Designer metadata.

## When To Use It

Use `FluxFlow.Engine` when a host already depends on:

- `ApplicationDefinition` workflow documents
- engine-specific validation and runtime build errors
- conditional links through engine definitions
- `ApplicationRuntimeBuilder` or `FlowApplicationHost`
- engine lifecycle state and diagnostic streams

Use `FluxFlow.Engine.Ports` when a host needs stable canonical port addresses,
compiled Composition links, revision-safe component attachment, or direct
send/receive/observe/request-reply interaction.

Use `FluxFlow.Engine.Signals` for canonical runtime status, system-event, and
diagnostic payloads. These contracts are transport-safe and travel in normal
`FlowMessage<T>` envelopes.

If a host only needs to compose standalone nodes from fluent C# or
`IConfiguration`, use `FluxFlow.Composition` instead.

Runtime and workflow startup check cancellation before each node and before
entering the running state. A canceled startup stops the affected state
machines without starting later nodes. Internal error, event, and diagnostic
fanout queues are bounded to 256 pending items; accepted items remain ordered,
while producers receive `false` immediately when a slow subscriber causes the
queue to overflow.

## Stable Ports

`ApplicationPortRuntimeBuilder` registers canonical input and output addresses.
Component targets and sources attach behind those addresses, so a host can
replace a component revision without replacing link or direct-API addresses.
Inputs are bounded mailboxes; outputs broadcast each message independently to
workflow links, one-shot receivers, and bounded observations.

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

- `FluxFlow.Engine`
- `FluxFlow.Engine.Components`
- `FluxFlow.Engine.Definitions`
- `FluxFlow.Engine.Ports`
- `FluxFlow.Engine.Runtime`
- `FluxFlow.Engine.Signals`

`FluxFlow.Mapping` owns expression and mapping contracts. The engine consumes
those contracts for link conditions but does not own concrete expression
languages.

## Component Boundary

Normal component packages should remain engine-free. Reusable node behavior
belongs in packages built on `FluxFlow.Nodes`; composition-facing registration
and design metadata belong in optional `.Composition` packages.

See `docs/15-engine-compatibility.md` for the compatibility policy and
`docs/12-component-composition.md` for the standalone-first component model.
