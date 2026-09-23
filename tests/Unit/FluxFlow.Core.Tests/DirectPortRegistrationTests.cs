using FluxFlow.Core.Application;
using FluxFlow.Core.Execution;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class DirectPortRegistrationTests
{
    [Fact]
    public async Task Native_blocks_without_component_metadata_support_addresses_links_and_concurrent_delivery()
    {
        var services = new ServiceCollection();
        services.AddComponentFactory("first.node", _ => new NativeNode())
            .AddInputPort<NativeNode>("first.node", "receive", node => node.Block)
            .AddOutputPort<NativeNode>("first.node", "send", node => node.Block)
            .AddComponentFactory("second.node", _ => new NativeNode())
            .AddInputPort<NativeNode>("second.node", "Input", node => node.Block)
            .AddOutputPort<NativeNode>("second.node", "Output", node => node.Block);
        services.AddSingleton<LinkFactory>(ports => ports.Link("first.node.send", "second.node.Input"));
        var received = OutputProbe.Register(services, "second.node.Output");
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        var node = (NativeNode)provider.GetRequiredKeyedService<IFlowNode>("first.node");
        var other = (NativeNode)provider.GetRequiredKeyedService<IFlowNode>("second.node");
        Should.Throw<NotSupportedException>(() =>
            ((IDictionary<string, FlowWorkflow>)appExecution.Workflows).Clear());
        provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("first.node.receive").ShouldBeSameAs(node.Block);
        await appExecution.StartAsync();
        Should.Throw<InvalidOperationException>(() => provider.Link("first.node.send", "second.node.Input"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.WhenAll(Enumerable.Range(0, 24).Select(value =>
            node.Block.SendAsync(Request(value), deadline.Token)));
        var values = new List<int>();
        for (var index = 0; index < 24; index++)
            values.Add((await received.Messages.ReceiveAsync(deadline.Token)).Value.Json.GetInt32());
        values.Order().ShouldBe(Enumerable.Range(0, 24));
        static FlowMessage Request(int value) => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value)));
        await appExecution.StopAsync(deadline.Token);
        await appExecution.DisposeAsync();
        node.Disposals.ShouldBe(0);
        other.Disposals.ShouldBe(0);
        await provider.DisposeAsync();
        node.Disposals.ShouldBe(1);
        other.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Duplicate_port_registration_does_not_replace_the_original_or_construct_a_node()
    {
        NativeNode? node = null;
        var services = new ServiceCollection();
        services.AddComponentFactory("main.node", _ => node = new NativeNode())
            .AddInputPort<NativeNode>("main.node", "Input", value => value.Block)
            .AddOutputPort<NativeNode>("main.node", "Output", value => value.Block);
        var original = services.ToArray();
        Should.Throw<ArgumentException>(() => services.AddInputPort<NativeNode>("main.node", "Input", _ => throw new Exception("Must not replace")));
        services.ShouldBe(original);
        node.ShouldBeNull();
        TestSink.Register(services, "main.node.Output");
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Input").ShouldBeSameAs(node!.Block);
        await appExecution.StartAsync();
        await appExecution.StopAsync();
    }

    private sealed class NativeNode : IFlowNode
    {
        public TransformBlock<FlowMessage, FlowMessage> Block { get; } = new(message => message,
            new ExecutionDataflowBlockOptions { BoundedCapacity = 2 });
        public int Disposals { get; private set; }
        public Task Completion => Block.Completion;
        public void Complete() => Block.Complete();
        public void Fault(Exception exception) => ((IDataflowBlock)Block).Fault(exception);
        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }
}
