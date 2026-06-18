using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowNodeTests
{
    [Fact]
    public async Task Processes_AndPreservesCorrelation()
    {
        await using var node = new DoubleNode();
        var output = Sink(node.Output);

        var message = FlowMessage.Create(21);
        await node.Input.SendAsync(message);

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.Payload.ShouldBe(42);
        result.CorrelationId.ShouldBe(message.CorrelationId);
    }

    [Fact]
    public async Task Output_FansOutToEveryConsumer()
    {
        await using var node = new DoubleNode();
        var first = Sink(node.Output);
        var second = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(5));

        (await first.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.ShouldBe(10);
        (await second.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Payload.ShouldBe(10);
    }

    [Fact]
    public async Task HandlerThrow_SurfacesFlowError_StampedWithCorrelation_WithoutFaulting()
    {
        await using var node = new BoomNode();
        var errors = Sink(node.Errors);

        var message = FlowMessage.Create(1);
        await node.Input.SendAsync(message);

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.CorrelationId.ShouldBe(message.CorrelationId);
        error.Message.ShouldBe("boom");

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    private sealed class DoubleNode : FlowNode<int, int>
    {
        protected override Task ProcessAsync(FlowMessage<int> message)
        {
            Emit(message.With(message.Payload * 2));
            return Task.CompletedTask;
        }
    }

    private sealed class BoomNode : FlowNode<int, int>
    {
        protected override Task ProcessAsync(FlowMessage<int> message)
            => throw new InvalidOperationException("boom");
    }
}
