using FluxFlow.Core.Application;
using FluxFlow.Core.Execution;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
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
    public async Task Controls_cannot_be_added_after_exposing_the_business_input()
    {
        await using var node = new ControlledNode((_, _) => Task.CompletedTask);
        _ = node.Input;
        node.TerminalInputs.Single().ShouldBeSameAs(node.Control);
        Should.Throw<InvalidOperationException>(() => node.AddLate());
    }

    [Theory]
    [InlineData("Input")]
    [InlineData("broker.ack / received")]
    public async Task Diagnostic_labels_do_not_register_or_identify_ports(string label)
    {
        await using var node = new LabeledNode(label);
        node.TerminalInputs.Count().ShouldBe(2);
        node.First.ShouldNotBeSameAs(node.Second);
        await node.First.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await node.Second.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        node.Complete();
        await node.Completion.WaitAsync(Deadline);
        node.FirstCalls.ShouldBe(1);
        node.SecondCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Control_failure_after_output_drains_cancels_sibling_and_faults_completion()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("late control failure");
        await using var node = new LateFailureNode(entered, fail, failure);
        await node.Wait.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await entered.Task.WaitAsync(Deadline);
        await node.Fail.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        node.Complete();
        try
        {
            await node.Output.Completion.WaitAsync(Deadline);
            node.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { fail.TrySetResult(); }
        (await Record.ExceptionAsync(() => node.Completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        node.Wait.Completion.IsCompleted.ShouldBeTrue();
        node.Exited.ShouldBeTrue();
    }

    private sealed class LateFailureNode : FlowNode
    {
        public ITargetBlock<FlowMessage> Wait { get; }
        public ITargetBlock<FlowMessage> Fail { get; }
        public bool Exited;
        public LateFailureNode(TaskCompletionSource entered, TaskCompletionSource fail, Exception error)
        {
            Wait = AddControlInput("wait", async (_, stopping) =>
            {
                entered.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, stopping); }
                finally { Exited = true; }
            }, 1);
            Fail = AddControlInput("fail", async (_, _) => { await fail.Task; throw error; }, 1);
        }
        protected override Task ProcessAsync(FlowMessage message) => Task.CompletedTask;
    }

    private sealed class LabeledNode : FlowNode
    {
        public int FirstCalls;
        public int SecondCalls;
        public ITargetBlock<FlowMessage> First { get; }
        public ITargetBlock<FlowMessage> Second { get; }
        public LabeledNode(string label)
        {
            First = AddControlInput(label, (_, _) => { FirstCalls++; return Task.CompletedTask; }, 1);
            Second = AddControlInput(label, (_, _) => { SecondCalls++; return Task.CompletedTask; }, 1);
        }
        protected override Task ProcessAsync(FlowMessage message) => Task.CompletedTask;
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
