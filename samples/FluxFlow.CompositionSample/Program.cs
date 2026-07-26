using System.Text.Json;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Composition.Model;
using FluxFlow.Engine.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ApplicationWorkflowDefinition = FluxFlow.Composition.Model.WorkflowDefinition;

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
                events: node.Events));
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
                events: node.Events));
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
                events: node.Events));
        },
        inputs: [CompositionPorts.Metadata<string>("Input")]);

var definition = new ApplicationDefinition(
    workflows:
    [
        new("main", new ApplicationWorkflowDefinition(
        [
            new("source", Component(
                "sample.source",
                ("messages", new[] { "alpha", "beta" }),
                ("Output", "upper.Input"))),
            new("upper", Component(
                "sample.uppercase",
                ("Output", "sink.Input"))),
            new("sink", Component("sample.sink"))
        ]))
    ]);

var services = new ServiceCollection();
services
    .AddFluxFlowApplication(definition)
    .UseRuntimeAssembler(runtime => runtime.RegisterNodes(nodes =>
    {
        foreach (var registration in registry.Registrations.Values)
            nodes.Register(registration);
    }));

await using var provider = services.BuildServiceProvider();
var host = provider.GetRequiredService<IApplicationRevisionHost>();
var result = await host.StartApplicationAsync();
if (!result.Succeeded)
{
    foreach (var failure in result.Update!.Failures)
    {
        Console.Error.WriteLine(failure.Error.Message);
    }

    return 1;
}

await WaitForItemsAsync(collector, expectedCount: 2, TimeSpan.FromSeconds(5));
await host.StopApplicationAsync();

foreach (var item in collector.Items)
{
    Console.WriteLine(item);
}

return 0;

static ComponentDefinition Component(
    string type,
    params (string Name, object? Value)[] properties)
    => new(
        type,
        properties.Select(property => KeyValuePair.Create(
            property.Name,
            JsonSerializer.SerializeToElement(property.Value))));

static async Task WaitForItemsAsync(
    StringCollector collector,
    int expectedCount,
    TimeSpan timeout)
{
    using var cancellation = new CancellationTokenSource(timeout);
    while (collector.Items.Count < expectedCount)
        await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token);
}

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
        Emit(message.With(message.Value.ToUpperInvariant()));
        return Task.CompletedTask;
    }
}

internal sealed class CollectSinkNode(StringCollector collector) : FlowNode<string, string>
{
    protected override Task ProcessAsync(FlowMessage<string> message)
    {
        collector.Add(message.Value);
        Emit(message);
        return Task.CompletedTask;
    }
}
