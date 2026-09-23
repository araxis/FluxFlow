using FluxFlow.Core.Application;
using FluxFlow.Core.Execution;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ComponentControlIdentityTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Controls_follow_native_identity_not_port_names(
        bool componentContract, bool controls)
    {
        var node = componentContract ? new ComponentProbe(controls) : new Probe();
        await using var provider = new ServiceCollection()
            .AddComponentFactory("main.node", _ => node)
            .AddInputPort<Probe>("main.node", "Terminal", value => value.Terminal)
            .AddInputPort<Probe>("main.node", "Alias", value => value.Terminal)
            .AddInputPort<Probe>("main.node", "Ack", value => value.Business)
            .AddInputPort<Probe>("main.node", "ack", value => value.Business)
            .BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        try
        {
            var expectedControls = componentContract && controls ? new[] { "Alias", "Terminal" } : [];
            provider.GetPortRegistrations<ITargetBlock<FlowMessage>>()
                .Where(port => ((IFlowNode)node).TerminalInputs.Any(input => ReferenceEquals(input,
                    provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>(port.Address))))
                .Select(port => FluxFlow.Core.Addressing.ApplicationAddress.Parse(port.Address).Segments[^1]).Order(StringComparer.Ordinal).ShouldBe(expectedControls);
            provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Ack").ShouldBeSameAs(node.Business);
            provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.ack").ShouldBeSameAs(node.Business);
            provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Terminal").ShouldBeSameAs(node.Terminal);
            provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Alias").ShouldBeSameAs(node.Terminal);
            node.DisposeCalls.ShouldBe(0);

            var business = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("business", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            var terminal = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("terminal", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            (await node.Business.SendAsync(business)).ShouldBeTrue();
            (await node.Terminal.SendAsync(terminal)).ShouldBeTrue();
            node.Complete();
            await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            node.BusinessMessage.ShouldBeSameAs(business);
            node.TerminalMessage.ShouldBeSameAs(terminal);
            node.DisposeCalls.ShouldBe(0);
        }
        finally { await appExecution.DisposeAsync(); }
        node.DisposeCalls.ShouldBe(0);
        await provider.DisposeAsync();
        node.DisposeCalls.ShouldBe(1);
    }

    private class Probe : IFlowNode
    {
        public Probe()
        {
            Business = new ActionBlock<FlowMessage>(message => BusinessMessage = message);
            Terminal = new ActionBlock<FlowMessage>(message => TerminalMessage = message);
            Completion = Task.WhenAll(Business.Completion, Terminal.Completion);
        }

        public ActionBlock<FlowMessage> Business { get; }
        public ActionBlock<FlowMessage> Terminal { get; }
        public FlowMessage? BusinessMessage { get; private set; }
        public FlowMessage? TerminalMessage { get; private set; }
        public int DisposeCalls { get; private set; }
        public Task Completion { get; }
        public void Complete() { Business.Complete(); Terminal.Complete(); }
        public void Fault(Exception exception)
        {
            ((IDataflowBlock)Business).Fault(exception);
            ((IDataflowBlock)Terminal).Fault(exception);
        }
        public async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            Complete();
            await Completion;
        }
    }

    private sealed class ComponentProbe : Probe, IFlowNode
    {
        private readonly bool _controls;
        public ComponentProbe(bool controls) => _controls = controls;
        public IEnumerable<ITargetBlock<FlowMessage>> TerminalInputs => _controls ? [Terminal] : [];
    }
}
