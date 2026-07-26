using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowFaultAndDrainHookTests
{
    [Fact]
    public async Task DrainHook_FlushesHeldItemAfterInputDrains_BeforeOutputCompletes()
    {
        await using var node = new HoldLastThenFlushNode();
        var output = new BufferBlock<FlowMessage<int>>();
        node.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });

        var first = FlowMessage.Create(1);
        var last = FlowMessage.Create(2);
        await node.Input.SendAsync(first);
        await node.Input.SendAsync(last);
        output.TryReceive(out _).ShouldBeFalse();

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var flushed = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        flushed.Value.ShouldBe(2);
        flushed.CorrelationId.ShouldBe(last.CorrelationId);
        output.TryReceive(out _).ShouldBeFalse();
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class HoldLastThenFlushNode : FlowNode<int, int>
    {
        private readonly object _gate = new();
        private FlowMessage<int>? _last;

        protected override Task ProcessAsync(FlowMessage<int> message)
        {
            lock (_gate)
            {
                _last = message;
            }

            return Task.CompletedTask;
        }

        protected override ValueTask OnInputCompletedAsync()
        {
            FlowMessage<int>? held;
            lock (_gate)
            {
                held = _last;
                _last = null;
            }

            if (held is { } message)
            {
                Emit(message);
            }

            return ValueTask.CompletedTask;
        }
    }
}
