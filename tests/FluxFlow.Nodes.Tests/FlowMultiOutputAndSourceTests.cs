using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowMultiOutputAndSourceTests
{
    [Fact]
    public async Task MultiOutput_RoutesToTheRightExtraPort_PreservingCorrelation()
    {
        await using var node = new EvenOddNode();
        var even = Sink(node.Output);   // primary output = evens
        var odd = Sink(node.Odd);       // extra output = odds

        var two = FlowMessage.Create(2);
        var three = FlowMessage.Create(3);
        await node.Input.SendAsync(two);
        await node.Input.SendAsync(three);

        var evenMsg = await even.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        evenMsg.Payload.ShouldBe(2);
        evenMsg.CorrelationId.ShouldBe(two.CorrelationId);

        var oddMsg = await odd.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        oddMsg.Payload.ShouldBe(3);
        oddMsg.CorrelationId.ShouldBe(three.CorrelationId);
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
        items.Select(m => m.Payload).ShouldBe([0, 1, 2]);
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

    // 1 input, 2 domain outputs: evens on the primary Output, odds on an extra port.
    private sealed class EvenOddNode : FlowNode<int, int>
    {
        private readonly BroadcastBlock<FlowMessage<int>> _odd;

        public EvenOddNode() => _odd = AddOutput<FlowMessage<int>>();

        public ISourceBlock<FlowMessage<int>> Odd => _odd;

        protected override Task ProcessAsync(FlowMessage<int> message)
        {
            if (message.Payload % 2 == 0)
            {
                Emit(message);
            }
            else
            {
                _odd.Post(message);
            }

            return Task.CompletedTask;
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
                Emit(FlowMessage.Create(i));
                if (count == int.MaxValue)
                {
                    await Task.Delay(5, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
