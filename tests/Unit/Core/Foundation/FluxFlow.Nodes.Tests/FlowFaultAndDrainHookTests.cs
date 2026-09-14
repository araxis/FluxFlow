using System.Text.Json;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowFaultAndDrainHookTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task DrainHook_awaits_backpressured_final_item_before_node_completion()
    {
        await using var node = new HoldLastThenFlushNode();
        var output = new PostponedTargetBlock<FlowMessage>();
        using var link = node.Output.LinkTo(
            output,
            new DataflowLinkOptions { PropagateCompletion = true });

        var first = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var last = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(2, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        await node.Input.SendAsync(first);
        await node.Input.SendAsync(last);
        node.Complete();
        await output.WaitForOfferAsync(Timeout);
        node.Completion.IsCompleted.ShouldBeFalse();

        output.AcceptNext();
        await node.Completion.WaitAsync(Timeout);
        await output.Completion.WaitAsync(Timeout);

        var flushed = output.Accepted.ShouldHaveSingleItem();
        flushed.Value.ShouldNotBeNull().Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(2);
        flushed.CorrelationId.ShouldBe(last.CorrelationId);
        flushed.ShouldBeSameAs(last);
    }

    private sealed class HoldLastThenFlushNode : FlowNode
    {
        private readonly object _gate = new();
        private FlowMessage? _last;

        protected override Task ProcessAsync(FlowMessage message)
        {
            lock (_gate)
            {
                _last = message;
            }

            return Task.CompletedTask;
        }

        protected override async ValueTask OnInputCompletedAsync()
        {
            FlowMessage? held;
            lock (_gate)
            {
                held = _last;
                _last = null;
            }

            if (held is { } message)
            {
                await EmitAsync(message, Stopping);
            }
        }
    }
}
