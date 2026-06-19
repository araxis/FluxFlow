using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Nodes.Tests;

// Regression tests for two kit-level fixes:
//  1. Faulting a node must FLUSH its Errors/Events ports (which carry the diagnostics that
//     explain the fault), not Fault them — faulting a BroadcastBlock discards its buffered
//     message, which used to drop the very FlowError a consumer needed to observe.
//  2. OnInputCompletedAsync lets a node flush work it held back (e.g. a debounce's pending
//     item) after the input drains and before the outputs complete.
public sealed class FlowFaultAndDrainHookTests
{
    [Fact]
    public async Task Source_Fault_FlushesBufferedErrorAndStillFaultsCompletion()
    {
        var correlation = CorrelationId.New();
        await using var source = new EmitThenThrowSource(correlation);
        // PropagateCompletion = false: the source flushes then completes its Errors port, but
        // the buffered FlowError must remain observable on the linked sink regardless.
        var errors = new BufferBlock<FlowError>();
        source.Errors.LinkTo(errors, new DataflowLinkOptions { PropagateCompletion = false });

        await source.StartAsync();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(42);
        error.CorrelationId.ShouldBe(correlation);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => source.Completion.WaitAsync(TimeSpan.FromSeconds(30)));
        thrown.Message.ShouldBe("boom");
    }

    [Fact]
    public async Task Node_Fault_FlushesBufferedErrorAndStillFaultsCompletion()
    {
        var correlation = CorrelationId.New();
        await using var node = new EmitErrorThenSignalNode(correlation);
        var errors = new BufferBlock<FlowError>();
        node.Errors.LinkTo(errors, new DataflowLinkOptions { PropagateCompletion = false });

        await node.Input.SendAsync(FlowMessage.Create(1));
        // Wait until the error has actually been posted to the Errors broadcast before
        // faulting, so we are exercising the buffered-message-survives-fault path.
        await node.ErrorEmitted.WaitAsync(TimeSpan.FromSeconds(30));
        node.Fault(new InvalidOperationException("boom"));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(42);
        error.CorrelationId.ShouldBe(correlation);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => node.Completion.WaitAsync(TimeSpan.FromSeconds(30)));
        thrown.Message.ShouldBe("boom");
    }

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
        // Nothing emitted yet: the node holds the latest until the input drains.
        output.TryReceive(out _).ShouldBeFalse();

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        // The held item was flushed by the drain hook, reaching the linked consumer before
        // the output completed (PropagateCompletion would otherwise have closed it empty).
        var flushed = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        flushed.Payload.ShouldBe(2);
        flushed.CorrelationId.ShouldBe(last.CorrelationId);
        output.TryReceive(out _).ShouldBeFalse();
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    // Emits one error then throws — the source's fault path must flush the buffered error.
    private sealed class EmitThenThrowSource(CorrelationId correlation) : FlowSource<int>
    {
        protected override Task RunAsync(CancellationToken cancellationToken)
        {
            EmitError(new FlowError
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = correlation,
                Code = 42,
                Message = "bad"
            });
            throw new InvalidOperationException("boom");
        }
    }

    // Emits one error from ProcessAsync and signals once it has been posted, so the test can
    // fault the node deterministically with the error already buffered on the Errors port.
    private sealed class EmitErrorThenSignalNode(CorrelationId correlation) : FlowNode<int, int>
    {
        private readonly TaskCompletionSource _emitted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ErrorEmitted => _emitted.Task;

        protected override Task ProcessAsync(FlowMessage<int> message)
        {
            EmitError(new FlowError
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = correlation,
                Code = 42,
                Message = "bad"
            });
            _emitted.TrySetResult();
            return Task.CompletedTask;
        }
    }

    // Holds only the latest input and flushes it via the drain hook when the input completes.
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
