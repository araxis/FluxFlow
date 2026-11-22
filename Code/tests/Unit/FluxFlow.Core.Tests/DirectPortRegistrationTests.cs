using FluxFlow.Application.Addressing;
using FluxFlow.Application;
using FluxFlow.Application.Execution;
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
        services.AddComponentFactory(ComponentAddress.Parse("first.node"), _ => new NativeNode())
            .AddInputPort<NativeNode>(ComponentAddress.Parse("first.node"), "receive", node => node.Block)
            .AddOutputPort<NativeNode>(ComponentAddress.Parse("first.node"), "send", node => node.Block)
            .AddComponentFactory(ComponentAddress.Parse("second.node"), _ => new NativeNode())
            .AddInputPort<NativeNode>(ComponentAddress.Parse("second.node"), "Input", node => node.Block)
            .AddOutputPort<NativeNode>(ComponentAddress.Parse("second.node"), "Output", node => node.Block);
        services.AddSingleton<LinkFactory>(ports => ports.Link(PortAddress.Parse("first.node.send"), PortAddress.Parse("second.node.Input")));
        var received = OutputProbe.Register(services, "second.node.Output");
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        var node = (NativeNode)provider.GetWorkflowServices("first").GetRequiredKeyedService<IFlowNode>("first.node");
        var other = (NativeNode)provider.GetWorkflowServices("second").GetRequiredKeyedService<IFlowNode>("second.node");
        Should.Throw<NotSupportedException>(() =>
            ((ICollection<FlowWorkflow>)appExecution.Workflows).Clear());
        provider.GetWorkflowServices("first").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("first.node.receive").ShouldBeSameAs(node.Block);
        await appExecution.StartAsync();
        Should.Throw<InvalidOperationException>(() => provider.Link(PortAddress.Parse("first.node.send"), PortAddress.Parse("second.node.Input")));
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
        node.Disposals.ShouldBe(1);
        other.Disposals.ShouldBe(1);
        await provider.DisposeAsync();
        node.Disposals.ShouldBe(1);
        other.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Host_keyed_blocks_are_not_implicitly_application_outputs()
    {
        var services = new ServiceCollection();
        var address = ComponentAddress.Parse("main.node");
        services.AddComponentFactory(address, _ => new NativeNode())
            .AddInputPort<NativeNode>(address, "Input", node => node.Block);
        services.AddKeyedSingleton<ISourceBlock<FlowMessage>>(address.Port("HostOutput").Value,
            (provider, _) => ((NativeNode)provider.GetWorkflowServices(address.WorkflowName).GetRequiredKeyedService<IFlowNode>(address.Value)).Block);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        Should.Throw<KeyNotFoundException>(() => provider.Link(address.Port("HostOutput"), address.Port("Input")))
            .Message.ShouldContain("HostOutput");
        app.Workflows.Single().Links.ShouldBeEmpty();
    }

    [Fact]
    public async Task Duplicate_port_registration_does_not_replace_the_original_or_construct_a_node()
    {
        NativeNode? node = null;
        var services = new ServiceCollection();
        services.AddComponentFactory(ComponentAddress.Parse("main.node"), _ => node = new NativeNode())
            .AddInputPort<NativeNode>(ComponentAddress.Parse("main.node"), "Input", value => value.Block)
            .AddOutputPort<NativeNode>(ComponentAddress.Parse("main.node"), "Output", value => value.Block);
        var original = services.ToArray();
        Should.Throw<ArgumentException>(() => services.AddInputPort<NativeNode>(ComponentAddress.Parse("main.node"), "Input", _ => throw new Exception("Must not replace")));
        services.ShouldBe(original);
        node.ShouldBeNull();
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        provider.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Input").ShouldBeSameAs(node!.Block);
        await appExecution.StartAsync();
        await appExecution.StopAsync();
    }

    [Theory]
    [InlineData("", "input")]
    [InlineData("bad.name", "output")]
    [InlineData(" padded ", "events")]
    public void Invalid_port_names_fail_before_registration_or_selection(string name, string kind)
    {
        var services = new ServiceCollection();
        var address = ComponentAddress.Parse("main.node");
        services.AddComponentFactory(address, _ => throw new Exception("Must not construct"));
        var before = services.ToArray();
        Should.Throw<ArgumentException>(() =>
        {
            if (kind == "input") services.AddInputPort<NativeNode>(address, name, _ => throw new Exception("Must not select"));
            else if (kind == "output") services.AddOutputPort<NativeNode>(address, name, _ => throw new Exception("Must not select"));
            else services.AddEventPort<NativeNode>(address, name, _ => throw new Exception("Must not select"));
        });
        services.ShouldBe(before);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unlinked_invalid_ports_fail_build_before_links_and_DI_owns_all_created_nodes(bool input)
    {
        var nodes = new List<NativeNode>();
        var linksCalled = false;
        var services = new ServiceCollection();
        foreach (var address in new[] { "first.node", "second.node" })
            services.AddComponentFactory(ComponentAddress.Parse(address), _ =>
            {
                var node = new NativeNode();
                nodes.Add(node);
                return node;
            });
        if (input) services.AddInputPort<NativeNode>(ComponentAddress.Parse("first.node"), "Invalid", _ => null!);
        else services.AddOutputPort<NativeNode>(ComponentAddress.Parse("first.node"), "Invalid", _ => null!);
        services.AddSingleton<LinkFactory>(_ => { linksCalled = true; });
        nodes.ShouldBeEmpty();

        await using var provider = services.BuildServiceProvider();
        var error = await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync());
        error.Message.ShouldContain("first.node.Invalid");
        linksCalled.ShouldBeFalse();
        nodes.Count.ShouldBe(2);
        foreach (var node in nodes)
        {
            await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            node.Disposals.ShouldBe(1);
        }
        await provider.DisposeAsync();
        foreach (var node in nodes) node.Disposals.ShouldBe(1);
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
