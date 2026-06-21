using FluxFlow.Composition;
using FluxFlow.Nodes;

var collector = new StringCollector();

var registry = new CompositionNodeRegistry()
    .Register(
        "sample.source",
        context =>
        {
            var options = context.BindConfiguration<SourceOptions>();
            var node = new StringSourceNode(options.Messages);
            return ValueTask.FromResult(ComposedNode.Create(
                node,
                outputs: [CompositionPorts.Output<string>("Output", node.Output)],
                events: node.Events,
                errors: node.Errors));
        },
        outputs: [CompositionPorts.Metadata<string>("Output")])
    .Register(
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
        outputs: [CompositionPorts.Metadata<string>("Output")])
    .Register(
        "sample.sink",
        _ =>
        {
            var node = new CollectSinkNode(collector);
            return ValueTask.FromResult(ComposedNode.Create(
                node,
                inputs: [CompositionPorts.Input<string>("Input", node.Input)],
                events: node.Events,
                errors: node.Errors));
        },
        inputs: [CompositionPorts.Metadata<string>("Input")]);

var definition = CompositionDefinitionBuilder
    .Create()
    .Workflow("main", workflow => workflow
        .Node("source", "sample.source", node => node.Configure("messages", new[] { "alpha", "beta" }))
        .Node("upper", "sample.uppercase")
        .Node("sink", "sample.sink")
        .Link("source.Output", "upper.Input")
        .Link("upper.Output", "sink.Input"))
    .Build();

var result = await new CompositionRuntimeBuilder(registry).BuildAsync(definition);
if (!result.Succeeded || result.Runtime is null)
{
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.Error.WriteLine(diagnostic.Message);
    }

    return 1;
}

await using var runtime = result.Runtime;
await runtime.StartAsync();
await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

foreach (var item in collector.Items)
{
    Console.WriteLine(item);
}

return 0;

internal sealed record SourceOptions
{
    public string[] Messages { get; init; } = [];
}

internal sealed class StringCollector
{
    private readonly List<string> _items = [];

    public IReadOnlyList<string> Items
    {
        get
        {
            lock (_items)
            {
                return _items.ToArray();
            }
        }
    }

    public void Add(string item)
    {
        lock (_items)
        {
            _items.Add(item);
        }
    }
}

internal sealed class StringSourceNode(IReadOnlyList<string> messages) : FlowSource<string>
{
    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Emit(FlowMessage.Create(message));
        }

        return Task.CompletedTask;
    }
}

internal sealed class UppercaseNode : FlowNode<string, string>
{
    protected override Task ProcessAsync(FlowMessage<string> message)
    {
        Emit(message.With(message.Payload.ToUpperInvariant()));
        return Task.CompletedTask;
    }
}

internal sealed class CollectSinkNode(StringCollector collector) : FlowNode<string, string>
{
    protected override Task ProcessAsync(FlowMessage<string> message)
    {
        collector.Add(message.Payload);
        Emit(message);
        return Task.CompletedTask;
    }
}
