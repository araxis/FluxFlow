# FluxFlow.Application

Optional application composition over FluxFlow.Core components. This package owns
workflows, typed application addresses, resource and component registration, deferred
links, application startup/shutdown and the standard hosting adapter.

Use `FluxFlow.Application` for registration and application APIs,
`FluxFlow.Application.Addressing` for addresses, and
`FluxFlow.Application.Execution` for link contracts.

Register factories and ports in one service collection, build one provider, then call
`BuildApplicationAsync`. Registration uses `AddComponentFactory`, `AddInputPort`,
`AddOutputPort` and `AddEventPort`. DI owns component disposal. Application coordinates
execution and completion; it does not provide another service registry or provider.

Application helpers are grouped in `FlowApplicationExtensions`: AddFlowWorkflow,
BuildApplicationAsync, Link, GetComponent and address discovery. ComponentRegistrationExtensions contains
only component/port registration; AddApplicationResources lives alongside the other
resource registration methods in ResourceRegistrationExtensions. Extension-call syntax
and namespace imports remain unchanged. Explicit static calls must use these classes;
rebuild dependent packages after this development-preview API reorganization.

Hosts can discover registered addresses directly from their existing service provider:

```csharp
IEnumerable<ComponentAddress> components = services.GetComponentAddresses();
IEnumerable<PortAddress> inputs = services.GetInputPortAddresses();
IEnumerable<PortAddress> outputs = services.GetOutputPortAddresses();

var workflowComponents = components.Where(address => address.WorkflowName == "main");
var componentOutputs = outputs.Where(address => address.ComponentAddress == componentAddress);
```

These methods enumerate existing DI address registrations, without constructing components,
resources or ports. They work before application composition and while running. Named event
outputs and unlinked ports are included; unrelated host block services are excluded. Results
describe registrations, not readiness, running state or active links. There is no additional
catalog or cache. Empty providers and workflows return empty sequences.

When an instance is needed, use ordinary keyed DI with the address's `Value`, for example
`services.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>(outputAddress.Value)`.
Unlike address discovery, resolving a block can construct its component if it has not
already been constructed. DI returns the same instance within that workflow run; discovery does not
attach observers or alter routing.

Live output observation uses the same declared output addresses:

```csharp
using var probe = services.ObserveOutput(outputAddress, capacity: 128);
await foreach (var message in probe.Messages.ReceiveAllAsync(cancellationToken))
{
    // Serialize or display on the reader side, away from business delivery.
    await DisplayAsync(message, cancellationToken);
}
```

Attach and dispose probes while the application is running. Each probe uses a native
filtered link, placed before ordinary consumers, that queues the original immutable
message reference and always declines the original offer. It never consumes or reserves
the business message. There are no user callbacks or serialization on the delivery path.
Named event outputs use their existing address-stamped messages. Resolving an output
before composition can construct its component, just like ordinary keyed DI resolution.

The buffer is bounded by message count (default 128). A full buffer drops new probe
samples and increments `DroppedCount`; it never waits for the reader. Other probes and
business consumers continue independently. Reader cancellation or failure does not stop
the application; dispose the probe when the reader ends. Source completion/fault propagates
to `Messages`; completion waits for retained probe samples to drain, but application
shutdown does not wait for the probe. Disposal detaches, closes the buffer and discards
unread available samples, without completing or faulting the observed output.

This is an output-offer inspection stream, not an audit trail or proof of delivery.
A broadcast output can replay its retained value when a probe attaches, and a buffered
source can offer a pending value more than once. Observation adds a bounded queue operation
per offer; it does not change business routing, completion ownership or backpressure.
Custom payloads must honor the existing FlowValue immutability contract. No eager JSON
snapshot, history store, remote transport or runtime graph mutation is introduced.

Input and link observation remain pending; input address discovery does not provide
an input probe. Viewer pause/resume can dispose and reattach an output probe without
pausing business processing; messages missed while detached are not guaranteed to replay.

`probe.Address` identifies its typed output port; `probe.Address.ComponentAddress.Value`
matches the `flow.component.address` tag on processing spans and runtime metrics.
`AddComponentFactory` attaches that diagnostic string once when DI creates the node,
before processing starts. It uses the existing weak instance-identity guard, which also
rejects aliasing the same component under another registration. No additional registry
or node interface is needed, and native DI still owns construction and disposal.

Event observations retain their existing `FlowEventHeaders.Source` component address.
Processing and lifecycle exception diagnostics also expose `ComponentAddress`, including
when tracing is disabled. Ordinary message headers and payloads are not changed by this
identity association. See [hosting and observability](../../../docs/05-hosting-and-observability.md).

Configuration-based setup is provided separately by FluxFlow.Configuration. Core and
component packages can also be used directly with native Dataflow `LinkTo`, without
this package or application addressing.

The standard Generic Host adapter also works with a host's `AddWindowsService()` setup;
Windows-specific dependencies belong to the host executable. Live probes use the same
APIs in either host. See [host verification](../../../docs/05-hosting-and-observability.md)
and `eng/verify-hosting.ps1`. CI runs the non-elevated console modes on .NET 8 and 10;
actual SCM verification remains pending as a separate elevated check.

