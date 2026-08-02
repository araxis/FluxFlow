using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowMultiOutputAndSourceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task MultiOutput_RoutesToTheRightExtraPort_PreservingCorrelation()
    {
        await using var node = new EvenOddNode();
        var even = Sink(node.Output);   // primary output = evens
        var odd = Sink(node.Odd);       // extra output = odds

        var messages = Enumerable.Range(1, 6)
            .Select(static value => FlowMessage.Create(value))
            .ToArray();
        foreach (var message in messages)
        {
            (await node.Input.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();
        }

        node.Complete();
        var evenMessages = await ReceiveAsync(even, 3);
        var oddMessages = await ReceiveAsync(odd, 3);
        await node.Completion.WaitAsync(Timeout);

        evenMessages.Select(static message => message.Value).ShouldBe([2, 4, 6]);
        oddMessages.Select(static message => message.Value).ShouldBe([1, 3, 5]);
        evenMessages.Select(static message => message.CorrelationId)
            .ShouldBe(messages.Where(static message => message.Value % 2 == 0)
                .Select(static message => message.CorrelationId));
        oddMessages.Select(static message => message.CorrelationId)
            .ShouldBe(messages.Where(static message => message.Value % 2 != 0)
                .Select(static message => message.CorrelationId));
    }

    [Fact]
    public async Task MultiOutput_ExtraPortCompletesWithTheNode()
    {
        var node = new EvenOddNode();
        var odd = Sink(node.Odd);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        await odd.Completion.WaitAsync(TimeSpan.FromSeconds(30)); // propagated completion
    }

    [Fact]
    public async Task Source_ProducesItems_ThenCompletes()
    {
        await using var source = new CountingSource(3);
        var sink = Sink(source.Output);

        await source.StartAsync();
        await source.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var items = Drain(sink);
        items.Select(m => m.Value).ShouldBe([0, 1, 2]);
    }

    [Fact]
    public async Task Source_StopsWhenCompleted()
    {
        await using var source = new CountingSource(int.MaxValue); // would run forever
        var sink = Sink(source.Output);

        await source.StartAsync();
        await sink.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)); // at least one produced
        source.Complete();                                            // signal stop

        await source.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        source.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Source_PreCanceledStartDoesNotConsumeStartState()
    {
        await using var source = new CountingSource(1);
        var sink = Sink(source.Output);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => source.StartAsync(cancellation.Token));

        await source.StartAsync();
        await source.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        sink.TryReceive(out var message).ShouldBeTrue();
        message.Value.ShouldBe(0);
    }

    [Fact]
    public async Task Source_EmitAsync_delivers_every_item_in_order_through_bounded_output()
    {
        await using var source = new BackpressuredSource();
        var target = new PostponedTargetBlock<FlowMessage<int>>();
        using var link = source.Output.LinkTo(
            target,
            new DataflowLinkOptions { PropagateCompletion = true });

        await source.StartAsync();
        await target.WaitForOfferAsync(Timeout);
        await source.ThirdEmissionStarted.Task.WaitAsync(Timeout);
        source.Completion.IsCompleted.ShouldBeFalse();

        target.AcceptNext();
        await target.WaitForOfferAsync(Timeout);
        target.AcceptNext();
        await target.WaitForOfferAsync(Timeout);
        target.AcceptNext();

        await source.Completion.WaitAsync(Timeout);
        await target.Completion.WaitAsync(Timeout);
        target.Accepted.Select(static message => message.Value).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task Source_without_subscribers_completes_without_replay()
    {
        await using var source = new CountingSource(3);

        await source.StartAsync();
        await source.Completion.WaitAsync(Timeout);

        var lateTarget = Sink(source.Output);
        await lateTarget.Completion.WaitAsync(Timeout);
        lateTarget.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Extra_output_target_rejection_faults_node_and_sibling_outputs()
    {
        await using var node = new EvenOddNode();
        var rejectingTarget = new RejectingTargetBlock<FlowMessage<int>>();
        var primaryTarget = Sink(node.Output);
        var eventTarget = Sink(node.Events);
        using var link = node.Odd.LinkTo(
            rejectingTarget,
            new DataflowLinkOptions { PropagateCompletion = true });

        (await node.Input.SendAsync(FlowMessage.Create(1)).WaitAsync(Timeout)).ShouldBeTrue();

        var outputFailure = await Should.ThrowAsync<InvalidOperationException>(
            () => node.Odd.Completion.WaitAsync(Timeout));
        var ownerFailure = await Should.ThrowAsync<InvalidOperationException>(
            () => node.Completion.WaitAsync(Timeout));
        var primaryFailure = await Should.ThrowAsync<InvalidOperationException>(
            () => primaryTarget.Completion.WaitAsync(Timeout));
        var targetFailure = await Should.ThrowAsync<InvalidOperationException>(
            () => rejectingTarget.Completion.WaitAsync(Timeout));

        ownerFailure.ShouldBeSameAs(outputFailure);
        primaryFailure.ShouldBeSameAs(outputFailure);
        targetFailure.ShouldBeSameAs(outputFailure);
        (await node.Input.SendAsync(FlowMessage.Create(2)).WaitAsync(Timeout)).ShouldBeFalse();
        await eventTarget.Completion.WaitAsync(Timeout);
        eventTarget.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Output_target_rejection_faults_source_stops_production_and_completes_events()
    {
        await using var source = new EventingSource();
        var rejectingTarget = new RejectingTargetBlock<FlowMessage<int>>();
        var eventTarget = Sink(source.Events);
        using var link = source.Output.LinkTo(
            rejectingTarget,
            new DataflowLinkOptions { PropagateCompletion = true });

        await source.StartAsync();
        var @event = await eventTarget.ReceiveAsync().WaitAsync(Timeout);

        var outputFailure = await Should.ThrowAsync<InvalidOperationException>(
            () => source.Output.Completion.WaitAsync(Timeout));
        var ownerFailure = await Should.ThrowAsync<InvalidOperationException>(
            () => source.Completion.WaitAsync(Timeout));
        var targetFailure = await Should.ThrowAsync<InvalidOperationException>(
            () => rejectingTarget.Completion.WaitAsync(Timeout));
        await source.Stopped.Task.WaitAsync(Timeout);

        @event.Name.ShouldBe("source.started");
        ownerFailure.ShouldBeSameAs(outputFailure);
        targetFailure.ShouldBeSameAs(outputFailure);
        await eventTarget.Completion.WaitAsync(Timeout);
        eventTarget.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Source_complete_cancels_blocked_emit_and_drains_accepted_output()
    {
        await using var source = new StoppableBackpressuredSource();
        var target = new PostponedTargetBlock<FlowMessage<int>>();
        using var link = source.Output.LinkTo(
            target,
            new DataflowLinkOptions { PropagateCompletion = true });

        await source.StartAsync();
        await target.WaitForOfferAsync(Timeout);
        await source.ThirdEmissionStarted.Task.WaitAsync(Timeout);
        source.ThirdEmissionExited.Task.IsCompleted.ShouldBeFalse();

        source.Complete();
        await source.ThirdEmissionExited.Task.WaitAsync(Timeout);
        source.Completion.IsCompleted.ShouldBeFalse();
        target.AcceptNext();
        await target.WaitForOfferAsync(Timeout);
        target.AcceptNext();

        await source.Completion.WaitAsync(Timeout);
        await target.Completion.WaitAsync(Timeout);
        source.Completion.IsFaulted.ShouldBeFalse();
        target.Accepted.Select(static message => message.Value).ShouldBe([1, 2]);
    }

    [Fact]
    public async Task Source_fault_preserves_original_exception_on_output_and_completion()
    {
        await using var source = new StoppableBackpressuredSource();
        var target = new PostponedTargetBlock<FlowMessage<int>>();
        using var link = source.Output.LinkTo(
            target,
            new DataflowLinkOptions { PropagateCompletion = true });
        var expected = new InvalidOperationException("distinctive source failure");

        await source.StartAsync();
        await target.WaitForOfferAsync(Timeout);
        await source.ThirdEmissionStarted.Task.WaitAsync(Timeout);

        source.Fault(expected);

        var outputFailure = await Should.ThrowAsync<InvalidOperationException>(
            () => source.Output.Completion.WaitAsync(Timeout));
        var ownerFailure = await Should.ThrowAsync<InvalidOperationException>(
            () => source.Completion.WaitAsync(Timeout));
        var targetFailure = await Should.ThrowAsync<InvalidOperationException>(
            () => target.Completion.WaitAsync(Timeout));
        outputFailure.ShouldBeSameAs(expected);
        ownerFailure.ShouldBeSameAs(expected);
        targetFailure.ShouldBeSameAs(expected);
    }

    [Fact]
    public async Task Source_extra_output_drains_before_owner_completion()
    {
        await using var source = new ExtraOutputSource();
        var target = new PostponedTargetBlock<int>();
        using var link = source.Extra.LinkTo(
            target,
            new DataflowLinkOptions { PropagateCompletion = true });

        await source.StartAsync();
        await target.WaitForOfferAsync(Timeout);
        source.Completion.IsCompleted.ShouldBeFalse();

        target.AcceptNext();
        await target.WaitForOfferAsync(Timeout);
        source.Completion.IsCompleted.ShouldBeFalse();
        target.AcceptNext();

        await source.Completion.WaitAsync(Timeout);
        await target.Completion.WaitAsync(Timeout);
        target.Accepted.ShouldBe([1, 2]);
    }

    [Fact]
    public void Source_RejectsInvalidOutputCapacity()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new InvalidCapacitySource());

        exception.Message.ShouldContain("OutputCapacity");
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    private static List<T> Drain<T>(BufferBlock<T> sink)
    {
        var items = new List<T>();
        while (sink.TryReceive(out var item))
        {
            items.Add(item);
        }

        return items;
    }

    private static async Task<IReadOnlyList<T>> ReceiveAsync<T>(BufferBlock<T> sink, int count)
    {
        var items = new List<T>(count);
        for (var index = 0; index < count; index++)
        {
            items.Add(await sink.ReceiveAsync().WaitAsync(Timeout));
        }

        return items;
    }

    // 1 input, 2 domain outputs: evens on the primary Output, odds on an extra port.
    private sealed class EvenOddNode : FlowNode<int, int>
    {
        private readonly FlowOutput<FlowMessage<int>> _odd;

        public EvenOddNode() => _odd = AddOutput<FlowMessage<int>>();

        public ISourceBlock<FlowMessage<int>> Odd => _odd;

        protected override async Task ProcessAsync(FlowMessage<int> message)
        {
            if (message.Value % 2 == 0)
            {
                await EmitAsync(message, Stopping).ConfigureAwait(false);
            }
            else
            {
                await EmitAsync(_odd, message, Stopping).ConfigureAwait(false);
            }
        }
    }

    // A source that emits 0..count-1 then completes (or runs until stopped).
    private sealed class CountingSource(int count) : FlowSource<int>
    {
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            for (var i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EmitAsync(FlowMessage.Create(i), cancellationToken).ConfigureAwait(false);
                if (count == int.MaxValue)
                {
                    await Task.Delay(5, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private sealed class BackpressuredSource()
        : FlowSource<int>(new FlowSourceOptions { OutputCapacity = 1 })
    {
        public TaskCompletionSource ThirdEmissionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            for (var value = 1; value <= 3; value++)
            {
                if (value == 3)
                {
                    ThirdEmissionStarted.TrySetResult();
                }

                await EmitAsync(FlowMessage.Create(value), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class EventingSource : FlowSource<int>
    {
        public TaskCompletionSource Stopped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                EmitEvent(new FlowEvent { Name = "source.started" });
                await EmitAsync(FlowMessage.Create(1), cancellationToken).ConfigureAwait(false);
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                Stopped.TrySetResult();
            }
        }
    }

    private sealed class StoppableBackpressuredSource()
        : FlowSource<int>(new FlowSourceOptions { OutputCapacity = 1 })
    {
        public TaskCompletionSource ThirdEmissionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ThirdEmissionExited { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            await EmitAsync(FlowMessage.Create(1), cancellationToken).ConfigureAwait(false);
            await EmitAsync(FlowMessage.Create(2), cancellationToken).ConfigureAwait(false);
            try
            {
                var thirdEmission = EmitAsync(FlowMessage.Create(3), cancellationToken);
                ThirdEmissionStarted.TrySetResult();
                await thirdEmission.ConfigureAwait(false);
            }
            finally
            {
                ThirdEmissionExited.TrySetResult();
            }
        }
    }

    private sealed class ExtraOutputSource : FlowSource<int>
    {
        private readonly FlowOutput<int> _extra;

        public ExtraOutputSource() => _extra = AddOutput<int>();

        public ISourceBlock<int> Extra => _extra;

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            await EmitAsync(_extra, 1, cancellationToken).ConfigureAwait(false);
            await EmitAsync(_extra, 2, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class InvalidCapacitySource()
        : FlowSource<int>(new FlowSourceOptions { OutputCapacity = 0 })
    {
        protected override Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
