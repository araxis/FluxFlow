using FluxFlow.Core.Nodes;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;
public sealed class AdditionalInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Additional_transform_input_orders_results_and_drains_before_flush(bool ordered)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = FlowMessage.Create((FlowValue)"first");
        var second = FlowMessage.Create((FlowValue)"second");
        var flushed = FlowMessage.Create((FlowValue)"flushed");
        await using var node = new CommandProbe(async message =>
        {
            if (ReferenceEquals(message, first))
                await release.Task;
            else
                secondEntered.TrySetResult();
            throw new InvalidOperationException("command failed");
        }, new ExecutionDataflowBlockOptions { BoundedCapacity = 2, MaxDegreeOfParallelism = 2, EnsureOrdered = ordered }, flushed);
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });
        try
        {
            (await node.Other.SendAsync(first)).ShouldBeTrue();
            (await node.Other.SendAsync(second)).ShouldBeTrue();
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (ordered)
                output.Count.ShouldBe(0);
            else
                (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).CausationId.ShouldBe(second.MessageId);
            node.Complete();
            node.Completion.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            release.TrySetResult();
        }

        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).CausationId.ShouldBe(first.MessageId);
        if (ordered)
            (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).CausationId.ShouldBe(second.MessageId);
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(flushed);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Additional_transform_input_forwards_errors_without_invoking_the_command()
    {
        var calls = 0;
        var flushed = FlowMessage.Create((FlowValue)"flushed");
        await using var node = new CommandProbe(_ =>
        {
            calls++;
            return Task.CompletedTask;
        }, new(), flushed);
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });
        var error = FlowMessage.Create(new FlowError("incoming", "failure", "test"));
        await node.Other.SendAsync(error);
        await node.Other.SendAsync(FlowMessage.Create((FlowValue)"command"));
        node.Complete();
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(error);
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(flushed);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        calls.ShouldBe(1);
    }

    private sealed class CommandProbe : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        private readonly FlowMessage _flushed;
        public ITargetBlock<FlowMessage> Other { get; }

        public CommandProbe(Func<FlowMessage, Task> action, ExecutionDataflowBlockOptions options, FlowMessage flushed)
        {
            var execution = options ?? new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 1;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, flush: () => Output.FlushMessagesAsync(FlushAsync, Stopping), cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            var lifetime = Lifetime;
            _flushed = flushed;
            Other = lifetime.HasInput(new TransformManyBlock<FlowMessage, FlowMessage>(commandMessage => commandMessage.ProcessMessageAsync(this, async value =>
            {
                await action(value).ConfigureAwait(false);
                return[];
            }, Output, Stopping), execution));
            ((ISourceBlock<FlowMessage>)Other).LinkTo((ITargetBlock<FlowMessage>)Output);
            Lifetime.Observe();
        }

        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message) => Task.FromResult<IEnumerable<FlowMessage>>([]);
        private ValueTask<IEnumerable<FlowMessage>> FlushAsync() => ValueTask.FromResult<IEnumerable<FlowMessage>>([_flushed]);
    }

    [Fact]
    public async Task Additional_input_errors_become_data_and_later_messages_continue()
    {
        await using var node = new Probe(false);
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        await node.Other.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeTrue();
        await node.Other.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeFalse();
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shutdown_joins_or_cancels_active_additional_input(bool fault)
    {
        await using var node = new Probe(true);
        await node.Other.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await node.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (fault)
            node.Fault(new InvalidOperationException("stop"));
        else
        {
            node.Complete();
            node.Completion.IsCompleted.ShouldBeFalse();
            node.Release.TrySetResult();
        }

        if (fault)
            await Should.ThrowAsync<InvalidOperationException>(() => node.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        else
            await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        node.Other.Completion.IsCompleted.ShouldBeTrue();
    }

    private sealed class Probe : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        private int _calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ITargetBlock<FlowMessage> Other { get; }

        public Probe(bool wait)
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
            Other = lifetime.HasInput(new TransformManyBlock<FlowMessage, FlowMessage>(commandMessage => commandMessage.ProcessMessageAsync(this, async value =>
            {
                Entered.TrySetResult();
                if (wait)
                    await Release.Task.WaitAsync(Stopping);
                else if (++_calls == 1)
                    throw new InvalidOperationException("bad value");
                await EmitAsync(value, Stopping);
            }, Output, Stopping), new ExecutionDataflowBlockOptions { BoundedCapacity = 128 }));
            ((ISourceBlock<FlowMessage>)Other).LinkTo((ITargetBlock<FlowMessage>)Output);
            Lifetime.Observe();
        }

        private Task ProcessAsync(FlowMessage message) => Task.CompletedTask;
    }
}
