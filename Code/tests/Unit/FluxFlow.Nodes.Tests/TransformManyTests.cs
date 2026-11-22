using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;
public sealed class TransformManyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static FlowMessage Message(int value) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sequential_success_forwarded_errors_and_processing_errors_keep_input_order(bool explicitEmission)
    {
        static Task<IEnumerable<FlowMessage>> Process(FlowMessage message) => message.Value.Json.GetInt32() < 0 ? throw new InvalidOperationException("processing failed") : Task.FromResult<IEnumerable<FlowMessage>>([message]);
        await using FlowNode node = explicitEmission ? new EmittingProbe() : new Probe(Process);
        var input = node is EmittingProbe emitting ? emitting.Input : ((Probe)node).Input;
        var sink = Sink(node);
        var before = Message(1);
        var forwarded = FlowMessage.Create(new FlowError("incoming", "incoming failure", "test"));
        var failed = Message(-1);
        var after = Message(2);
        foreach (var message in new FlowMessage[]
        {
            before,
            forwarded,
            failed,
            after
        }

        )
            (await input.SendAsync(message)).ShouldBeTrue();
        node.Complete();
        (await sink.ReceiveAsync(Timeout)).ShouldBeSameAs(before);
        (await sink.ReceiveAsync(Timeout)).ShouldBeSameAs(forwarded);
        var error = await sink.ReceiveAsync(Timeout);
        error.Error!.Code.ShouldBe("node.processing_failed");
        error.CausationId.ShouldBe(failed.MessageId);
        (await sink.ReceiveAsync(Timeout)).ShouldBeSameAs(after);
        await node.Completion.WaitAsync(Timeout);
        await sink.Completion.WaitAsync(Timeout);
    }

    private sealed class EmittingProbe : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public EmittingProbe()
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
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

        private async Task ProcessAsync(FlowMessage message)
        {
            if (message.Value.Json.GetInt32() < 0)
                throw new InvalidOperationException("processing failed");
            await EmitAsync(message, Stopping);
        }
    }

    [Fact]
    public async Task Zero_one_many_preserve_identity_lineage_and_broadcast_fanout()
    {
        await using var node = new Probe(message => Task.FromResult<IEnumerable<FlowMessage>>(message.Value.Json.GetInt32() switch
        {
            0 => [],
            1 => [message],
            _ => [message.WithValue(message.Value), message.WithValue(message.Value)]
        }));
        var first = Sink(node);
        var second = Sink(node);
        var forwarded = Message(1);
        var parent = Message(2);
        foreach (var message in new[]
        {
            Message(0),
            forwarded,
            parent
        }

        )
            (await node.Input.SendAsync(message)).ShouldBeTrue();
        node.Complete();
        var results = new List<FlowMessage>();
        for (var i = 0; i < 3; i++)
        {
            var result = await first.ReceiveAsync(Timeout);
            (await second.ReceiveAsync(Timeout)).ShouldBeSameAs(result);
            results.Add(result);
        }

        results[0].ShouldBeSameAs(forwarded);
        foreach (var result in results.Skip(1))
        {
            result.CausationId.ShouldBe(parent.MessageId);
            result.TraceId.ShouldBe(parent.TraceId);
            result.Value.ShouldBeSameAs(parent.Value);
        }

        results[1].MessageId.ShouldNotBe(results[2].MessageId);
        await node.Completion.WaitAsync(Timeout);
        await Task.WhenAll(first.Completion, second.Completion).WaitAsync(Timeout);
    }

    [Fact]
    public async Task Lazy_enumeration_failure_emits_only_error_and_next_input_continues()
    {
        static IEnumerable<FlowMessage> Broken(FlowMessage message)
        {
            yield return message;
            throw new InvalidOperationException("iteration failed");
        }

        await using var node = new Probe(message => Task.FromResult(message.Value.Json.GetInt32() == 1 ? Broken(message) : new[] { message }));
        var sink = Sink(node);
        var next = Message(2);
        await node.Input.SendAsync(Message(1));
        await node.Input.SendAsync(next);
        node.Complete();
        (await sink.ReceiveAsync(Timeout)).Error!.Code.ShouldBe("node.processing_failed");
        (await sink.ReceiveAsync(Timeout)).ShouldBeSameAs(next);
        await node.Completion.WaitAsync(Timeout);
        await sink.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Parallel_batches_keep_input_order()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var node = new Probe(async message =>
        {
            if (message.Value.Json.GetInt32() == 1)
                await release.Task;
            else
                secondEntered.SetResult();
            return new[]
            {
                message,
                message
            };
        }, new() { MaxDegreeOfParallelism = 2 });
        var sink = Sink(node);
        var first = Message(1);
        var second = Message(2);
        try
        {
            await node.Input.SendAsync(first);
            await node.Input.SendAsync(second);
            await secondEntered.Task.WaitAsync(Timeout);
            sink.Count.ShouldBe(0);
        }
        finally
        {
            release.TrySetResult();
        }

        node.Complete();
        foreach (var expected in new[]
        {
            first,
            first,
            second,
            second
        }

        )
            (await sink.ReceiveAsync(Timeout)).ShouldBeSameAs(expected);
        await node.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Command_failure_drains_as_error_and_command_input_continues()
    {
        await using var node = new Probe(message => Task.FromResult<IEnumerable<FlowMessage>>([]));
        var sink = Sink(node);
        await node.Command.SendAsync(Message(1));
        await node.Command.SendAsync(Message(2));
        node.Complete();
        (await sink.ReceiveAsync(Timeout)).IsError.ShouldBeTrue();
        await node.Completion.WaitAsync(Timeout);
        node.Commands.ShouldBe(2);
    }

    private static BufferBlock<FlowMessage> Sink(FlowNode node)
    {
        var sink = new BufferBlock<FlowMessage>();
        node.Output.LinkTo(sink, new() { PropagateCompletion = true });
        return sink;
    }

    [Fact]
    public async Task Flush_enumeration_failure_faults_lifetime_without_partial_output()
    {
        var failure = new InvalidOperationException("flush failed");
        await using var node = new BrokenFlush(failure);
        var sink = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(sink);
        node.Complete();
        (await Record.ExceptionAsync(() => node.Completion.WaitAsync(Timeout))).ShouldBeSameAs(failure);
        sink.Count.ShouldBe(0);
        node.Output.Completion.IsFaulted.ShouldBeTrue();
        node.Extra.Completion.IsFaulted.ShouldBeTrue();
    }

    private sealed class BrokenFlush : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        private readonly Exception _error;
        public BroadcastBlock<FlowMessage> Extra { get; }

        public BrokenFlush(Exception error)
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
            _error = error;
            Extra = lifetime.HasOutput(new BroadcastBlock<FlowMessage>(static message => message, new DataflowBlockOptions { BoundedCapacity = 128 }));
            Lifetime.Observe();
        }

        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message) => Task.FromResult<IEnumerable<FlowMessage>>([]);
        private ValueTask<IEnumerable<FlowMessage>> FlushAsync() => ValueTask.FromResult(Results());
        private IEnumerable<FlowMessage> Results()
        {
            yield return Message(1);
            throw _error;
        }
    }

    private sealed class Probe : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        private readonly Func<FlowMessage, Task<IEnumerable<FlowMessage>>> _process;
        public int Commands;
        public ITargetBlock<FlowMessage> Command { get; }

        public Probe(Func<FlowMessage, Task<IEnumerable<FlowMessage>>> process, ExecutionDataflowBlockOptions? options = null)
        {
            var execution = options ?? new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 1;
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
            var lifetime = Lifetime;
            _process = process;
            Command = lifetime.HasInput(new TransformManyBlock<FlowMessage, FlowMessage>(commandMessage => commandMessage.ProcessMessageAsync(this, value =>
            {
                Commands++;
                if (value.Value.Json.GetInt32() == 1)
                    throw new InvalidOperationException("command failed");
                return Task.FromResult<IEnumerable<FlowMessage>>([]);
            }, Output, Stopping), options ?? new ExecutionDataflowBlockOptions { BoundedCapacity = 128 }));
            ((ISourceBlock<FlowMessage>)Command).LinkTo((ITargetBlock<FlowMessage>)Output);
            Lifetime.Observe();
        }

        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message) => _process(message);
    }
}
