using System.Text.Json;
using FluxFlow.Data;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.RequestReply.Tests;

public sealed class BridgeTerminalTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData("complete", false, false)]
    [InlineData("complete", true, false)]
    [InlineData("complete", true, true)]
    [InlineData("fault", false, false)]
    [InlineData("fault", true, false)]
    [InlineData("fault", true, true)]
    [InlineData("dispose", false, false)]
    [InlineData("dispose", true, false)]
    [InlineData("dispose", true, true)]
    public async Task Bridge_shutdown_joins_callers_and_preserves_cleanup_errors(
        string operation, bool stopFails, bool disposalFails)
    {
        var clock = new FailureClock { DisposalFails = disposalFails };
        var bridge = new RequestReplyCoordinator<string, string>(clock: clock);
        await bridge.StartAsync();
        var completion = bridge.Completion;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new BlockingContext("first", release.Task);
        var second = new BlockingContext("second", release.Task);
        foreach (var context in new[] { first, second })
        {
            var receive = bridge.Output.ReceiveAsync();
            (await bridge.Incoming.SendAsync(context)).ShouldBeTrue();
            var request = await receive.WaitAsync(Deadline);
            request.Value!.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(context.Request);
            request.CorrelationId.ShouldNotBeNull();
            request.CorrelationId.ShouldNotBe(context.CorrelationId);
        }
        var initiatingError = new InvalidOperationException("bridge faulted");
        clock.ChangeFails = stopFails;
        if (operation == "fault" && stopFails)
            clock.BeforeChangeFailure = () => bridge.Fault(clock.ChangeFailure);
        Task? disposal = null;
        if (operation == "complete") bridge.Complete();
        else if (operation == "fault") bridge.Fault(initiatingError);
        else disposal = bridge.DisposeAsync().AsTask();
        try
        {
            await Task.WhenAll(first.Entered.Task, second.Entered.Task).WaitAsync(Deadline);
            completion.IsCompleted.ShouldBeFalse();
            bridge.Completion.ShouldBeSameAs(completion);
            clock.DisposeCalls.ShouldBe(0);
            bridge.InFlightCount.ShouldBe(0);
        }
        finally { release.TrySetResult(); }

        var completionError = await Record.ExceptionAsync(() => completion.WaitAsync(Deadline));
        var expectedCompletion = new[]
        {
            operation == "fault" ? initiatingError : null,
            stopFails ? clock.ChangeFailure : null,
            disposalFails ? clock.DisposalFailure : null
        }.OfType<Exception>().ToArray();
        AssertFailures(completionError, expectedCompletion);
        foreach (var context in new[] { first, second })
        {
            context.FailCalls.ShouldBe(1);
            context.Exited.Task.IsCompletedSuccessfully.ShouldBeTrue();
            context.ReplyCalls.ShouldBe(0);
            context.AckCalls.ShouldBe(0);
            if (operation == "fault") context.Error.ShouldBeSameAs(initiatingError);
            else context.Error.ShouldBeOfType<OperationCanceledException>();
        }
        (await bridge.Incoming.SendAsync(new BlockingContext("later", Task.CompletedTask)).WaitAsync(Deadline))
            .ShouldBeFalse();
        (await bridge.Responses.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("late", new JsonSerializerOptions(JsonSerializerDefaults.Web))), first.CorrelationId)).WaitAsync(Deadline))
            .ShouldBeFalse();
        await bridge.Events.Completion.WaitAsync(Deadline);
        disposal ??= bridge.DisposeAsync().AsTask();
        var disposalError = await Record.ExceptionAsync(() => disposal.WaitAsync(Deadline));
        AssertFailures(disposalError, new[]
        {
            stopFails ? clock.ChangeFailure : null,
            disposalFails ? clock.DisposalFailure : null
        }.OfType<Exception>().ToArray());
        await bridge.DisposeAsync();
        clock.DisposeCalls.ShouldBe(1);
        bridge.Output.Completion.IsCompleted.ShouldBeTrue();
        bridge.Incoming.Completion.IsCompleted.ShouldBeTrue();
        bridge.Responses.Completion.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Bridge_admission_failure_notifies_untracked_caller_and_stops_the_graph()
    {
        var clock = new FailureClock();
        var bridge = new RequestReplyCoordinator<string, string>(clock: clock);
        await bridge.StartAsync();
        var first = new BlockingContext("first", Task.CompletedTask);
        var second = new BlockingContext("second", Task.CompletedTask);
        var receive = bridge.Output.ReceiveAsync();
        (await bridge.Incoming.SendAsync(first)).ShouldBeTrue();
        (await receive.WaitAsync(Deadline)).CorrelationId.ShouldNotBe(first.CorrelationId);
        clock.ChangeFails = true;
        (await bridge.Incoming.SendAsync(second)).ShouldBeTrue();
        var error = await Should.ThrowAsync<InvalidOperationException>(() => bridge.Completion.WaitAsync(Deadline));
        error.ShouldBeSameAs(clock.ChangeFailure);
        foreach (var context in new[] { first, second })
        {
            context.Error.ShouldBeSameAs(clock.ChangeFailure);
            context.FailCalls.ShouldBe(1);
            context.ReplyCalls.ShouldBe(0);
            context.AckCalls.ShouldBe(0);
            context.Exited.Task.IsCompletedSuccessfully.ShouldBeTrue();
        }
        bridge.InFlightCount.ShouldBe(0);
        (await bridge.Incoming.SendAsync(new BlockingContext("later", Task.CompletedTask))).ShouldBeFalse();
        (await bridge.Responses.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("late", new JsonSerializerOptions(JsonSerializerDefaults.Web))), first.CorrelationId))).ShouldBeFalse();
        (await Should.ThrowAsync<InvalidOperationException>(() => bridge.DisposeAsync().AsTask().WaitAsync(Deadline)))
            .ShouldBeSameAs(clock.ChangeFailure);
        await bridge.DisposeAsync();
        clock.DisposeCalls.ShouldBe(1);
        await bridge.Events.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Bridge_fault_releases_backpressured_output_without_waiting_for_consumer()
    {
        var clock = new FailureClock();
        var bridge = new RequestReplyCoordinator<string, string>(clock: clock);
        await bridge.StartAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = 0;
        var consumer = new ActionBlock<FlowMessage>(async _ =>
        {
            Interlocked.Increment(ref received);
            entered.TrySetResult();
            await release.Task;
        }, new ExecutionDataflowBlockOptions { BoundedCapacity = 1 });
        using var link = bridge.Output.LinkTo(consumer);
        var contexts = new[]
        {
            new BlockingContext("first", Task.CompletedTask),
            new BlockingContext("second", Task.CompletedTask),
            new BlockingContext("third", Task.CompletedTask)
        };
        var error = new InvalidOperationException("stop backpressured delivery");
        try
        {
            (await bridge.Incoming.SendAsync(contexts[0])).ShouldBeTrue();
            await entered.Task.WaitAsync(Deadline);
            (await bridge.Incoming.SendAsync(contexts[1])).ShouldBeTrue();
            (await bridge.Incoming.SendAsync(contexts[2])).ShouldBeTrue();
            // Ordered ingress has queued the second output before reading the third.
            await contexts[2].Read.Task.WaitAsync(Deadline);
            bridge.Fault(error);
            (await Should.ThrowAsync<InvalidOperationException>(() => bridge.Completion.WaitAsync(Deadline)))
                .ShouldBeSameAs(error);
            received.ShouldBe(1);
            bridge.InFlightCount.ShouldBe(0);
            foreach (var context in contexts)
            {
                context.FailCalls.ShouldBe(1);
                context.Error.ShouldBeSameAs(error);
                context.ReplyCalls.ShouldBe(0);
                context.AckCalls.ShouldBe(0);
            }
            await bridge.DisposeAsync().AsTask().WaitAsync(Deadline);
            clock.DisposeCalls.ShouldBe(1);
        }
        finally
        {
            release.TrySetResult();
            consumer.Complete();
            await consumer.Completion.WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task Bridge_disposal_preserves_cancellation_callback_error_and_joins_reply()
    {
        var clock = new FailureClock();
        var bridge = new RequestReplyCoordinator<string, string>(clock: clock);
        await bridge.StartAsync();
        var context = new CancellableReplyContext();
        var receive = bridge.Output.ReceiveAsync();
        (await bridge.Incoming.SendAsync(context)).ShouldBeTrue();
        var request = await receive.WaitAsync(Deadline);
        (await bridge.Responses.SendAsync(request.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("reply", new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
        await context.Entered.Task.WaitAsync(Deadline);
        var disposal = bridge.DisposeAsync().AsTask();
        try
        {
            disposal.IsCompleted.ShouldBeFalse();
            bridge.Completion.IsCompleted.ShouldBeFalse();
            context.CancelCalls.ShouldBe(1);
        }
        finally { context.Release.TrySetResult(); }
        (await Should.ThrowAsync<InvalidOperationException>(() => disposal.WaitAsync(Deadline)))
            .ShouldBeSameAs(context.CancellationFailure);
        (await Should.ThrowAsync<InvalidOperationException>(() => bridge.Completion.WaitAsync(Deadline)))
            .ShouldBeSameAs(context.CancellationFailure);
        context.Response.ShouldBe("reply");
        context.ReplyCalls.ShouldBe(1);
        context.FailCalls.ShouldBe(0);
        context.AckCalls.ShouldBe(0);
        context.Exited.ShouldBeTrue();
        bridge.InFlightCount.ShouldBe(0);
        clock.DisposeCalls.ShouldBe(1);
        await bridge.Events.Completion.WaitAsync(Deadline);
        await bridge.DisposeAsync();
        clock.DisposeCalls.ShouldBe(1);
    }

    private static void AssertFailures(Exception? actual, Exception[] expected)
    {
        if (expected.Length == 0) { actual.ShouldBeNull(); return; }
        actual.ShouldNotBeNull();
        actual.ShouldNotBeOfType<TimeoutException>();
        var failures = actual is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : [actual];
        failures.ShouldBe(expected);
    }

    private sealed class BlockingContext(string request, Task release) : IRequestContext<string, string>
    {
        public string Request { get { Read.TrySetResult(); return request; } }
        public TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CorrelationId? CorrelationId { get; } = new CorrelationId(request);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Error { get; private set; }
        public int FailCalls { get; private set; }
        public int ReplyCalls { get; private set; }
        public int AckCalls { get; private set; }
        public Task ReplyAsync(string response, CancellationToken cancellationToken = default)
        { ReplyCalls++; return Task.CompletedTask; }
        public Task AcknowledgeAsync(CancellationToken cancellationToken = default)
        { AckCalls++; return Task.CompletedTask; }
        public async Task FailAsync(Exception error, CancellationToken cancellationToken = default)
        {
            Error = error;
            FailCalls++;
            Entered.TrySetResult();
            await release;
            Exited.TrySetResult();
        }
    }

    private sealed class CancellableReplyContext : IRequestContext<string, string>
    {
        public string Request => "request";
        public CorrelationId? CorrelationId => new CorrelationId("cancellable-reply");
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public InvalidOperationException CancellationFailure { get; } = new("reply cancellation failed");
        public string? Response { get; private set; }
        public int ReplyCalls { get; private set; }
        public int FailCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public int AckCalls { get; private set; }
        public bool Exited { get; private set; }
        public async Task ReplyAsync(string response, CancellationToken cancellationToken = default)
        {
            Response = response;
            ReplyCalls++;
            using var registration = cancellationToken.Register(() =>
            {
                CancelCalls++;
                throw CancellationFailure;
            });
            Entered.TrySetResult();
            await Release.Task;
            Exited = true;
        }
        public Task FailAsync(Exception error, CancellationToken cancellationToken = default)
        { FailCalls++; return Task.CompletedTask; }
        public Task AcknowledgeAsync(CancellationToken cancellationToken = default)
        { AckCalls++; return Task.CompletedTask; }
    }

    private sealed class FailureClock : TimeProvider
    {
        private readonly FakeTimeProvider _inner = new();
        public bool ChangeFails { get; set; }
        public Action? BeforeChangeFailure;
        public bool DisposalFails { get; set; }
        public InvalidOperationException ChangeFailure { get; } = new("bridge timer change failed");
        public InvalidOperationException DisposalFailure { get; } = new("bridge timer disposal failed");
        public int DisposeCalls { get; private set; }
        public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new Timer(_inner.CreateTimer(callback, state, dueTime, period), this);
        private sealed class Timer(ITimer inner, FailureClock owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (!owner.ChangeFails) return inner.Change(dueTime, period);
                // Reproduce a cleanup fault racing the initiating shutdown fault.
                Interlocked.Exchange(ref owner.BeforeChangeFailure, null)?.Invoke();
                throw owner.ChangeFailure;
            }
            public void Dispose() => throw new NotSupportedException();
            public async ValueTask DisposeAsync()
            {
                owner.DisposeCalls++;
                await inner.DisposeAsync();
                if (owner.DisposalFails) throw owner.DisposalFailure;
            }
        }
    }
}
