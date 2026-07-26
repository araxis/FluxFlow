using FluxFlow.Data;
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
        result.Value.ShouldBe(42);
        result.CorrelationId.ShouldBe(message.CorrelationId);
    }

    [Fact]
    public async Task Output_FansOutToEveryConsumer()
    {
        await using var node = new DoubleNode();
        var first = Sink(node.Output);
        var second = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(5));

        (await first.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Value.ShouldBe(10);
        (await second.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Value.ShouldBe(10);
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

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    private sealed class DoubleNode : FlowNode<int, int>
    {
        public int ProcessCount { get; private set; }

        protected override Task ProcessAsync(FlowMessage<int> message)
        {
            ProcessCount++;
            Emit(message.With(message.Value * 2));
            return Task.CompletedTask;
        }
    }

    private sealed class BoomNode : FlowNode<int, int>
    {
        protected override Task ProcessAsync(FlowMessage<int> message)
            => throw new InvalidOperationException("boom");
    }
}
