using FluxFlow.Core.Nodes;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class NativeProcessingOptionsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Native_cancellation_stops_active_processing_and_disposal(bool transform, bool preCanceled)
    {
        using var cancellation = new CancellationTokenSource();
        if (preCanceled) cancellation.Cancel();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<FlowMessage> Process(FlowMessage message, CancellationToken stopping)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, stopping);
            return message;
        }
        var options = new ExecutionDataflowBlockOptions { BoundedCapacity = 1, CancellationToken = cancellation.Token };
        IFlowNode node;
        ITargetBlock<FlowMessage> input;
        ISourceBlock<FlowMessage> output;
        ISourceBlock<FlowMessage> events;
        if (transform)
        {
            var instance = new Transform(Process, options);
            node = instance;
            input = instance.Input;
            output = instance.Output;
            events = instance.Events;
        }
        else
        {
            var instance = new Action(Process, options);
            node = instance;
            input = instance.Input;
            output = instance.Output;
            events = instance.Events;
        }
        if (!preCanceled)
        {
            (await input.SendAsync(Message())).ShouldBeTrue();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
        }
        await Should.ThrowAsync<OperationCanceledException>(() => node.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        await Should.ThrowAsync<OperationCanceledException>(() => output.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        await events.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        (await input.SendAsync(Message())).ShouldBeFalse();
        await node.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Native_unordered_transform_releases_second_result_before_blocked_first()
    {
        var first = Message();
        var second = Message();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var node = new Transform(async (message, stopping) =>
        {
            if (ReferenceEquals(message, first)) await release.Task.WaitAsync(stopping);
            return message;
        }, new ExecutionDataflowBlockOptions { BoundedCapacity = 2, MaxDegreeOfParallelism = 2, EnsureOrdered = false });
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        try
        {
            (await node.Input.SendAsync(first)).ShouldBeTrue();
            (await node.Input.SendAsync(second)).ShouldBeTrue();
            (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(second);
        }
        finally { release.TrySetResult(); }
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(first);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Native_scheduler_is_used_for_processing()
    {
        var schedulers = new ConcurrentExclusiveSchedulerPair();
        var observed = new TaskCompletionSource<TaskScheduler>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var node = new Action((message, _) =>
        {
            observed.TrySetResult(TaskScheduler.Current);
            return Task.FromResult(message);
        }, new ExecutionDataflowBlockOptions { TaskScheduler = schedulers.ExclusiveScheduler });
        (await node.Input.SendAsync(Message())).ShouldBeTrue();
        (await observed.Task.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(schedulers.ExclusiveScheduler);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        schedulers.Complete();
        await schedulers.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static FlowMessage Message() => FlowMessage.Create(JsonFlowValue.Null);

    private sealed class Action(Func<FlowMessage, CancellationToken, Task<FlowMessage>> process,
        ExecutionDataflowBlockOptions options) : FlowNode(options)
    {
        protected override async Task ProcessAsync(FlowMessage message) => await process(message, Stopping);
    }

    private sealed class Transform(Func<FlowMessage, CancellationToken, Task<FlowMessage>> process,
        ExecutionDataflowBlockOptions options) : FlowTransformNode(options)
    {
        protected override async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message) => [await process(message, Stopping)];
    }
}
