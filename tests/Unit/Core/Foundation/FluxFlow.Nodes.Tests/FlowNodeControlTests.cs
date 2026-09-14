using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowNodeControlTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Completion_joins_an_accepted_control_after_business_output_completes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var node = new ControlledNode(async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
        });
        await node.Control.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await entered.Task.WaitAsync(Deadline);
        node.Complete();
        try
        {
            await node.Output.Completion.WaitAsync(Deadline);
            node.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await node.Completion.WaitAsync(Deadline);
        node.Control.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Fault_cancels_and_joins_an_active_control_preserving_the_initiating_error()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var node = new ControlledNode(async (_, token) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { exited.SetResult(); }
        });
        await node.Control.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await entered.Task.WaitAsync(Deadline);
        var error = new InvalidOperationException("original");
        node.Fault(error);
        (await Record.ExceptionAsync(() => node.Completion.WaitAsync(Deadline))).ShouldBeSameAs(error);
        exited.Task.IsCompletedSuccessfully.ShouldBeTrue();
        node.Control.Completion.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Controls_cannot_be_added_after_the_port_snapshot_is_exposed()
    {
        await using var node = new ControlledNode((_, _) => Task.CompletedTask);
        node.Ports.Controls["Control"].ShouldBeSameAs(node.Control);
        Should.Throw<InvalidOperationException>(() => node.AddLate());
    }

    private sealed class ControlledNode : FlowNode
    {
        public ControlledNode(Func<FlowMessage, CancellationToken, Task> action)
            => Control = AddControlInput("Control", action, 1);
        public ITargetBlock<FlowMessage> Control { get; }
        public void AddLate() => AddControlInput("Late", (_, _) => Task.CompletedTask, 1);
        protected override Task ProcessAsync(FlowMessage message) => EmitAsync(message, Stopping).AsTask();
    }
}
