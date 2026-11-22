using FluxFlow.Core.Nodes;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;
public sealed class ProcessingLifetimeTests
{
    private static ITargetBlock<FlowMessage> InputOf(IFlowNode node) => node is ActionProbe action ? action.Input : ((TransformProbe)node).Input;
    private static ISourceBlock<FlowMessage> OutputOf(IFlowNode node) => ((FlowNode)node).Output;
    private static ISourceBlock<FlowMessage> EventsOf(IFlowNode node) => ((FlowNode)node).Events;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Transform_input_capacity_and_cancelled_admission_are_native(int capacity)
    {
        var entered = Gate();
        var release = Gate();
        var calls = 0;
        await using var node = new TransformProbe(async (message, _) =>
        {
            if (Interlocked.Increment(ref calls) == capacity)
                entered.TrySetResult();
            await release.Task;
            return message.WithValue((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(message.Value!.Json.Deserialize<int>() * 10, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        }, new ExecutionDataflowBlockOptions { BoundedCapacity = capacity, MaxDegreeOfParallelism = capacity });
        var output = Sink(node.Output);
        Task<bool>? pending = null;
        try
        {
            for (var index = 1; index <= capacity; index++)
                (await node.Input.SendAsync(Message(index))).ShouldBeTrue();
            await entered.Task.WaitAsync(Timeout);
            using var cancellation = new CancellationTokenSource();
            var cancelled = node.Input.SendAsync(Message(99), cancellation.Token);
            cancelled.IsCompleted.ShouldBeFalse();
            cancellation.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(async () => await cancelled.WaitAsync(Timeout));
            pending = node.Input.SendAsync(Message(capacity + 1));
            pending.IsCompleted.ShouldBeFalse();
            Volatile.Read(ref calls).ShouldBe(capacity);
        }
        finally
        {
            release.TrySetResult();
        }

        (await pending!.WaitAsync(Timeout)).ShouldBeTrue();
        node.Complete();
        var results = new List<int>();
        for (var index = 0; index <= capacity; index++)
            results.Add((await output.ReceiveAsync().WaitAsync(Timeout)).Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await node.Completion.WaitAsync(Timeout);
        results.ShouldBe(Enumerable.Range(1, capacity + 1).Select(value => value * 10));
        calls.ShouldBe(capacity + 1);
    }

    [Fact]
    public async Task Explicit_transform_fault_cancels_work_before_joining_it()
    {
        var entered = Gate();
        var cancelled = Gate();
        var release = Gate();
        var exited = Gate();
        var failure = new InvalidOperationException("unexpected processing failure");
        await using var node = new TransformProbe(async (message, token) =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            entered.TrySetResult();
            try
            {
                await release.Task;
            }
            finally
            {
                exited.TrySetResult();
            }

            return message;
        }, new ExecutionDataflowBlockOptions { BoundedCapacity = 2, MaxDegreeOfParallelism = 2 });
        var output = Sink(node.Output);
        Task? disposal = null;
        try
        {
            (await node.Input.SendAsync(Message(1))).ShouldBeTrue();
            await entered.Task.WaitAsync(Timeout);
            node.Fault(failure);
            await cancelled.Task.WaitAsync(Timeout);
            node.Completion.IsCompleted.ShouldBeFalse();
            disposal = node.DisposeAsync().AsTask();
            disposal.IsCompleted.ShouldBeFalse();
            node.DisposeCount.ShouldBe(0);
            exited.Task.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            release.TrySetResult();
        }

        (await Should.ThrowAsync<InvalidOperationException>(() => node.Completion.WaitAsync(Timeout))).ShouldBeSameAs(failure);
        await disposal!.WaitAsync(Timeout);
        exited.Task.IsCompletedSuccessfully.ShouldBeTrue();
        node.DisposeCount.ShouldBe(1);
        output.Count.ShouldBe(0);
        (await Should.ThrowAsync<AggregateException>(() => output.Completion.WaitAsync(Timeout))).Flatten().InnerExceptions.ShouldContain(failure);
        await node.Events.Completion.WaitAsync(Timeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Processing_failure_preserves_peer_and_subsequent_messages(bool transform)
    {
        var entered = Gate();
        var failed = Gate();
        var release = Gate();
        CancellationToken peerToken = default;
        async Task<FlowMessage> Process(FlowMessage message, CancellationToken token)
        {
            var value = message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (value == 2)
            {
                failed.TrySetResult();
                throw new InvalidOperationException("invalid item");
            }

            if (value == 1)
            {
                peerToken = token;
                entered.TrySetResult();
                await release.Task;
            }

            return message.WithValue((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value * 10, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        }

        await using IFlowNode node = transform ? new TransformProbe(Process, new() { MaxDegreeOfParallelism = 2 }) : new ActionProbe(Process, () =>
        {
        }, new() { MaxDegreeOfParallelism = 2 });
        var output = Sink(OutputOf(node));
        var input = InputOf(node);
        var rejected = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(2, new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());
        try
        {
            (await input.SendAsync(Message(1)).WaitAsync(Timeout)).ShouldBeTrue();
            await entered.Task.WaitAsync(Timeout);
            (await input.SendAsync(rejected).WaitAsync(Timeout)).ShouldBeTrue();
            await failed.Task.WaitAsync(Timeout);
        }
        finally
        {
            release.TrySetResult();
        }

        var first = await output.ReceiveAsync(Timeout);
        var second = await output.ReceiveAsync(Timeout);
        var error = new[]
        {
            first,
            second
        }.Single(message => message.IsError);
        error.Error!.Code.ShouldBe("node.processing_failed");
        error.TraceId.ShouldBe(rejected.TraceId);
        error.CorrelationId.ShouldBe(rejected.CorrelationId);
        error.CausationId.ShouldBe(rejected.MessageId);
        new[]
        {
            first,
            second
        }.Single(message => !message.IsError).Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(10);
        peerToken.IsCancellationRequested.ShouldBeFalse();
        (await input.SendAsync(Message(3)).WaitAsync(Timeout)).ShouldBeTrue();
        (await output.ReceiveAsync(Timeout)).Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(30);
        node.Complete();
        await node.Completion.WaitAsync(Timeout);
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Processing_disposal_joins_work_before_releasing_resources(bool transform)
    {
        var entered = Gate();
        var release = Gate();
        var disposed = 0;
        CancellationToken stopping = default;
        async Task<FlowMessage> Process(FlowMessage message, CancellationToken token)
        {
            stopping = token;
            entered.TrySetResult();
            await release.Task;
            return message.WithValue((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        }

        await using IFlowNode node = transform ? new TransformProbe(Process, release: () => disposed++) : new ActionProbe(Process, () => disposed++);
        var output = Sink(OutputOf(node));
        var input = InputOf(node);
        Task? disposal = null;
        try
        {
            (await input.SendAsync(Message(1))).ShouldBeTrue();
            await entered.Task.WaitAsync(Timeout);
            disposal = node.DisposeAsync().AsTask();
            disposal.IsCompleted.ShouldBeFalse();
            stopping.IsCancellationRequested.ShouldBeFalse();
            disposed.ShouldBe(0);
            (await input.SendAsync(Message(2))).ShouldBeFalse();
        }
        finally
        {
            release.TrySetResult();
        }

        (await output.ReceiveAsync().WaitAsync(Timeout)).Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(42);
        await disposal!.WaitAsync(Timeout);
        await node.DisposeAsync();
        disposed.ShouldBe(1);
        stopping.IsCancellationRequested.ShouldBeTrue();
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        await output.Completion.WaitAsync(Timeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Throwing_cancellation_callback_cannot_strand_processing_shutdown(bool transform)
    {
        var entered = Gate();
        var release = Gate();
        var failure = new InvalidOperationException("processing stopped");
        var cancellationFailure = new InvalidOperationException("callback failed");
        var disposed = 0;
        async Task<FlowMessage> Process(FlowMessage message, CancellationToken token)
        {
            using var registration = token.Register(() => throw cancellationFailure);
            entered.TrySetResult();
            await release.Task;
            return message;
        }

        await using IFlowNode node = transform ? new TransformProbe(Process, release: () => disposed++) : new ActionProbe(Process, () => disposed++);
        try
        {
            (await InputOf(node).SendAsync(Message(1))).ShouldBeTrue();
            await entered.Task.WaitAsync(Timeout);
            node.Fault(failure);
            node.Completion.IsCompleted.ShouldBeFalse();
            disposed.ShouldBe(0);
        }
        finally
        {
            release.TrySetResult();
        }

        var actual = await Should.ThrowAsync<AggregateException>(() => node.Completion.WaitAsync(Timeout));
        var failures = actual.Flatten().InnerExceptions;
        failures.Count.ShouldBe(2);
        failures[0].ShouldBeSameAs(failure);
        failures[1].ShouldBeSameAs(cancellationFailure);
        await node.DisposeAsync().AsTask().WaitAsync(Timeout);
        disposed.ShouldBe(1);
        await EventsOf(node).Completion.WaitAsync(Timeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_lifetime_preserves_flush_and_extra_output_ownership(bool fault)
    {
        await using var node = new FlushProbe();
        var normal = Sink(node.Output);
        var extra = Sink(node.Extra);
        var input = Message(7);
        (await node.Input.SendAsync(input)).ShouldBeTrue();
        await node.Received.Task.WaitAsync(Timeout);
        var failure = new InvalidOperationException("stop without flushing");
        if (fault)
            node.Fault(failure);
        else
            node.Complete();
        if (fault)
        {
            (await Should.ThrowAsync<InvalidOperationException>(() => node.Completion.WaitAsync(Timeout))).ShouldBeSameAs(failure);
            (await Should.ThrowAsync<AggregateException>(() => extra.Completion.WaitAsync(Timeout))).Flatten().InnerExceptions.ShouldContain(failure);
            normal.Count.ShouldBe(0);
            extra.Count.ShouldBe(0);
            node.FlushCount.ShouldBe(0);
        }
        else
        {
            (await normal.ReceiveAsync().WaitAsync(Timeout)).ShouldBeSameAs(input);
            (await extra.ReceiveAsync().WaitAsync(Timeout)).ShouldBeSameAs(input);
            await node.Completion.WaitAsync(Timeout);
            await Task.WhenAll(normal.Completion, extra.Completion).WaitAsync(Timeout);
            node.FlushCount.ShouldBe(1);
        }

        await node.DisposeAsync();
        await node.DisposeAsync();
        node.DisposeCount.ShouldBe(1);
        (await node.Extra.SendAsync(Message(8))).ShouldBeFalse();
    }

    private sealed class TransformProbe : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public TransformProbe(Func<FlowMessage, CancellationToken, Task<FlowMessage>> process, ExecutionDataflowBlockOptions? options = null, Action? release = null, int outputCapacity = 128)
        {
            var execution = options ?? new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = outputCapacity;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            this.process = process;
            this.release = release;
            Lifetime.Observe();
        }

        private readonly Action? release;
        private readonly Func<FlowMessage, CancellationToken, Task<FlowMessage>> process;
        public int DisposeCount { get; private set; }

        private async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message) => [await process(message, Stopping)];
        protected override ValueTask OnDisposeAsync()
        {
            DisposeCount++;
            release?.Invoke();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ActionProbe : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public ActionProbe(Func<FlowMessage, CancellationToken, Task<FlowMessage>> process, Action release, ExecutionDataflowBlockOptions? options = null)
        {
            var execution = options ?? new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
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
            this.process = process;
            this.release = release;
            Lifetime.Observe();
        }

        private readonly Action release;
        private readonly Func<FlowMessage, CancellationToken, Task<FlowMessage>> process;
        private async Task ProcessAsync(FlowMessage message) => await EmitAsync(await process(message, Stopping), Stopping);
        protected override ValueTask OnDisposeAsync()
        {
            release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FlushProbe : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        private FlowMessage? _last;
        public BroadcastBlock<FlowMessage> Extra { get; }
        public TaskCompletionSource Received { get; } = Gate();
        public int FlushCount { get; private set; }
        public int DisposeCount { get; private set; }

        public FlushProbe()
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
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
            Extra = lifetime.HasOutput(new BroadcastBlock<FlowMessage>(static message => message, new DataflowBlockOptions { BoundedCapacity = 128 }));
            Lifetime.Observe();
        }

        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        {
            _last = message;
            Received.TrySetResult();
            return Task.FromResult<IEnumerable<FlowMessage>>([]);
        }

        private async ValueTask<IEnumerable<FlowMessage>> FlushAsync()
        {
            FlushCount++;
            await EmitAsync(Extra, _last!, Stopping);
            return[_last!];
        }

        protected override ValueTask OnDisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static FlowMessage Message(int value) => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }
}
