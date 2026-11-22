using System.Text.Json;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
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
        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(21, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        await node.Input.SendAsync(message);
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.Value.ShouldNotBeNull().Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(42);
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
            (await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).WaitAsync(Timeout)).ShouldBeTrue();
        }

        node.Complete();
        var firstValues = await ReceiveAsync(first, 5);
        var secondValues = await ReceiveAsync(second, 5);
        await node.Completion.WaitAsync(Timeout);
        firstValues.Select(static message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([2, 4, 6, 8, 10]);
        secondValues.Select(static message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([2, 4, 6, 8, 10]);
        for (var index = 0; index < firstValues.Count; index++)
            secondValues[index].ShouldBeSameAs(firstValues[index]);
    }

    [Fact]
    public async Task HandlerThrow_EmitsFlowErrorMessageWithoutFaulting()
    {
        await using var node = new BoomNode();
        var output = Sink(node.Output);
        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
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
        var incoming = FlowMessage.Create(error);
        await node.Input.SendAsync(incoming);
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.IsError.ShouldBeTrue();
        result.Error.ShouldBeSameAs(error);
        result.ShouldBeSameAs(incoming);
        node.ProcessCount.ShouldBe(0);
    }

    [Fact]
    public async Task InputCapacity_AppliesBackpressureToTheProcessingBlock()
    {
        await using var node = new BlockingNode();
        Task<bool>? secondSend = null;
        try
        {
            (await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
            await node.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            secondSend = node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(2, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
            secondSend.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            node.Release.TrySetResult();
        }

        (await secondSend!.WaitAsync(TimeSpan.FromSeconds(30))).ShouldBeTrue();
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
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

    private sealed class DoubleNode : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public DoubleNode()
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
            Lifetime.Observe();
        }

        public int ProcessCount { get; private set; }

        private async Task ProcessAsync(FlowMessage message)
        {
            ProcessCount++;
            await EmitAsync(message.WithValue((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(message.Value!.Json.Deserialize<int>() * 2, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), Stopping);
        }
    }

    private sealed class BoomNode : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public BoomNode()
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
            Lifetime.Observe();
        }

        private Task ProcessAsync(FlowMessage message) => throw new InvalidOperationException("boom");
    }

    private sealed class BlockingNode : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public BlockingNode()
        {
            var execution = new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = 1
            };
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
            Lifetime.Observe();
        }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private async Task ProcessAsync(FlowMessage message)
        {
            Started.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            await EmitAsync(message, Stopping).ConfigureAwait(false);
        }
    }

    private sealed class BackpressuredNode : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public BackpressuredNode()
        {
            var execution = new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = 1
            };
            var outputCapacityValue = 1;
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
            Lifetime.Observe();
        }

        private int _emissionCount;
        public TaskCompletionSource SecondEmissionCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ThirdEmissionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private async Task ProcessAsync(FlowMessage message)
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

    private sealed class EventingNode : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public EventingNode()
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
            Lifetime.Observe();
        }

        public bool IsStopping => Stopping.IsCancellationRequested;

        private async Task ProcessAsync(FlowMessage message)
        {
            EmitEvent(new FlowEvent { Name = "test.output.emitting", Message = "Emitting normal data." });
            await EmitAsync(message, Stopping).ConfigureAwait(false);
        }
    }
}
