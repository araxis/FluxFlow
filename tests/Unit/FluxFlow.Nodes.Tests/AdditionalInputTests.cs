using FluxFlow.Core.Nodes;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class AdditionalInputTests
{
    [Fact]
    public async Task Additional_input_errors_become_data_and_later_messages_continue()
    {
        await using var node = new Probe(false);
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output);
        await node.Other.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeTrue();
        await node.Other.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeFalse();
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shutdown_joins_or_cancels_active_additional_input(bool fault)
    {
        await using var node = new Probe(true);
        await node.Other.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await node.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (fault) node.Fault(new InvalidOperationException("stop"));
        else
        {
            node.Complete();
            node.Completion.IsCompleted.ShouldBeFalse();
            node.Release.TrySetResult();
        }
        if (fault) await Should.ThrowAsync<InvalidOperationException>(() => node.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        else await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        node.Other.Completion.IsCompleted.ShouldBeTrue();
    }

    private sealed class Probe : FlowNode
    {
        private int _calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ITargetBlock<FlowMessage> Other { get; }
        public Probe(bool wait)
        {
            Other = AddInput(async message =>
            {
                Entered.TrySetResult();
                if (wait) await Release.Task.WaitAsync(Stopping);
                else if (++_calls == 1) throw new InvalidOperationException("bad value");
                await EmitAsync(message, Stopping);
            });
        }
        protected override Task ProcessAsync(FlowMessage message) => Task.CompletedTask;
    }
}
