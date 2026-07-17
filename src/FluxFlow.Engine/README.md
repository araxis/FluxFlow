# FluxFlow.Engine

Optional advanced executable runtime. The package contains the established
`ApplicationDefinition` runtime and the additive canonical stable-port runtime.

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

`Rejections` is a bounded best-effort stream for intake, condition, target,
source, and observation failures. It is not the full vNext system-event or
diagnostics contract.

## Public Surface

The package exposes these public namespaces:

- `FluxFlow.Engine`
- `FluxFlow.Engine.Components`
- `FluxFlow.Engine.Definitions`
- `FluxFlow.Engine.Ports`
- `FluxFlow.Engine.Runtime`

`FluxFlow.Mapping` owns expression and mapping contracts. The engine consumes
those contracts for link conditions but does not own concrete expression
languages.

## Component Boundary

Normal component packages should remain engine-free. Reusable node behavior
belongs in packages built on `FluxFlow.Nodes`; composition-facing registration
and design metadata belong in optional `.Composition` packages.

See `docs/15-engine-compatibility.md` for the compatibility policy and
`docs/12-component-composition.md` for the standalone-first component model.
