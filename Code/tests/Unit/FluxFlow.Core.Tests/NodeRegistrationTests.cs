using FluxFlow.Application.Addressing;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Execution;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class NodeRegistrationTests
{
    [Fact]
    public async Task Node_and_native_ports_resolve_by_key_without_workflow_lookup()
    {
        var node = new NativeNode();
        var services = new ServiceCollection();
        services.AddComponentFactory(ComponentAddress.Parse("main.node"), _ => node)
            .AddInputPort<NativeNode>(ComponentAddress.Parse("main.node"), "Input", value => value.Input);
        await using var provider = services.BuildServiceProvider();
        provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.node").ShouldBeSameAs(node);
        provider.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Input").ShouldBeSameAs(node.Input);
        provider.GetWorkflowServices("main").GetKeyedService<ITargetBlock<FlowMessage>>("main.node.Missing").ShouldBeNull();
        await using var app = await provider.BuildApplicationAsync();
        ((IFlowNode)node).TerminalInputs.ShouldBeEmpty();
    }

    [Fact]
    public async Task External_callbacks_choose_port_names_for_the_same_component_type()
    {
        var services = new ServiceCollection();
        services.AddComponentFactory(ComponentAddress.Parse("main.automatic"), _ => new NativeNode())
            .AddInputPort<NativeNode>(ComponentAddress.Parse("main.automatic"), "Input", node => node.Input)
            .AddComponentFactory(ComponentAddress.Parse("main.explicit"), _ => new NativeNode())
            .AddInputPort<NativeNode>(ComponentAddress.Parse("main.explicit"), "Custom", node => node.Input);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        foreach (var (component, name) in new[] { ("automatic", "Input"), ("explicit", "Custom") })
        {
            var node = (NativeNode)provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main." + component);
            provider.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>($"main.{component}.{name}").ShouldBeSameAs(node.Input);
        }
        provider.GetWorkflowServices("main").GetKeyedService<ITargetBlock<FlowMessage>>("main.explicit.Input").ShouldBeNull();
    }

    private class NativeNode : IFlowNode
    {
        public ActionBlock<FlowMessage> Input { get; } = new(_ => { });
        public Task Completion => Input.Completion;
        public void Complete() => Input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)Input).Fault(error);
        public async ValueTask DisposeAsync() { Complete(); await Completion; }
    }

}
