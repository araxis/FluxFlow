using FluxFlow.Application;
using FluxFlow.Application.Execution;
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
        finally
        {
            release.TrySetResult();
        }

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
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            finally
            {
                exited.SetResult();
            }
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
    public async Task Controls_cannot_be_registered_after_completion_starts()
    {
        await using var node = new ControlledNode((_, _) => Task.CompletedTask);
        node.TerminalInputs.Single().ShouldBeSameAs(node.Control);
        node.Complete();
        Should.Throw<InvalidOperationException>(() => node.Coordinator.HasTerminalInput(node.Control));
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
        finally
        {
            fail.TrySetResult();
        }

        (await Record.ExceptionAsync(() => node.Completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        node.Wait.Completion.IsCompleted.ShouldBeTrue();
        node.Exited.ShouldBeTrue();
    }

    private sealed class LateFailureNode : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }
        public ITargetBlock<FlowMessage> Wait { get; }
        public ITargetBlock<FlowMessage> Fail { get; }

        public bool Exited;
        public LateFailureNode(TaskCompletionSource entered, TaskCompletionSource fail, Exception error)
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            var lifetime = Lifetime;
            Wait = lifetime.HasTerminalInput(new ActionBlock<FlowMessage>(controlMessage => FlowBlockExtensions.ProcessControlAsync(controlMessage, this, "wait", async (_, stopping) =>
            {
                entered.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, stopping);
                }
                finally
                {
                    Exited = true;
                }
            }, lifetime.Stopping, Fault), new ExecutionDataflowBlockOptions { BoundedCapacity = 1 }));
            Fail = lifetime.HasTerminalInput(new ActionBlock<FlowMessage>(controlMessage => FlowBlockExtensions.ProcessControlAsync(controlMessage, this, "fail", async (_, _) =>
            {
                await fail.Task;
                throw error;
            }, lifetime.Stopping, Fault), new ExecutionDataflowBlockOptions { BoundedCapacity = 1 }));
            Lifetime.Observe();
        }

        private Task ProcessAsync(FlowMessage message) => Task.CompletedTask;
    }

    private sealed class LabeledNode : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public int FirstCalls;
        public int SecondCalls;
        public ITargetBlock<FlowMessage> First { get; }
        public ITargetBlock<FlowMessage> Second { get; }

        public LabeledNode(string label)
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            var lifetime = Lifetime;
            First = lifetime.HasTerminalInput(new ActionBlock<FlowMessage>(controlMessage => FlowBlockExtensions.ProcessControlAsync(controlMessage, this, label, (_, _) =>
            {
                FirstCalls++;
                return Task.CompletedTask;
            }, lifetime.Stopping, Fault), new ExecutionDataflowBlockOptions { BoundedCapacity = 1 }));
            Second = lifetime.HasTerminalInput(new ActionBlock<FlowMessage>(controlMessage => FlowBlockExtensions.ProcessControlAsync(controlMessage, this, label, (_, _) =>
            {
                SecondCalls++;
                return Task.CompletedTask;
            }, lifetime.Stopping, Fault), new ExecutionDataflowBlockOptions { BoundedCapacity = 1 }));
            Lifetime.Observe();
        }

        private Task ProcessAsync(FlowMessage message) => Task.CompletedTask;
    }

    private sealed class ControlledNode : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public ControlledNode(Func<FlowMessage, CancellationToken, Task> action)
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            var lifetime = Lifetime;
            Control = lifetime.HasTerminalInput(new ActionBlock<FlowMessage>(controlMessage => FlowBlockExtensions.ProcessControlAsync(controlMessage, this, "Control", action, lifetime.Stopping, Fault), new ExecutionDataflowBlockOptions { BoundedCapacity = 1 }));
            Lifetime.Observe();
        }

        public ITargetBlock<FlowMessage> Control { get; }
        public FlowLifetime Coordinator => Lifetime;

        private Task ProcessAsync(FlowMessage message) => EmitAsync(message, Stopping).AsTask();
    }
}
