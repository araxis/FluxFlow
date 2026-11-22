# Node authoring

A component is a standalone class over native Dataflow blocks. Reference FluxFlow.Core;
the implementation namespace is `FluxFlow.Core.Nodes`. `FlowNode` is the only optional
base, with emission helpers and lifetime forwarding. Each component constructs native
blocks and declares them to `FlowLifetime`. Processing and producer callbacks are
ordinary functions, with no abstract execution methods to override.
Custom components can implement `IFlowNode`/`IFlowSource` directly around their blocks.
No application registration belongs in these classes.

## Message and payload

All addressable ports use FlowMessage. FlowMessage<T> preserves a typed payload where
T : FlowValue. FlowValue exposes a lazy JSON representation; typed forwarding need not
serialize it. Forward the same envelope when nothing changes. WithValue creates a new
message identity/causation while preserving trace, correlation and headers.

## Processing

```csharp
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using System.Threading.Tasks.Dataflow;

public sealed class UppercaseNode : FlowNode
{
    public UppercaseNode()
    {
        Output = new BroadcastBlock<FlowMessage>(static message => message);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(
            message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping));
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    public ITargetBlock<FlowMessage> Input { get; }

    private async Task ProcessAsync(FlowMessage message)
    {
        var text = message.Value.Json.GetString()
            ?? throw new InvalidOperationException("Expected text.");
        await EmitAsync(message.WithValue((FlowValue)text.ToUpperInvariant()), Stopping);
    }
}
```

Incoming errors are forwarded unless ProcessMessageAsync receives handlesErrors: true. Processing exceptions
become FlowError output data. Delivery/lifecycle faults remain visible through Completion.
Configure execution with native ExecutionDataflowBlockOptions and the output capacity.
Component-specific options and dependencies belong to that component.

For synchronous work, pass a function returning IEnumerable<FlowMessage> directly,
or an Action<FlowMessage> for a callback with no returned results. There is no need to
wrap synchronous logic in Task.FromResult. ProcessMessageAsync retains its awaitable
result because error publication can wait for output admission. FlushMessagesAsync
also accepts a synchronous function returning a finite sequence, and ProcessControlAsync
accepts a synchronous action. These overloads share the existing behavior; none blocks
on asynchronous publication.

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
services.AddComponentFactory(ComponentAddress.Parse("main.upper"), _ => new UppercaseNode())
    .AddInputPort<UppercaseNode>(ComponentAddress.Parse("main.upper"), "Input", node => node.Input)
    .AddOutputPort<UppercaseNode>(ComponentAddress.Parse("main.upper"), "Output", node => node.Output)
    .AddEventPort<UppercaseNode>(ComponentAddress.Parse("main.upper"), node => node.Events);
services.AddSingleton<LinkFactory>(ports =>
    ports.Link(PortAddress.Parse("main.upper.Output"), PortAddress.Parse("main.sink.Input")));
```

The sink must also be registered. Configuration registration does the same work while
reading sections. Build constructs the application once from native DI; Start activates
sources. There is no Workflow.Add/RegisterPorts API.

## Sources and additional blocks

```csharp
public sealed class NumberSource : FlowNode, IFlowSource
{
    public NumberSource()
    {
        Output = new BroadcastBlock<FlowMessage>(static message => message);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, produce: ProduceAsync);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
        => Lifetime.StartAsync(cancellationToken);
    public void StopProduction() => Lifetime.StopProduction();
    public Task ProductionCompletion => Lifetime.ProductionCompletion;

    private async Task ProduceAsync(CancellationToken cancellationToken)
    {
        for (var value = 1; value <= 3; value++)
            await EmitAsync(FlowMessage.Create(new JsonFlowValue(
                System.Text.Json.JsonSerializer.SerializeToElement(value))), cancellationToken);
    }
}
```

Await sends to observe local output admission. Native broadcast does not wait for
consumer capacity; slow consumers may miss messages. Additional blocks can be exposed as
normal component properties. Construct those native blocks in the component, then
supply them to the same lifetime before calling Observe. The coordinator's
HasInput/HasOutput/HasEvents/HasTerminalInput methods accept existing blocks; FlowNode
has no block factories. The same coordinator works in components implementing
IFlowNode directly. See the [Core node guide](../Code/src/FluxFlow.Core/Nodes/README.md)
for processing, flush and producer contracts. Register addresses with AddInputPort/AddOutputPort
outside the component. TerminalInputs identifies controls that remain open through
business drain; their names do not determine lifecycle behavior.

Registered ports must not implement IDisposable or IAsyncDisposable. The component owns
their cleanup; native DI owns the component once. Disposable port aliases are rejected
before DI captures them. Injected dependencies retain their normal DI lifetime.

Events are best-effort observations. Errors remain FlowError payloads on normal broadcast
outputs. Native processing diagnostics do not require an exporter in component code.

## Shutdown

Stop the application before disposing the host provider. Application drains accepted work
and events; DI disposes components/resources. Standard DI stops at the first disposal
exception. Components must join owned asynchronous work before releasing resources.
FlowLifetime supplies this lifecycle; ProcessMessageAsync supplies processing-error
behavior. Declare all blocks, then call Observe at the end of construction. This keeps
already canceled or faulted blocks from ending the lifetime before declarations finish.
The optional FlowLifetime flush callback runs after ordinary inputs drain and is
skipped on fault. The first declared ordinary input initiates that drain on completion;
terminal inputs remain available until processing and output drain finish.
Functions returning IEnumerable<FlowMessage> use native TransformMany ordering;
direct EmitAsync calls are immediate and are not reordered by EnsureOrdered.

Next: [Package authoring](04-package-authoring.md).
