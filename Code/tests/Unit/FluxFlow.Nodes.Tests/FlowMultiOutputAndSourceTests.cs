using System.Text.Json;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
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
        var even = Sink(node.Output); // primary output = evens
        var odd = Sink(node.Odd); // extra output = odds
        var messages = Enumerable.Range(1, 6).Select(static value => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToArray();
        foreach (var message in messages)
        {
            (await node.Input.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();
        }

        node.Complete();
        var evenMessages = await ReceiveAsync(even, 3);
        var oddMessages = await ReceiveAsync(odd, 3);
        await node.Completion.WaitAsync(Timeout);
        evenMessages.Select(static message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([2, 4, 6]);
        oddMessages.Select(static message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([1, 3, 5]);
        evenMessages.Select(static message => message.CorrelationId).ShouldBe(messages.Where(static message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) % 2 == 0).Select(static message => message.CorrelationId));
        oddMessages.Select(static message => message.CorrelationId).ShouldBe(messages.Where(static message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) % 2 != 0).Select(static message => message.CorrelationId));
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
        items.Select(m => m.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([0, 1, 2]);
    }

    [Fact]
    public async Task Source_StopsWhenCompleted()
    {
        await using var source = new CountingSource(int.MaxValue); // would run forever
        var sink = Sink(source.Output);
        await source.StartAsync();
        await sink.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)); // at least one produced
        source.Complete(); // signal stop
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
        await Should.ThrowAsync<OperationCanceledException>(() => source.StartAsync(cancellation.Token));
        await source.StartAsync();
        await source.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        sink.TryReceive(out var message).ShouldBeTrue();
        message.Value.ShouldNotBeNull().Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(0);
    }

    [Fact]
    public async Task Source_without_subscribers_completes_and_retains_latest_value()
    {
        await using var source = new CountingSource(3);
        await source.StartAsync();
        await source.Completion.WaitAsync(Timeout);
        var lateTarget = Sink(source.Output);
        (await lateTarget.ReceiveAsync(Timeout)).Value.Json.GetInt32().ShouldBe(2);
        await lateTarget.Completion.WaitAsync(Timeout);
        lateTarget.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public void Source_RejectsInvalidOutputCapacity()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() => new InvalidCapacitySource());
        exception.ParamName.ShouldBe("outputCapacity");
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
    private sealed class EvenOddNode : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        private readonly BroadcastBlock<FlowMessage> _odd;
        public EvenOddNode()
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
            _odd = lifetime.HasOutput(new BroadcastBlock<FlowMessage>(static message => message, new DataflowBlockOptions { BoundedCapacity = 128 }));
            Lifetime.Observe();
        }

        public ISourceBlock<FlowMessage> Odd => _odd;

        private async Task ProcessAsync(FlowMessage message)
        {
            if (message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) % 2 == 0)
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
    private sealed class CountingSource : FlowNode, IFlowSource
    {
        public Task ProductionCompletion => Lifetime.ProductionCompletion;

        public void StopProduction() => Lifetime.StopProduction();
        public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
        public CountingSource(int count)
        {
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, produce: RunAsync);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            this.count = count;
            Lifetime.Observe();
        }

        private readonly int count;
        private async Task RunAsync(CancellationToken cancellationToken)
        {
            for (var i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EmitAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(i, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken).ConfigureAwait(false);
                if (count == int.MaxValue)
                {
                    await Task.Delay(5, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private sealed class BackpressuredSource : FlowNode, IFlowSource
    {
        public Task ProductionCompletion => Lifetime.ProductionCompletion;

        public void StopProduction() => Lifetime.StopProduction();
        public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
        public BackpressuredSource()
        {
            var outputCapacityValue = 1;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, produce: RunAsync);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        public TaskCompletionSource ThirdEmissionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            for (var value = 1; value <= 3; value++)
            {
                if (value == 3)
                {
                    ThirdEmissionStarted.TrySetResult();
                }

                await EmitAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class EventingSource : FlowNode, IFlowSource
    {
        public Task ProductionCompletion => Lifetime.ProductionCompletion;

        public void StopProduction() => Lifetime.StopProduction();
        public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
        public EventingSource()
        {
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, produce: RunAsync);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                EmitEvent(new FlowEvent { Name = "source.started" });
                await EmitAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken).ConfigureAwait(false);
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Stopped.TrySetResult();
            }
        }
    }

    private sealed class StoppableBackpressuredSource : FlowNode, IFlowSource
    {
        public Task ProductionCompletion => Lifetime.ProductionCompletion;

        public void StopProduction() => Lifetime.StopProduction();
        public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
        public StoppableBackpressuredSource()
        {
            var outputCapacityValue = 1;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, produce: RunAsync);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        public TaskCompletionSource ThirdEmissionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ThirdEmissionExited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            await EmitAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken).ConfigureAwait(false);
            await EmitAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(2, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken).ConfigureAwait(false);
            try
            {
                var thirdEmission = EmitAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(3, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken);
                ThirdEmissionStarted.TrySetResult();
                await thirdEmission.ConfigureAwait(false);
            }
            finally
            {
                ThirdEmissionExited.TrySetResult();
            }
        }
    }

    private sealed class ExtraOutputSource : FlowNode, IFlowSource
    {
        public Task ProductionCompletion => Lifetime.ProductionCompletion;

        public void StopProduction() => Lifetime.StopProduction();
        public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
        private readonly BroadcastBlock<FlowMessage> _extra;
        public ExtraOutputSource()
        {
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, produce: RunAsync);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            var lifetime = Lifetime;
            _extra = lifetime.HasOutput(new BroadcastBlock<FlowMessage>(static message => message, new DataflowBlockOptions { BoundedCapacity = 128 }));
            Lifetime.Observe();
        }

        public ISourceBlock<FlowMessage> Extra => _extra;

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            await EmitAsync(_extra, FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken).ConfigureAwait(false);
            await EmitAsync(_extra, FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(2, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class InvalidCapacitySource : FlowNode, IFlowSource
    {
        public Task ProductionCompletion => Lifetime.ProductionCompletion;

        public void StopProduction() => Lifetime.StopProduction();
        public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
        public InvalidCapacitySource()
        {
            var outputCapacityValue = 0;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, produce: RunAsync);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        private Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
