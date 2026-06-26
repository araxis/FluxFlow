# Node Authoring

A node is a small standalone runtime object over TPL Dataflow. The default
authoring path uses `FluxFlow.Nodes`: construct the node directly, link typed
ports with `LinkTo`, and pass `FlowMessage<T>` envelopes between nodes. No
engine, registry, or runtime is required.

## Core Types

| Type | Use |
|------|-----|
| `FlowMessage<T>` | Immutable message envelope with payload, correlation id, message id, timestamp, and headers. |
| `FlowNode<TInput,TOutput>` | Single-input, single-output processor with `Input`, `Output`, `Events`, `Errors`, and `Completion`. |
| `FlowSource<TOutput>` | Source node with `Output`, `Events`, `Errors`, `Completion`, and `StartAsync()`. |
| `IFlowNode` | Lifecycle contract for complete, fault, completion, and async disposal. |
| `IFlowSource` | Marker/lifecycle contract for nodes that must be started to produce data. |
| `FlowNodeOptions` | Bounded input capacity and processing degree options. |
| `FlowSourceOptions` | Source output capacity options. |

`FlowMessage<T>.With(...)` creates the next message while preserving correlation
id and headers:

```csharp
var input = FlowMessage.Create("hello");
var output = input.With(input.Payload.ToUpperInvariant());
```

## Transform Node

Use `FlowNode<TInput,TOutput>` for processors with one input and one primary
output:

```csharp
public sealed class UppercaseNode : FlowNode<string, string>
{
    public UppercaseNode()
        : base(new FlowNodeOptions { InputCapacity = 128 })
    {
    }

    protected override Task ProcessAsync(FlowMessage<string> message)
    {
        Emit(message.With(message.Payload.ToUpperInvariant()));
        return Task.CompletedTask;
    }
}
```

Direct usage stays simple:

```csharp
await using var upper = new UppercaseNode();
var output = new BufferBlock<FlowMessage<string>>();

upper.Output.LinkTo(
    output,
    new DataflowLinkOptions { PropagateCompletion = true });

await upper.Input.SendAsync(FlowMessage.Create("alpha"));
upper.Complete();
await upper.Completion;

var received = await output.ReceiveAsync();
```

Throwing from `ProcessAsync` is caught by the base class and surfaced on
`Errors` with the in-flight correlation id. The node keeps processing later
messages.

## Source Node

Use `FlowSource<TOutput>` when the node starts a stream:

```csharp
public sealed class NumberSource : FlowSource<int>
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        for (var value = 1; value <= 3; value++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(FlowMessage.Create(value), cancellationToken);
        }
    }
}
```

Sources start through `StartAsync()`:

```csharp
await using var source = new NumberSource();
var output = new BufferBlock<FlowMessage<int>>();

source.Output.LinkTo(
    output,
    new DataflowLinkOptions { PropagateCompletion = true });

await source.StartAsync(cancellationToken);
await source.Completion;
```

Composition runtime also starts `IFlowSource` nodes through
`CompositionRuntime.StartAsync()`.

## Extra Outputs

Nodes that fan out to additional typed ports can call `AddOutput<T>()`:

```csharp
public sealed class SplitNode : FlowNode<int, int>
{
    private readonly BroadcastBlock<FlowMessage<int>> _rejected;

    public SplitNode()
    {
        _rejected = AddOutput<FlowMessage<int>>();
    }

    public ISourceBlock<FlowMessage<int>> Rejected => _rejected;

    protected override Task ProcessAsync(FlowMessage<int> message)
    {
        if (message.Payload >= 0)
            Emit(message);
        else
            _rejected.Post(message);

        return Task.CompletedTask;
    }
}
```

Extra outputs are completed, faulted, and disposed with the node.

## Events And Errors

Use `FlowEvent` for workflow activity that a host may store, filter, or show as
history:

```csharp
EmitEvent(new FlowEvent
{
    Timestamp = DateTimeOffset.UtcNow,
    CorrelationId = message.CorrelationId,
    Name = "sample.order.reviewed",
    Level = FlowEventLevel.Information,
    Message = "Reviewed order.",
    Attributes = new Dictionary<string, object?>
    {
        ["priority"] = true
    }
});
```

Use `FlowError` for node failures that should be reported through the error
stream while the node can continue:

```csharp
EmitError(new FlowError
{
    Timestamp = DateTimeOffset.UtcNow,
    CorrelationId = message.CorrelationId,
    Code = 1001,
    Message = "Order review failed.",
    Exception = exception
});
```

Fatal startup or teardown failures should fault the node or source so
`Completion` exposes the failure.

## Optional Composition Factory

Composition support belongs in an optional adapter package or host registration
extension. Register node type strings with explicit factories:

```csharp
public static CompositionNodeRegistry RegisterSampleNodes(
    this CompositionNodeRegistry registry)
    => registry.Register(
        "sample.uppercase",
        _ =>
        {
            var node = new UppercaseNode();
            return ValueTask.FromResult(ComposedNode.Create(
                node,
                inputs: [CompositionPorts.Input<string>("Input", node.Input)],
                outputs: [CompositionPorts.Output<string>("Output", node.Output)],
                events: node.Events,
                errors: node.Errors));
        },
        inputs: [CompositionPorts.Metadata<string>("Input")],
        outputs: [CompositionPorts.Metadata<string>("Output")]);
```

Keep reflection scanning, assembly discovery, and host service orchestration out
of node packages. Hosts and adapter packages own concrete resources and keyed DI.

## Lifecycle Rules

- Keep input buffers bounded with `FlowNodeOptions`.
- Propagate cancellation tokens through source loops and external calls.
- Preserve correlation ids with `message.With(...)`.
- Complete entry nodes when the host wants the graph to drain.
- Await `Completion` in tests.
- Keep port names stable once exposed through composition or persisted config.
- Release node-owned resources from `OnDisposeAsync()`.
- Keep app workspace parsing outside reusable node packages.

Next: [Package Authoring](04-package-authoring.md).
