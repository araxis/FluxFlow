# Node Authoring

A node is a small standalone runtime object over TPL Dataflow. The default
authoring path uses `FluxFlow.Nodes`: construct the node directly, link typed
ports with `LinkTo`, and pass `FlowMessage<T>` envelopes between nodes. No
engine, registry, or runtime is required.

## Core Types

| Type | Use |
|------|-----|
| `FlowMessage<T>` | Immutable value-or-error envelope with business correlation, graph trace, hop/causation identity, timestamp, and string headers. |
| `FlowNode<TInput,TOutput>` | Single-input, single-output processor with bounded `Input`, reliable bounded `Output`, best-effort `Events`, and `Completion`. |
| `FlowSource<TOutput>` | Source node with reliable bounded `Output`, best-effort `Events`, `Completion`, and `StartAsync()`. |
| `IFlowNode` | Lifecycle contract for complete, fault, completion, and async disposal. |
| `IFlowSource` | Marker/lifecycle contract for nodes that must be started to produce data. |
| `FlowNodeOptions` | Bounded input/output capacity and processing degree options. |
| `FlowSourceOptions` | Source output capacity options. |

`FlowMessage<T>.With(...)` creates the next message while preserving correlation
id and headers:

```csharp
var input = FlowMessage.Create("hello");
var output = input.With(input.Value.ToUpperInvariant());
```

## Transform Node

Use `FlowNode<TInput,TOutput>` for processors with one input and one primary
output:

```csharp
public sealed class UppercaseNode : FlowNode<string, string>
{
    public UppercaseNode()
        : base(new FlowNodeOptions
        {
            InputCapacity = 128,
            OutputCapacity = 128
        })
    {
    }

    protected override async Task ProcessAsync(FlowMessage<string> message)
    {
        await EmitAsync(
            message.With(message.Value.ToUpperInvariant()),
            Stopping);
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

Throwing from `ProcessAsync` is caught by the base class and emitted as ordinary
`FlowError` output data with the in-flight identity. Infrastructure delivery
failures instead fault `Completion`.

## Source Node

Use `FlowSource<TOutput>` when the node starts a stream:

```csharp
public sealed class NumberSource : FlowSource<int>
{
    public NumberSource()
        : base(new FlowSourceOptions { OutputCapacity = 128 })
    {
    }

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

An `ApplicationRuntime` also starts registered `IFlowSource` components through
`ApplicationRuntime.StartAsync()`.

## Extra Outputs

Nodes that fan out to additional typed ports can call `AddOutput<T>()`:

```csharp
public sealed class SplitNode : FlowNode<int, int>
{
    private readonly FlowOutput<FlowMessage<int>> _rejected;

    public SplitNode()
    {
        _rejected = AddOutput<FlowMessage<int>>();
    }

    public ISourceBlock<FlowMessage<int>> Rejected => _rejected;

    protected override async Task ProcessAsync(FlowMessage<int> message)
    {
        if (message.Value >= 0)
            await EmitAsync(message, Stopping);
        else
            await EmitAsync(_rejected, message, Stopping);
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

Use `FlowError` for domain failures that should remain normal workflow data while
the node can continue:

```csharp
await EmitAsync(
    message.WithError<string>(new FlowError(
        "order.review.failed",
        exception.Message,
        "order")),
    Stopping);
```

Fatal startup or teardown failures should fault the node or source so
`Completion` exposes the failure.

## Optional Component Registration

Composition support belongs in an optional adapter package or host registration
extension. For normal compiled-C# authoring, expose one complete contract:

```csharp
public static ComponentContract<UppercaseHandle> Uppercase { get; } =
    ComponentContract.Create(
        "sample.uppercase",
        component =>
        {
            component
                .UseFactory(static _ => new UppercaseNode())
                .HasInput("Input", static node => node.Input)
                .HasOutput("Output", static node => node.Output)
                .HasEvents("Events", static node => node.Events);
        },
        static component => new UppercaseHandle(component));
```

Each typed call is authoritative: it creates descriptor metadata during
registration and selects the concrete Dataflow block after node activation.
The `Has...` prefix is intentional: the selected node member already exists;
the call describes the component contract and maps its external port name to
that existing member rather than creating another Dataflow port.
Factories and selectors are not executed during registration. `HasEvents`
selects an `ISourceBlock<FlowEvent>` and exposes the bridged public
`FlowMessage<ComponentEvent>` output under the chosen name. There is no
implicit or globally reserved `Events` port.

Signal targets use the same single-declaration model and retain signal
semantics without pretending to carry a message payload:

```csharp
component
    .UseFactory(CreateTriggerNode)
    .HasSignalInput("Ack", static node => node.Ack)
    .HasSignalInput("Nak", static node => node.Nak)
    .HasOutput("Output", static node => node.Output)
    .HasEvents("Events", static node => node.Events);
```

Keep reflection scanning, assembly discovery, and host service orchestration out
of node packages. When the optional adapter exposes metadata, keep its constants
and presentation authoring in one package-owned `*ComponentDefinition` and
register it with the designed `AddComponent(...)` path from the same family
extension. Hosts and adapter packages own concrete resources and keyed DI.

JSON or low-level string definitions register the complete contract explicitly
with `AddComponent(Uppercase)`. Reserve
`AddFluxFlowComponents().Advanced.AddDynamicComponent(...)` for a dynamic
runtime-only descriptor that has no reusable typed contract.

## Lifecycle Rules

- Keep input and normal-data output buffers bounded with `FlowNodeOptions`.
- Treat `FlowNodeOptions` and `FlowSourceOptions` as node-instance settings;
  graph builders link configured node instances and do not override them.
- Await every normal-data `EmitAsync`/`SendAsync`; do not fire-and-forget output delivery.
- Keep events and diagnostics best-effort so observers cannot stall workflow data.
- Propagate cancellation tokens through source loops and external calls.
- Preserve correlation ids with `message.With(...)`.
- Complete entry nodes when the host wants the graph to drain.
- Await `Completion` in tests.
- Keep port names stable once exposed through composition or persisted config.
- Release node-owned resources from `OnDisposeAsync()`.
- Keep app workspace parsing outside reusable node packages.

Next: [Package Authoring](04-package-authoring.md).
