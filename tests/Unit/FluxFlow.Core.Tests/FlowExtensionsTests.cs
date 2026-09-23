using FluxFlow.Core.Execution;
using FluxFlow.Core.Application;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class FlowExtensionsTests
{
    [Fact]
    public async Task Direct_extensions_route_real_messages_with_captured_predicates_and_cross_workflow_ports()
    {
        var services = new ServiceCollection();
        var archiveNode = new Sink();
        var selectedNode = new Sink();
        var source = new Echo();
        services.AddComponentFactory("archive.sink", _ => archiveNode)
            .AddInputPort<Sink>("archive.sink", "Input", node => node.Input)
            .AddComponentFactory("main.selected", _ => selectedNode)
            .AddInputPort<Sink>("main.selected", "Input", node => node.Input)
            .AddComponentFactory("main.source", _ => source)
            .AddInputPort<Echo>("main.source", "Input", node => node.Input)
            .AddOutputPort<Echo>("main.source", "Output", node => node.Output);
        var threshold = 5;
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.source.Output", "archive.sink.Input").Output.ShouldBeSameAs(source.Output);
            ports.Link("main.source.Output", "main.selected.Input", message => message.Value.Json.GetInt32() > threshold);
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        await source.Input.SendAsync(Message(3));
        await source.Input.SendAsync(Message(7));
        await appExecution.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        archiveNode.Values.ShouldBe([3, 7]);
        selectedNode.Values.ShouldBe([7]);
    }

    [Fact]
    public async Task Native_link_delegates_keep_fan_in_and_reject_late_routes()
    {
        var first = new Echo();
        var second = new Echo();
        var sinkNode = new Sink();
        var services = new ServiceCollection();
        foreach (var (name, node) in new[] { ("first", first), ("second", second) })
            services.AddComponentFactory("main." + name, _ => node)
                .AddInputPort<Echo>("main." + name, "Input", value => value.Input)
                .AddOutputPort<Echo>("main." + name, "Output", value => value.Output);
        services.AddComponentFactory("main.sink", _ => sinkNode).AddInputPort<Sink>("main.sink", "Input", node => node.Input);
        TestSink.Register(services, "main.second.Output");
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.first.Output", "main.sink.Input");
            ports.Link("main.second.Output", "main.sink.Input", message => message.Value.Json.GetInt32() > 0);
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        await first.Input.SendAsync(Message(1));
        await second.Input.SendAsync(Message(-1));
        await second.Input.SendAsync(Message(2));
        await appExecution.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        sinkNode.Values.Order().ShouldBe([1, 2]);
        Should.Throw<InvalidOperationException>(() => provider.Link("main.first.Output", "main.sink.Input"));
    }

    private static FlowMessage Message(int value) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));

    private sealed class Echo : FlowTransformNode
    {
        protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message) => Task.FromResult<IEnumerable<FlowMessage>>([message]);
    }

    private sealed class Sink : FlowNode
    {
        public List<int> Values { get; } = [];
        protected override Task ProcessAsync(FlowMessage message)
        {
            Values.Add(message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return Task.CompletedTask;
        }
    }
}
