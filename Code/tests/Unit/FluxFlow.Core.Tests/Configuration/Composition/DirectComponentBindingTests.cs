using FluxFlow.Application.Addressing;
using FluxFlow.Application;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class DirectComponentBindingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Named_events_preserve_native_sources_and_normal_outputs_can_be_named_Events(bool namedEvents)
    {
        var node = new Probe();
        var services = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.probe"), _ => node)
            .AddInputPort<Probe>(ComponentAddress.Parse("main.probe"), "Start", n => n.Input);
        if (namedEvents) services.AddEventPort<Probe>(ComponentAddress.Parse("main.probe"), "First", n => n.Output)
            .AddEventPort<Probe>(ComponentAddress.Parse("main.probe"), "Second", n => n.Events);
        else services.AddOutputPort<Probe>(ComponentAddress.Parse("main.probe"), "Events", n => n.Output);
        await using var provider = services.BuildServiceProvider();
        var instance = await provider.BuildApplicationAsync();
        try
        {
            instance.GetTestInput("main.probe.Start").ShouldBeSameAs(node.Input);
            var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("first", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            var first = instance.GetOutput("main.probe." + (namedEvents ? "First" : "Events")).Block.ReceiveAsync(TimeSpan.FromSeconds(5));
            node.Output.Post(message).ShouldBeTrue();
            (await first).Value.ShouldBe(message.Value);
            if (namedEvents)
            {
                var second = instance.GetOutput("main.probe.Second").Block.ReceiveAsync(TimeSpan.FromSeconds(5));
                node.Events.Post(message).ShouldBeTrue();
                (await second).Value.ShouldBe(message.Value);
                provider.GetPortRegistrations<ISourceBlock<FlowMessage>>().Select(p => p.PortName).Order().ShouldBe(["First", "Second"]);
            }
            else
            {
                instance.GetOutput("main.probe.Events").Block.ShouldBeSameAs(node.Output);
                provider.GetPortRegistrations<ISourceBlock<FlowMessage>>().Select(p => p.PortName).ShouldBe(["Events"]);
            }
        }
        finally { await instance.DisposeAsync(); }
        node.Disposals.ShouldBe(1);
        await provider.DisposeAsync();
        node.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Undeclared_events_are_not_injected_and_removed_policy_name_is_available()
    {
        var plain = new Probe();
        await using var provider = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.plain"), _ => plain).BuildServiceProvider();
        var instance = await provider.BuildApplicationAsync();
        provider.GetPortRegistrations<ISourceBlock<FlowMessage>>().ShouldBeEmpty();
        await instance.DisposeAsync();
        var constructions = 0;
        var services = new ServiceCollection().AddComponentFactory(ComponentAddress.Parse("main.bad"), _ => { constructions++; return new Probe(); });
        services.AddOutputPort<Probe>(ComponentAddress.Parse("main.bad"), "DiscardUnmatched", n => n.Output);
        constructions.ShouldBe(0);
    }

    private sealed class Probe : IFlowNode
    {
        public BufferBlock<FlowMessage> Input { get; } = new();
        public BufferBlock<FlowMessage> Output { get; } = new();
        public BufferBlock<FlowMessage> Events { get; } = new();
        public int Disposals;
        public Task Completion => Task.WhenAll(Input.Completion, Output.Completion, Events.Completion);
        public void Complete() { Input.Complete(); Output.Complete(); Events.Complete(); }
        public void Fault(Exception error) { }
        public ValueTask DisposeAsync() { Disposals++; Complete(); return ValueTask.CompletedTask; }
    }
}
