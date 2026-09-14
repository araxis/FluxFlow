# Node Definition

A node is a standalone object over Dataflow. Reference `FluxFlow.Core`; the
implementation namespace remains `FluxFlow.Nodes`. Construct components directly
with their actual dependencies. No creator, catalog or activation context is
required on this path.

## Core types

| Type | Use |
|------|-----|
| `FlowMessage` | Immutable envelope containing one `FlowValue` or `FlowError`, identity, timestamp and owned headers. |
| `FlowNode` | Bounded input, reliable output, best-effort events and drain-aware completion. |
| `FlowSource` | Reliable output, best-effort events and explicit source startup. |
| `IFlowComponent` | Actual port inventory plus lifecycle for custom components. |
| `IFlowNode` / `IFlowSource` | Lifecycle contracts, including explicit startup for sources. |
| `FlowNodeOptions` / `FlowSourceOptions` | Instance capacities and processing settings. |

Inter-component transport is non-generic. A component projects a value into its
local model only when it needs that model. Typed serialization/client helpers
are boundary conveniences, not another node runtime. Ordinary forwarding and
fanout retain the exact canonical envelope/value references.

## Transform node

```csharp
using FluxFlow.Data;
using FluxFlow.Nodes;

public sealed class UppercaseNode : FlowNode
{
    public UppercaseNode()
        : base(new FlowNodeOptions { InputCapacity = 128, OutputCapacity = 128 })
    {
    }

    protected override async Task ProcessAsync(FlowMessage message)
    {
        var text = message.Value!.Json.Deserialize<string>()!;
        await EmitAsync(message.With(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(text.ToUpperInvariant(), new JsonSerializerOptions(JsonSerializerDefaults.Web))))), Stopping);
    }
}
```

`With(...)` preserves trace, correlation and headers, creates a new message
identity and records the preceding message as causation. Incoming error messages
are forwarded unchanged unless `HandlesErrors` is explicitly overridden.
Per-message processing exceptions become error data; unrecoverable delivery and
lifecycle faults remain observable through `Completion`.

Direct block usage:

```csharp
await using var upper = new UppercaseNode();
var output = new BufferBlock<FlowMessage>();
using var link = upper.Output.LinkTo(
    output, new DataflowLinkOptions { PropagateCompletion = true });

await upper.Input.SendAsync(FlowMessage.Create(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("alpha", new JsonSerializerOptions(JsonSerializerDefaults.Web))))));
upper.Complete();
await upper.Completion;
var received = await output.ReceiveAsync();
```

Or place the same instance in a named application:

```csharp
await using var application = new FlowApplication("orders");
var workflow = application.AddWorkflow("main");
var upper = workflow.Add("upper", new UppercaseNode());
await application.StartAsync();
```

Successful addition transfers component ownership by default. Use explicit
borrowed ownership for instances whose DI scope owns disposal. Constructor
dependencies remain owned by their original owner unless their own contract
says otherwise.

## Source node

```csharp
public sealed class NumberSource : FlowSource
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        for (var value = 1; value <= 3; value++)
            await EmitAsync(FlowMessage.Create(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))), cancellationToken);
    }
}
```

Wire sources before calling `StartAsync`. A `FlowApplication` starts its
registered sources. Await emissions so bounded downstream pressure reaches the
producer.

## Additional ports

An extra reliable output can be created with `AddOutput<FlowMessage>()` and
written through `EmitAsync(output, message, Stopping)`. The base owns its
completion and disposal. Include each additional addressable output in the
component's overridden `Ports` inventory. Keep the same canonical envelope on
every message port. Custom multi-input components can implement
`IFlowComponent` around their actual native blocks without a universal base
class or dummy ports.

## Events and errors

`EmitEvent(FlowEvent)` projects an emitter-side event record into the canonical
event stream. `EmitEvent(FlowMessage)` accepts an already canonical event.
Events are best-effort observations; dashboards, storage and reporting policy
belong to consumers.

Domain failures remain ordinary output data:

```csharp
await EmitAsync(
    message.WithError(new FlowError("order.invalid", "An order is required.", "validation")),
    Stopping);
```

## Optional component registration

Direct application composition needs no registration. The current optional
configuration host uses direct factories to map trusted names/configuration
to constructor calls:

```csharp
services.AddComponent("sample.uppercase", context =>
{
    var node = new UppercaseNode();
    context.HasInput(node.Input);
    context.HasOutput(node.Output);
    context.HasEvents(node.Events);
    return node;
});
```

This belongs to the optional definition/host boundary, not the processing class.
UI tooling belongs to the independent consumer project. Direct Core composition
constructs nodes without this optional registration.

## Lifecycle rules

- Bound input and reliable output queues; await normal-data sends.
- Keep processing models component-local and immutable across message boundaries.
- Stop ingress before draining accepted work; await `Completion`.
- Propagate cancellation into external calls and source loops.
- Release component-owned resources in `OnDisposeAsync`.
- Treat graph names as addressing, not implicit processing dependencies.
- Keep workspace parsing, service lookup and reporting policy outside components.

Next: [Package Definition](04-package-authoring.md).
