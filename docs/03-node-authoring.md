# Node authoring

A component is a standalone class over native Dataflow blocks. Reference FluxFlow.Core;
the implementation namespace is `FluxFlow.Core.Nodes`. A `FlowNode` supports explicit
emissions, a `FlowTransformNode` supports zero/many results, and a `FlowSource` produces
messages after startup. Custom components can implement `IFlowNode`/`IFlowSource`
directly around their blocks. No application registration belongs in these classes.

## Message and payload

All addressable ports use FlowMessage. FlowMessage<T> preserves a typed payload where
T : FlowValue. FlowValue exposes a lazy JSON representation; typed forwarding need not
serialize it. Forward the same envelope when nothing changes. WithValue creates a new
message identity/causation while preserving trace, correlation and headers.

## Processing

```csharp
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

public sealed class UppercaseNode : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
    {
        var text = message.Value.Json.GetString()
            ?? throw new InvalidOperationException("Expected text.");
        await EmitAsync(message.WithValue((FlowValue)text.ToUpperInvariant()), Stopping);
    }
}
```

Incoming errors are forwarded unless HandlesErrors is overridden. Processing exceptions
become FlowError output data. Delivery/lifecycle faults remain visible through Completion.
Configure execution with native ExecutionDataflowBlockOptions and the output capacity.
Component-specific options and dependencies belong to that component.

Direct native block use remains available:

```csharp
await using var upper = new UppercaseNode();
var received = new BufferBlock<FlowMessage>();
using var link = upper.Output.LinkTo(received,
    new DataflowLinkOptions { PropagateCompletion = true });
await upper.Input.SendAsync(FlowMessage.Create((FlowValue)"alpha"));
upper.Complete();
await upper.Completion;
var message = await received.ReceiveAsync();
```

For application composition, register separately:

```csharp
services.AddComponentFactory("main.upper", _ => new UppercaseNode())
    .AddInputPort<UppercaseNode>("main.upper", "Input", node => node.Input)
    .AddOutputPort<UppercaseNode>("main.upper", "Output", node => node.Output)
    .AddEventPort<UppercaseNode>("main.upper", node => node.Events);
services.AddSingleton<LinkFactory>(ports =>
    ports.Link("main.upper.Output", "main.sink.Input"));
```

The sink must also be registered. Configuration registration does the same work while
reading sections. Build constructs the application once from native DI; Start activates
sources. There is no Workflow.Add/RegisterPorts API.

## Sources and additional blocks

```csharp
public sealed class NumberSource : FlowSource
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        for (var value = 1; value <= 3; value++)
            await EmitAsync(FlowMessage.Create((FlowValue)value), cancellationToken);
    }
}
```

Await sends so backpressure reaches the producer. Additional blocks can be exposed as
normal component properties. Register their addresses with AddInputPort/AddOutputPort
outside the component. TerminalInputs identifies controls that remain open through
business drain; their names do not determine lifecycle behavior.

Registered ports must not implement IDisposable or IAsyncDisposable. The component owns
their cleanup; native DI owns the component once. Disposable port aliases are rejected
before DI captures them. Injected dependencies retain their normal DI lifetime.

Events are best-effort observations. Reliable errors remain FlowError payloads on normal
outputs. Native processing diagnostics do not require an exporter in component code.

## Shutdown

Stop the application before disposing the host provider. Application drains accepted work
and events; DI disposes components/resources. Standard DI stops at the first disposal
exception. Components must join owned asynchronous work before releasing resources.
The base wrappers supply this lifecycle and processing-error behavior.

Next: [Package authoring](04-package-authoring.md).