This extraction changes package and namespace locations, not delivery, addressing,
fan-in completion, fault handling or lifecycle behavior. There are no compatibility
aliases in Core.

## Registration and application lifetime

Register factories and ports before constructing the single host provider:

```csharp
var address = ComponentAddress.Create("main", "node");
services.AddComponentFactory(address, provider => new MyNode())
    .AddInputPort<MyNode>(address, "Input", node => node.Input)
    .AddOutputPort<MyNode>(address, "Output", node => node.Output)
    .AddEventPort<MyNode>(address, node => node.Events);

services.AddSingleton<LinkFactory>(provider =>
    provider.Link(address.Port("Output"), PortAddress.Parse("main.archive.Input")));

await using var provider = services.BuildServiceProvider();
await using var application = await provider.BuildApplicationAsync();
await application.StartAsync();
```

The example assumes main.archive.Input was registered separately. Addresses
validate syntax at construction. Application registration takes ComponentAddress and
links take PortAddress; canonical Value strings are native DI keys. Existence
and direction are checked when resolving the registered blocks.

Factories are synchronous, lazy keyed scoped services. Build resolves components,
ports and link declarations once. Start attaches native links before starting
sources. Repeated Build returns the same application. Application.Workflows
enumerates DI-owned workflows. Messages drive processing while the workflow is running.
Empty workflows are supported through AddFlowWorkflow.

`ComponentFactory` is the delegate accepted by `AddComponentFactory`; it is stored
inside the native keyed `IFlowNode` registration. It is not separately resolvable
as a keyed service. Resolve the component through keyed `IFlowNode` when needed;
DI invokes the factory once per workflow run and owns the resulting instance.

Application coordinates fan-in completion, source shutdown and terminal controls.
A shared consumer completes after all its business producers complete. Accepted
input work and component flushes finish before outputs complete; terminal reply
inputs remain available until business work ends. Runtime synchronization remains.

Stop/dispose the application before the host provider. Application drains execution;
Each stopped workflow disposes its native DI scope; the root provider retains shared
application resources until host disposal. DI alone disposes instances. Standard DI stops disposal at its first
disposal failure. A weak identity guard rejects the same node registered under
another key/provider before DI captures ownership. Port selectors must return
separate non-disposable native blocks, avoiding duplicate component disposal.
Failed composition cleans up only successfully constructed components.

Faulting a built application before Start also faults its constructed components,
rejects further admission and preserves the first failure through Stop. Faulting
before Build does not construct components. Link attachment failures release every
previously attached link once, collecting unlink failures without hiding the startup
error. Native DI remains the sole owner of component disposal.

AddEventPort defaults to the named Events output. Its event bridge adds address
headers and exposes one native output; there is no hidden second activity stream.
It unlinks once after component completion and waits for admitted events to drain.
Every bridge disposal caller observes the same completion. Independent forwarding
and unlink failures are reported together; component faults remain observable on
component completion without faulting the event stream.

## Workflow stop and restart

After application startup, an independently linked workflow can be stopped and restarted:

```csharp
var workflow = provider.GetRequiredKeyedService<FlowWorkflow>("main");
await workflow.StopAsync(cancellationToken);
await workflow.StartAsync(cancellationToken);
var input = workflow.Services.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Input");
```

Stop closes production, drains admitted work and flushes, completes reply/control inputs
after business work, detaches links and disposes the run's native DI scope. Start after
a successful Stop creates a new scope, constructs fresh components and reconnects the
existing link declarations before starting sources. Factories must return fresh nodes.
Configuration registration and link delegates are not replayed; no provider is rebuilt.
Calling Start on an already running workflow or Stop again is idempotent. Starting during
an unfinished Stop is rejected. Failed cleanup prevents restart.
Application-wide Stop waits for every workflow and reports failures from all their
shutdown tasks. Disposal within each scope still follows standard DI behavior.
Canceling the Stop wait faults the execution; cleanup continues and the existing
application-wide fault policy applies. Cancellation cannot forcibly terminate custom
component code that ignores its stop signal.

Reacquire `workflow.Services`, components, ports and Completion after restart. Resolving
components from the root provider, an HTTP request scope or another workflow scope is
rejected. `GetWorkflowServices(name)` is the convenience extension for this lookup.
Output/event probes belong to the old run and must be attached again. ASP.NET Core routes
resolve the current run on each request; stopped workflows return 503.

Application resources remain root singletons. Workflow resource factories are scoped to
their owning workflow run and cannot be resolved from another workflow. External resource
instances remain caller-owned and are reused. Disposal ownership does not imply operational
isolation: stopping an MQTT connection component disconnects its shared client. Put that
connection controller in a long-lived workflow or use a workflow-local broker resource when
consumers need independent lifetimes.

Individual Start/Stop rejects workflows with incoming or outgoing cross-workflow links
before changing any components. Application-wide Stop still coordinates those links.
There is no automatic cross-workflow buffering, rebinding or completion propagation on
individual restart. Existing fatal faults retain application-wide fail-fast behavior.

Application Stop remains terminal. The Generic Host stays available across successful
workflow completion and restart. Application.Completion describes the current runs;
reacquire it after restart. Hot reload, configuration replacement and state migration
remain deferred and outside Core.

