# Node Authoring

A node is a small runtime object that exposes typed input and output ports.
Nodes can be written directly with `IFlowNode`, but most component authors should
start with the helper base classes.

## Helper Types

| Type | Use |
|------|-----|
| `FlowNodeBase` | Shared id, completion, errors, and diagnostics |
| `SourceFlowNode<TOutput>` | Source node with one output |
| `SinkFlowNode<TInput>` | Sink node with one input |
| `TransformFlowNode<TInput,TOutput>` | Transform with zero or more outputs per input |
| `MapFlowNode<TInput,TOutput>` | Transform with exactly one output per input |
| `EventFlowNodeBase` | Node that emits `FlowEvent` records |
| `RuntimeNodeBuilder` | Factory helper for declaring ports |

## Source Node

```csharp
public sealed class NumberSource : SourceFlowNode<int>
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        var node = new NumberSource();
        return context.CreateNode(node)
            .Output("Output", node.Output)
            .Build();
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        for (var value = 1; value <= 3; value++)
            await SendOutputAsync(value, cancellationToken);

        CompleteOutput();
    }
}
```

## Map Node

Prefer bounded Dataflow options for nodes that buffer or process data:

```csharp
public sealed class DoubleNode : MapFlowNode<int, int>
{
    public DoubleNode()
        : base(
            new ExecutionDataflowBlockOptions { BoundedCapacity = 16 },
            new DataflowBlockOptions { BoundedCapacity = 16 })
    {
    }

    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        var node = new DoubleNode();
        return context.CreateNode(node)
            .Input("Input", node.Input)
            .Output("Output", node.Output)
            .Build();
    }

    protected override ValueTask<int> MapAsync(
        int input,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(input * 2);
}
```

## Sink Node

```csharp
public sealed class CollectNode(List<int> values) : SinkFlowNode<int>
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context, List<int> values)
    {
        var node = new CollectNode(values);
        return context.CreateNode(node)
            .Input("Input", node.Input)
            .Build();
    }

    protected override ValueTask HandleAsync(
        int input,
        CancellationToken cancellationToken)
    {
        values.Add(input);
        return ValueTask.CompletedTask;
    }
}
```

## Errors, Events, And Diagnostics

Use `FlowError` for node failures that should be reported through the error
stream.

Use `FlowEvent` for workflow activity that the application may store, filter, or
show as history.

Use `FlowDiagnostic` for health, status, counters, and live monitoring data.

```csharp
TryEmitDiagnostic(
    "sample.order.reviewed",
    message: "Reviewed order.",
    attributes: new Dictionary<string, object?>
    {
        ["priority"] = true
    });
```

## Lifecycle Rules

- Complete output blocks when source work is done.
- Link node `Completion` to underlying Dataflow block completion.
- Propagate cancellation tokens through send and process operations.
- Keep input and output port names stable.
- Keep configuration parsing inside the node factory or package-owned helpers.

Next: [Package Authoring](04-package-authoring.md).
