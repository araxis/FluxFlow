using System.Text.Json;
using FluxFlow.Composition;
using FluxFlow.Composition.Model;
using FluxFlow.Engine;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ApplicationWorkflowDefinition = FluxFlow.Composition.Model.WorkflowDefinition;

var collector = new StringCollector();
var services = new ServiceCollection();
services.AddFluxFlowComponents()
    .AddRuntimeComponent("sample.source", component =>
    {
        component.UseFactory(context =>
        {
            var options = context.BindConfiguration<SourceOptions>();
            var node = new StringSourceNode(options.Messages);
            return ValueTask.FromResult(ComponentInstance.Create(
                node,
                outputs: [ComponentPorts.Output<string>("Output", node.Output)],
                events: node.Events));
        });
        component.AddOutput<string>("Output");
    })
    .AddRuntimeComponent("sample.uppercase", component =>
    {
        component.UseFactory(_ =>
        {
            var node = new UppercaseNode();
            return ValueTask.FromResult(ComponentInstance.Create(
                node,
                inputs: [ComponentPorts.Input<string>("Input", node.Input)],
                outputs: [ComponentPorts.Output<string>("Output", node.Output)],
                events: node.Events));
        });
        component.AddInput<string>("Input");
        component.AddOutput<string>("Output");
    })
    .AddRuntimeComponent("sample.sink", component =>
    {
        component.UseFactory(_ =>
        {
            var node = new CollectSinkNode(collector);
            return ValueTask.FromResult(ComponentInstance.Create(
                node,
                inputs: [ComponentPorts.Input<string>("Input", node.Input)],
                events: node.Events));
        });
        component.AddInput<string>("Input");
    });

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

services.AddFluxFlow(definition, options => options.StartWithHost = false);

await using var provider = services.BuildServiceProvider();
var application = provider.GetRequiredService<FluxFlowApplication>();
var result = await application.StartAsync();
if (result.IsRejected)
{
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.Error.WriteLine(diagnostic.Error.Message);
    }

    return 1;
}

await WaitForItemsAsync(collector, expectedCount: 2, TimeSpan.FromSeconds(5));
await application.StopAsync();

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
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(FlowMessage.Create(message), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

internal sealed class UppercaseNode : FlowNode<string, string>
{
    protected override async Task ProcessAsync(FlowMessage<string> message)
    {
        await EmitAsync(message.With(message.Value.ToUpperInvariant()), Stopping)
            .ConfigureAwait(false);
    }
}

internal sealed class CollectSinkNode(StringCollector collector) : FlowNode<string, string>
{
    protected override async Task ProcessAsync(FlowMessage<string> message)
    {
        collector.Add(message.Value);
        await EmitAsync(message, Stopping).ConfigureAwait(false);
    }
}
