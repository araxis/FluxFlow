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

    [Fact]
    public async Task Zero_one_many_preserve_identity_lineage_and_reliable_fanout()
    {
        await using var node = new Probe(message => Task.FromResult<IEnumerable<FlowMessage>>(
            message.Value.Json.GetInt32() switch
            {
                0 => [],
                1 => [message],
                _ => [message.WithValue(message.Value), message.WithValue(message.Value)]
            }));
        var first = Sink(node);
        var second = Sink(node);
        var forwarded = Message(1);
        var parent = Message(2);
        foreach (var message in new[] { Message(0), forwarded, parent })
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
        await using var node = new Probe(message => Task.FromResult(
            message.Value.Json.GetInt32() == 1 ? Broken(message) : new[] { message }));
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
            if (message.Value.Json.GetInt32() == 1) await release.Task;
            else secondEntered.SetResult();
            return new[] { message, message };
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
        finally { release.TrySetResult(); }
        node.Complete();
        foreach (var expected in new[] { first, first, second, second })
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

    private static BufferBlock<FlowMessage> Sink(FlowTransformNode node)
    {
        var sink = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 1 });
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

    private sealed class BrokenFlush : FlowTransformNode
    {
        private readonly Exception _error;
        public FlowOutput<FlowMessage> Extra { get; }
        public BrokenFlush(Exception error)
        {
            _error = error;
            Extra = AddOutput<FlowMessage>();
        }
        protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
            => Task.FromResult<IEnumerable<FlowMessage>>([]);
        protected override ValueTask<IEnumerable<FlowMessage>> FlushAsync()
            => ValueTask.FromResult(Results());
        private IEnumerable<FlowMessage> Results()
        {
            yield return Message(1);
            throw _error;
        }
    }

    private sealed class Probe : FlowTransformNode
    {
        private readonly Func<FlowMessage, Task<IEnumerable<FlowMessage>>> _process;
        public int Commands;
        public ITargetBlock<FlowMessage> Command { get; }
        public Probe(Func<FlowMessage, Task<IEnumerable<FlowMessage>>> process,
            ExecutionDataflowBlockOptions? options = null) : base(options, outputCapacity: 1)
        {
            _process = process;
            Command = AddInput(message =>
            {
                Commands++;
                if (message.Value.Json.GetInt32() == 1) throw new InvalidOperationException("command failed");
                return Task.CompletedTask;
            });
        }
        protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message) => _process(message);
    }
}
