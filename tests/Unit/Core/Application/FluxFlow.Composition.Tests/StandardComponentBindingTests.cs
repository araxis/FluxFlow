using System.Text.Json;
using FluxFlow.Composition.Definition;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class StandardComponentBindingTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Standard_ports_share_native_blocks_without_owning_them()
    {
        await using var node = new ProbeComponent();
        var ports = FlowPorts.CreateStandard(node.NativeInput, node.NativeOutput, node.NativeEvents);
        ports.Inputs.Keys.ShouldBe(["Input"]);
        ports.Outputs.Keys.ShouldBe(["Output"]);
        ports.Inputs["Input"].ShouldBeSameAs(node.NativeInput);
        ports.Outputs["Output"].ShouldBeSameAs(node.NativeOutput);
        ports.Events.ShouldBeSameAs(node.NativeEvents);
        ports.Controls.ShouldBeEmpty();
        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("shared", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        (await ports.Inputs["Input"].SendAsync(message)).ShouldBeTrue();
        (await node.NativeInput.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        node.Completion.IsCompleted.ShouldBeFalse();
        node.DisposeCount.ShouldBe(0);

        await using var application = new FlowApplication();
        var component = application.AddWorkflow("main").Add("node", node, ComponentOwnership.Borrowed);
        component.Input.Address.ShouldBe("main.node.Input");
        component.Output.Address.ShouldBe("main.node.Output");
        component.Input.ShouldBeSameAs(component.Inputs["Input"]);
        component.Output.ShouldBeSameAs(component.Outputs["Output"]);
        application.GetInput("main.node.Input").ShouldBeSameAs(component.Input);
        application.GetOutput("main.node.Output").ShouldBeSameAs(component.Output);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("output")]
    [InlineData("events")]
    public async Task Standard_port_construction_rejects_missing_blocks(string missing)
    {
        await using var node = new ProbeComponent();
        Should.Throw<ArgumentNullException>(() => FlowPorts.CreateStandard(
            missing == "input" ? null! : node.NativeInput,
            missing == "output" ? null! : node.NativeOutput,
            missing == "events" ? null! : node.NativeEvents)).ParamName.ShouldBe(missing);
        node.Completion.IsCompleted.ShouldBeFalse();
        node.DisposeCount.ShouldBe(0);
    }

    private sealed class ProbeComponent : IFlowComponent
    {
        private readonly bool _failCleanup;
        public BufferBlock<FlowMessage> NativeInput { get; } = new(new DataflowBlockOptions { BoundedCapacity = 2 });
        public BufferBlock<FlowMessage> NativeOutput { get; } = new(new DataflowBlockOptions { BoundedCapacity = 2 });
        public BufferBlock<FlowMessage> NativeEvents { get; } = new(new DataflowBlockOptions { BoundedCapacity = 2 });
        public Exception CleanupFailure { get; } = new InvalidOperationException("expected cleanup failure");
        public int DisposeCount { get; private set; }

        public ProbeComponent(string? difference = null, bool failCleanup = false)
        {
            _failCleanup = failCleanup;
            var inputs = new Dictionary<string, ITargetBlock<FlowMessage>>
            { [difference == "inputName" ? "Other" : "Input"] = NativeInput };
            var outputs = new Dictionary<string, ISourceBlock<FlowMessage>>
            { [difference == "outputName" ? "Other" : "Output"] = NativeOutput };
            if (difference == "extraInput") inputs.Add("Other", NativeInput);
            if (difference == "extraOutput") outputs.Add("Other", NativeOutput);
            if (difference == "noInput") inputs.Clear();
            if (difference == "noOutput") outputs.Clear();
            Ports = difference == "noPorts" ? null! : new FlowPorts(inputs, outputs,
                difference == "noEvents" ? null : NativeEvents,
                difference == "control" ? new Dictionary<string, ITargetBlock<FlowMessage>> { ["Stop"] = NativeInput } : null);
            Completion = Task.WhenAll(NativeInput.Completion, NativeOutput.Completion, NativeEvents.Completion);
        }

        public FlowPorts Ports { get; }
        public Task Completion { get; }
        public void Complete() { NativeInput.Complete(); NativeOutput.Complete(); NativeEvents.Complete(); }
        public void Fault(Exception exception)
        {
            ((IDataflowBlock)NativeInput).Fault(exception);
            ((IDataflowBlock)NativeOutput).Fault(exception);
            ((IDataflowBlock)NativeEvents).Fault(exception);
        }
        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            Complete();
            await Completion.WaitAsync(Deadline);
            if (_failCleanup) throw CleanupFailure;
        }
    }
}
