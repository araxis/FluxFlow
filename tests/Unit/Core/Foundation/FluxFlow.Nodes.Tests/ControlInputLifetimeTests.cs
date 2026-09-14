using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class ControlInputLifetimeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static FlowMessage Message(int value) => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    [Fact]
    public async Task Native_control_admission_is_bounded_and_cancellable_without_cancelling_accepted_work()
    {
        var entered = Gate();
        var release = Gate();
        var received = new ConcurrentQueue<FlowMessage>();
        await using var source = new Source(async (message, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            received.Enqueue(message);
        });
        var first = Message(1);
        try
        {
            source.Control.ShouldBeOfType<ActionBlock<FlowMessage>>();
            source.Ports.Controls["Ack"].ShouldBeSameAs(source.Control);
            (await source.Control.SendAsync(first)).ShouldBeTrue();
            await entered.Task.WaitAsync(Deadline);
            received.ShouldBeEmpty();
            using var cancellation = new CancellationTokenSource();
            var pending = source.Control.SendAsync(Message(2), cancellation.Token);
            pending.IsCompleted.ShouldBeFalse();
            cancellation.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => pending.WaitAsync(Deadline));
            source.StopProduction();
            await source.ProductionCompletion.WaitAsync(Deadline);
            source.Control.Completion.IsCompleted.ShouldBeFalse();
            source.Completion.IsCompleted.ShouldBeFalse();
            source.Complete();
            (await source.Control.SendAsync(Message(3))).ShouldBeFalse();
            source.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await source.Completion.WaitAsync(Deadline);
        received.ShouldHaveSingleItem().ShouldBeSameAs(first);
        source.Control.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        await source.DisposeAsync();
        source.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Native_control_fault_cancels_and_joins_accepted_work_before_disposal()
    {
        var entered = Gate();
        var cancelled = Gate();
        var release = Gate();
        var exited = Gate();
        await using var source = new Source(async (_, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                cancelled.TrySetResult();
                await release.Task;
                throw;
            }
            finally { exited.TrySetResult(); }
        });
        var failure = new InvalidOperationException("control failed");
        Task disposing;
        try
        {
            (await source.Control.SendAsync(Message(1))).ShouldBeTrue();
            await entered.Task.WaitAsync(Deadline);
            source.Fault(failure);
            await cancelled.Task.WaitAsync(Deadline);
            disposing = source.DisposeAsync().AsTask();
            source.Completion.IsCompleted.ShouldBeFalse();
            disposing.IsCompleted.ShouldBeFalse();
            source.DisposeCalls.ShouldBe(0);
        }
        finally { release.TrySetResult(); }
        (await Should.ThrowAsync<InvalidOperationException>(() => source.Completion.WaitAsync(Deadline)))
            .ShouldBeSameAs(failure);
        await disposing.WaitAsync(Deadline);
        exited.Task.IsCompletedSuccessfully.ShouldBeTrue();
        source.Control.Completion.IsFaulted.ShouldBeTrue();
        (await source.Control.SendAsync(Message(2))).ShouldBeFalse();
        source.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Native_control_port_fault_joins_inflight_action_before_faulting_source()
    {
        var entered = Gate();
        var release = Gate();
        await using var source = new Source(async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
        });
        var failure = new InvalidOperationException("native port failed");
        try
        {
            (await source.Control.SendAsync(Message(1))).ShouldBeTrue();
            await entered.Task.WaitAsync(Deadline);
            source.Control.Fault(failure);
            (await source.Control.SendAsync(Message(2))).ShouldBeFalse();
            source.Control.Completion.IsCompleted.ShouldBeFalse();
            source.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        (await Should.ThrowAsync<InvalidOperationException>(() => source.Completion.WaitAsync(Deadline)))
            .ShouldBeSameAs(failure);
        source.Sibling.Completion.IsFaulted.ShouldBeTrue();
    }

    [Fact]
    public async Task Native_control_handler_failure_faults_source_and_sibling_controls()
    {
        var failure = new InvalidOperationException("handler failed");
        await using var source = new Source((_, _) => Task.FromException(failure));
        (await source.Control.SendAsync(Message(1))).ShouldBeTrue();
        (await Should.ThrowAsync<InvalidOperationException>(() => source.Completion.WaitAsync(Deadline)))
            .ShouldBeSameAs(failure);
        source.Sibling.Completion.IsFaulted.ShouldBeTrue();
        source.ProductionCompletion.IsFaulted.ShouldBeTrue();
        (await source.Sibling.SendAsync(Message(2))).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_production_failure_closes_controls_without_explicit_complete(bool cancellationCallback)
    {
        var failure = new InvalidOperationException("production failed");
        await using var source = new Source(static (_, _) => Task.CompletedTask, token =>
        {
            if (!cancellationCallback) return Task.FromException(failure);
            token.Register(() => throw failure);
            return Task.CompletedTask;
        });
        await source.StartAsync();
        (await Should.ThrowAsync<InvalidOperationException>(() => source.Completion.WaitAsync(Deadline)))
            .ShouldBeSameAs(failure);
        source.ProductionCompletion.IsFaulted.ShouldBeTrue();
        source.Control.Completion.IsFaulted.ShouldBeTrue();
        source.Sibling.Completion.IsFaulted.ShouldBeTrue();
        await source.DisposeAsync();
        source.DisposeCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canonical_control_target_preserves_identity_and_error_payload(bool error)
    {
        var message = error
            ? FlowMessage.CreateError(new FlowError("test.control", "failed", "test", false))
            : FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { Value = "payload" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        FlowMessage? receivedMessage = null;
        var calls = 0;
        await using var source = new Source((value, _) =>
        {
            receivedMessage = value;
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        });
        (await source.Control.SendAsync(message)).ShouldBeTrue();
        source.Control.Complete();
        await source.Control.Completion.WaitAsync(Deadline);
        calls.ShouldBe(1);
        var received = receivedMessage.ShouldNotBeNull();
        received.ShouldBeSameAs(message);
        received.MessageId.ShouldBe(message.MessageId);
        received.TraceId.ShouldBe(message.TraceId);
        received.Timestamp.ShouldBe(message.Timestamp);
        received.IsError.ShouldBe(error);
        if (error) received.Error.ShouldBeSameAs(message.Error);
        else received.Value.ShouldBeSameAs(message.Value);
    }



    private sealed class Source : FlowSource
    {
        private readonly Func<CancellationToken, Task> _produce;
        public Source(Func<FlowMessage, CancellationToken, Task> handler, Func<CancellationToken, Task>? produce = null)
        {
            _produce = produce ?? (token => Task.Delay(Timeout.InfiniteTimeSpan, token));
            Control = AddControlInput("Ack", handler, 1);
            Sibling = AddControlInput("Nak", static (_, _) => Task.CompletedTask, 1);
        }
        public ITargetBlock<FlowMessage> Control { get; }
        public ITargetBlock<FlowMessage> Sibling { get; }
        public int DisposeCalls { get; private set; }
        protected override Task RunAsync(CancellationToken cancellationToken)
            => _produce(cancellationToken);
        protected override ValueTask OnDisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }
}
