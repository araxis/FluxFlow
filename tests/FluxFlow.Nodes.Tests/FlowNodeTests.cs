using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Processes_AndPreservesCorrelation()
    {
        await using var node = new DoubleNode();
        var output = Sink(node.Output);

        var message = FlowMessage.Create(21);
        await node.Input.SendAsync(message);

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.Value.ShouldBe(42);
        result.CorrelationId.ShouldBe(message.CorrelationId);
    }

    [Fact]
    public async Task Output_delivers_every_item_to_every_consumer_in_order()
    {
        await using var node = new DoubleNode();
        var first = Sink(node.Output);
        var second = Sink(node.Output);

        foreach (var value in Enumerable.Range(1, 5))
        {
            (await node.Input.SendAsync(FlowMessage.Create(value)).WaitAsync(Timeout))
                .ShouldBeTrue();
        }

        node.Complete();
        var firstValues = await ReceiveAsync(first, 5);
        var secondValues = await ReceiveAsync(second, 5);
        await node.Completion.WaitAsync(Timeout);

        firstValues.Select(static message => message.Value).ShouldBe([2, 4, 6, 8, 10]);
        secondValues.Select(static message => message.Value).ShouldBe([2, 4, 6, 8, 10]);
    }

    [Fact]
    public async Task HandlerThrow_EmitsFlowErrorMessageWithoutFaulting()
    {
        await using var node = new BoomNode();
        var output = Sink(node.Output);

        var message = FlowMessage.Create(1);
        await node.Input.SendAsync(message);

        var error = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.IsError.ShouldBeTrue();
        error.CorrelationId.ShouldBe(message.CorrelationId);
        error.TraceId.ShouldBe(message.TraceId);
        error.CausationId.ShouldBe(message.MessageId);
        error.Error!.Code.ShouldBe("node.processing_failed");
        error.Error.Message.ShouldBe("boom");

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task IncomingError_IsPropagatedWithoutInvokingBusinessOperation()
    {
        await using var node = new DoubleNode();
        var output = Sink(node.Output);
        var error = new FlowError("input.invalid", "Invalid input.", "validation");

        await node.Input.SendAsync(FlowMessage.CreateError<int>(error));

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.IsError.ShouldBeTrue();
        result.Error.ShouldBeSameAs(error);
        node.ProcessCount.ShouldBe(0);
    }

    [Fact]
    public async Task InputCapacity_AppliesBackpressureToTheProcessingBlock()
    {
        await using var node = new BlockingNode();

        (await node.Input.SendAsync(FlowMessage.Create(1))).ShouldBeTrue();
        await node.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var secondSend = node.Input.SendAsync(FlowMessage.Create(2));
        secondSend.IsCompleted.ShouldBeFalse();

        node.Release.TrySetResult();
        (await secondSend.WaitAsync(TimeSpan.FromSeconds(30))).ShouldBeTrue();
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Output_backpressure_reaches_node_input_without_dropping_messages()
    {
        await using var node = new BackpressuredNode();
        var target = new PostponedTargetBlock<FlowMessage<int>>();
        using var link = node.Output.LinkTo(
            target,
            new DataflowLinkOptions { PropagateCompletion = true });

        (await node.Input.SendAsync(FlowMessage.Create(1)).WaitAsync(Timeout)).ShouldBeTrue();
        await target.WaitForOfferAsync(Timeout);
        (await node.Input.SendAsync(FlowMessage.Create(2)).WaitAsync(Timeout)).ShouldBeTrue();
        await node.SecondEmissionCompleted.Task.WaitAsync(Timeout);
        (await node.Input.SendAsync(FlowMessage.Create(3)).WaitAsync(Timeout)).ShouldBeTrue();
        await node.ThirdEmissionStarted.Task.WaitAsync(Timeout);

        var fourthInput = node.Input.SendAsync(FlowMessage.Create(4));
        fourthInput.IsCompleted.ShouldBeFalse();

        target.AcceptNext();
        (await fourthInput.WaitAsync(Timeout)).ShouldBeTrue();
        node.Complete();

        await target.WaitForOfferAsync(Timeout);
        target.AcceptNext();
        await target.WaitForOfferAsync(Timeout);
        target.AcceptNext();
        await target.WaitForOfferAsync(Timeout);
        target.AcceptNext();

        await node.Completion.WaitAsync(Timeout);
        await target.Completion.WaitAsync(Timeout);
        target.Accepted.Select(static message => message.Value).ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public async Task Output_target_rejection_faults_node_stops_input_and_completes_events()
    {
        await using var node = new EventingNode();
        var rejecting = new RejectingTargetBlock<FlowMessage<int>>();
        var events = Sink(node.Events);
        using var outputLink = node.Output.LinkTo(
            rejecting,
            new DataflowLinkOptions { PropagateCompletion = true });

        (await node.Input.SendAsync(FlowMessage.Create(7)).WaitAsync(Timeout)).ShouldBeTrue();
        var @event = await events.ReceiveAsync().WaitAsync(Timeout);
        var outputFailure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await node.Output.Completion.WaitAsync(Timeout));
        var nodeFailure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await node.Completion.WaitAsync(Timeout));
        var targetFailure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await rejecting.Completion.WaitAsync(Timeout));

        @event.Name.ShouldBe("test.output.emitting");
        nodeFailure.ShouldBeSameAs(outputFailure);
        targetFailure.ShouldBeSameAs(outputFailure);
        node.IsStopping.ShouldBeTrue();
        (await node.Input.SendAsync(FlowMessage.Create(8)).WaitAsync(Timeout)).ShouldBeFalse();
        await events.Completion.WaitAsync(Timeout);
        events.Completion.IsFaulted.ShouldBeFalse();
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    private static async Task<List<T>> ReceiveAsync<T>(BufferBlock<T> target, int count)
    {
        var values = new List<T>(count);
        for (var index = 0; index < count; index++)
        {
            values.Add(await target.ReceiveAsync().WaitAsync(Timeout));
        }

        return values;
    }

    private sealed class DoubleNode : FlowNode<int, int>
    {
        public int ProcessCount { get; private set; }

        protected override async Task ProcessAsync(FlowMessage<int> message)
        {
            ProcessCount++;
            await EmitAsync(message.With(message.Value * 2), Stopping);
        }
    }

    private sealed class BoomNode : FlowNode<int, int>
    {
        protected override Task ProcessAsync(FlowMessage<int> message)
            => throw new InvalidOperationException("boom");
    }

    private sealed class BlockingNode()
        : FlowNode<int, int>(new FlowNodeOptions { InputCapacity = 1 })
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task ProcessAsync(FlowMessage<int> message)
        {
            Started.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            await EmitAsync(message, Stopping).ConfigureAwait(false);
        }
    }

    private sealed class BackpressuredNode()
        : FlowNode<int, int>(new FlowNodeOptions
        {
            InputCapacity = 1,
            OutputCapacity = 1
        })
    {
        private int _emissionCount;

        public TaskCompletionSource SecondEmissionCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ThirdEmissionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task ProcessAsync(FlowMessage<int> message)
        {
            var emission = Interlocked.Increment(ref _emissionCount);
            if (emission == 3)
            {
                ThirdEmissionStarted.TrySetResult();
            }

            await EmitAsync(message, Stopping).ConfigureAwait(false);
            if (emission == 2)
            {
                SecondEmissionCompleted.TrySetResult();
            }
        }
    }

    private sealed class EventingNode : FlowNode<int, int>
    {
        public bool IsStopping => Stopping.IsCancellationRequested;

        protected override async Task ProcessAsync(FlowMessage<int> message)
        {
            EmitEvent(new FlowEvent
            {
                Name = "test.output.emitting",
                Message = "Emitting normal data."
            });
            await EmitAsync(message, Stopping).ConfigureAwait(false);
        }
    }
}
