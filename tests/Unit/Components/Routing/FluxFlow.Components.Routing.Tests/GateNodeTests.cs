using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Data;
using FluxFlow.Core;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class GateNodeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Closed_gate_drops_values_but_keeps_errors_and_open_gate_preserves_envelopes()
    {
        await using var gate = new GateNode();
        gate.IsOpen.ShouldBeFalse();
        await AssertDroppedAsync(gate);
        await gate.Open.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await WaitForStateAsync(gate, true);
        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web))), CorrelationId.New());
        var forwarded = gate.Output.ReceiveAsync(Deadline);
        await gate.Input.SendAsync(message);
        (await forwarded).ShouldBeSameAs(message);
        await gate.Close.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await WaitForStateAsync(gate, false);
        await AssertDroppedAsync(gate);
        gate.Complete();
        await gate.Completion.WaitAsync(Deadline);
        gate.Open.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        gate.Close.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        (await gate.Open.SendAsync(message)).ShouldBeFalse();
    }

    [Fact]
    public async Task Initially_open_gate_drains_data_and_closes_controls_when_input_completes()
    {
        await using var gate = new GateNode(initiallyOpen: true, boundedCapacity: 1);
        var messages = new List<FlowMessage>();
        var sink = new ActionBlock<FlowMessage>(messages.Add);
        using var link = gate.Output.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        var sent = Enumerable.Range(0, 20).Select(i => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(i, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToArray();
        foreach (var message in sent) (await gate.Input.SendAsync(message)).ShouldBeTrue();
        gate.Input.Complete();
        await gate.Completion.WaitAsync(Deadline);
        await sink.Completion.WaitAsync(Deadline);
        messages.ShouldBe(sent);
        gate.Open.Completion.IsCompleted.ShouldBeTrue();
        gate.Close.Completion.IsCompleted.ShouldBeTrue();
        gate.Ports.Controls["Open"].ShouldBeSameAs(gate.Open);
        gate.Ports.Controls["Close"].ShouldBeSameAs(gate.Close);
    }

    [Fact]
    public async Task Fault_releases_backpressured_data_and_finishes_controls()
    {
        await using var gate = new GateNode(initiallyOpen: true, boundedCapacity: 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new ActionBlock<FlowMessage>(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        }, new ExecutionDataflowBlockOptions { BoundedCapacity = 1 });
        using var link = gate.Output.LinkTo(sink);
        try
        {
            var admissions = Enumerable.Range(0, 20)
                .Select(i => gate.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(i, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ToArray();
            await entered.Task.WaitAsync(Deadline);
            admissions.Any(task => !task.IsCompleted).ShouldBeTrue();
            var failure = new InvalidOperationException("stop gate");
            gate.Fault(failure);
            (await Record.ExceptionAsync(() => gate.Completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
            (await Task.WhenAll(admissions).WaitAsync(Deadline)).ShouldContain(false);
            gate.Open.Completion.IsCompleted.ShouldBeTrue();
            gate.Close.Completion.IsCompleted.ShouldBeTrue();
        }
        finally
        {
            release.TrySetResult();
            sink.Complete();
            await sink.Completion.WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task Application_can_link_data_back_to_close_control_and_shutdown()
    {
        await using var application = new FlowApplication();
        var instance = new GateNode(initiallyOpen: true);
        var gate = application.AddWorkflow("main").Add("gate", instance);
        gate.Output.To(gate.Inputs["Close"]);
        await application.StartAsync();
        (await application.SendAsync(gate.Input, FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
        await WaitForStateAsync(instance, false);
        await application.StopAsync().WaitAsync(Deadline);
        await application.Completion.WaitAsync(Deadline);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Capacity_must_be_positive(int capacity)
        => Should.Throw<ArgumentOutOfRangeException>(() => new GateNode(boundedCapacity: capacity));

    private static async Task AssertDroppedAsync(GateNode gate)
    {
        // FlowOutput is live: attach the receiver before production, without relying on scheduler timing.
        var received = gate.Output.ReceiveAsync(Deadline);
        await gate.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("drop", new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        // The error is also a FIFO barrier: no time-based absence assertion is needed.
        var error = FlowMessage.CreateError(new FlowError("upstream", "original error", "test"));
        await gate.Input.SendAsync(error);
        (await received).ShouldBeSameAs(error);
    }

    private static async Task WaitForStateAsync(GateNode gate, bool open)
    {
        using var timeout = new CancellationTokenSource(Deadline);
        while (gate.IsOpen != open) await Task.Delay(1, timeout.Token);
    }
}
