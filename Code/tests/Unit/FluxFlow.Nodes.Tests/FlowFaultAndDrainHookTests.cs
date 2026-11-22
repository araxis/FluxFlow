using System.Text.Json;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Nodes.Tests;
public sealed class FlowFaultAndDrainHookTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    [Fact]
    public async Task DrainHook_publishes_final_item_before_output_completion()
    {
        await using var node = new HoldLastThenFlushNode();
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });
        var first = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var last = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(2, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        await node.Input.SendAsync(first);
        await node.Input.SendAsync(last);
        node.Complete();
        await node.Completion.WaitAsync(Timeout);
        var flushed = await output.ReceiveAsync(Timeout);
        await output.Completion.WaitAsync(Timeout);
        flushed.Value.ShouldNotBeNull().Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(2);
        flushed.CorrelationId.ShouldBe(last.CorrelationId);
        flushed.ShouldBeSameAs(last);
    }

    private sealed class HoldLastThenFlushNode : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public HoldLastThenFlushNode()
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, flush: OnInputCompletedAsync, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        private readonly object _gate = new();
        private FlowMessage? _last;
        private Task ProcessAsync(FlowMessage message)
        {
            lock (_gate)
            {
                _last = message;
            }

            return Task.CompletedTask;
        }

        private async ValueTask OnInputCompletedAsync()
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
