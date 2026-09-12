using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ComponentControlIdentityTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Bound_controls_follow_native_identity_not_names_or_signal_metadata(
        bool componentContract, bool controls)
    {
        var node = componentContract ? new ComponentProbe(controls) : new Probe();
        var instance = new ComponentInstance(node, inputs:
        [
            new("Terminal", node.Terminal),
            new("Alias", node.Terminal, ComponentPortKind.Signal),
            new("Ack", node.Business, ComponentPortKind.Signal),
            new("ack", node.Business)
        ]);
        var view = ComponentGraphView.Create(instance);
        try
        {
            var expectedControls = componentContract && controls ? new[] { "Alias", "Terminal" } : [];
            view.Ports.Controls.Keys.Order(StringComparer.Ordinal).ShouldBe(expectedControls);
            view.Ports.Inputs.Keys.Order(StringComparer.Ordinal).ShouldBe(componentContract && controls
                ? ["Ack", "ack"] : ["Ack", "Alias", "Terminal", "ack"]);
            view.Ports.Inputs["Ack"].ShouldBeSameAs(node.Business);
            view.Ports.Inputs["ack"].ShouldBeSameAs(node.Business);
            var terminalPorts = componentContract && controls ? view.Ports.Controls : view.Ports.Inputs;
            terminalPorts["Terminal"].ShouldBeSameAs(node.Terminal);
            terminalPorts["Alias"].ShouldBeSameAs(node.Terminal);
            view.Ports.Events.ShouldBeSameAs(instance.Activity);
            node.DisposeCalls.ShouldBe(0);

            var business = FlowMessage.Create(FlowValue.From("business"));
            var terminal = FlowMessage.Create(FlowValue.From("terminal"));
            (await view.Ports.Inputs["Ack"].SendAsync(business)).ShouldBeTrue();
            (await terminalPorts["Terminal"].SendAsync(terminal)).ShouldBeTrue();
            view.Complete();
            await view.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            node.BusinessMessage.ShouldBeSameAs(business);
            node.TerminalMessage.ShouldBeSameAs(terminal);
            node.DisposeCalls.ShouldBe(0);
        }
        finally { await view.DisposeAsync(); }
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

    private sealed class ComponentProbe : Probe, IFlowComponent
    {
        public ComponentProbe(bool controls)
            => Ports = new FlowPorts(
                inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Business"] = Business },
                controls: controls ? new Dictionary<string, ITargetBlock<FlowMessage>> { ["Ack"] = Terminal } : null);

        public FlowPorts Ports { get; }
    }
}
