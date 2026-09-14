using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
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
        using var provider = new ServiceCollection().AddComponent("probe", context =>
        {
            context.HasInput("Start", node.Input, links: ComponentPortLinkCardinality.Single);
            if (namedEvents)
            {
                context.HasEvents("First", node.Output);
                context.HasEvents("Second", node.Events);
            }
            else context.HasOutput("Events", node.Output);
            return node;
        }).BuildServiceProvider();
        var instance = await FluxFlowRegistrationExtensions.CreateFactoryMap(provider)["probe"](
            new ComponentActivationContext(provider, "main", "probe", new ComponentDefinition("probe")));
        try
        {
            instance.Inputs["Start"].Target.ShouldBeSameAs(node.Input);
            instance.InputMetadata["Start"].LinkCardinality.ShouldBe(ComponentPortLinkCardinality.Single);
            var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("first", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            var first = instance.Outputs[namedEvents ? "First" : "Events"].Source.ReceiveAsync(TimeSpan.FromSeconds(5));
            node.Output.Post(message).ShouldBeTrue();
            (await first).Value.ShouldBe(message.Value);
            if (namedEvents)
            {
                var second = instance.Outputs["Second"].Source.ReceiveAsync(TimeSpan.FromSeconds(5));
                node.Events.Post(message).ShouldBeTrue();
                (await second).Value.ShouldBe(message.Value);
                instance.OutputMetadata.Keys.Order().ShouldBe(["First", "Second"]);
            }
            else
            {
                instance.Outputs["Events"].Source.ShouldBeSameAs(node.Output);
                instance.OutputMetadata.Keys.ShouldBe(["Events"]);
            }
        }
        finally { await instance.DisposeAsync(); }
        node.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Undeclared_events_are_not_injected_and_reserved_names_fail_with_cleanup()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var plain = new Probe();
        var context = new ComponentActivationContext(services, "main", "plain", new ComponentDefinition("probe"));
        var instance = await context.BindAsync(plain);
        instance.OutputMetadata.ShouldBeEmpty();
        await instance.DisposeAsync();
        var invalid = new Probe();
        var bad = new ComponentActivationContext(services, "main", "bad", new ComponentDefinition("probe"));
        bad.HasOutput("DiscardUnmatched", invalid.Output);
        await Should.ThrowAsync<ArgumentException>(async () => await bad.BindAsync(invalid));
        invalid.Disposals.ShouldBe(1);
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
