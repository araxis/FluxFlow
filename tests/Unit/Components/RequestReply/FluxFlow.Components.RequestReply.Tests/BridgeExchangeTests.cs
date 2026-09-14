using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.RequestReply.Tests;

public sealed class BridgeExchangeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reused_transport_correlation_cannot_match_a_stale_reply(bool expireFirst)
    {
        var clock = new FakeTimeProvider();
        await using var bridge = new RequestReplyCoordinator<object, string>(
            new RequestReplyOptions { Capacity = 2, Timeout = TimeSpan.FromSeconds(1) }, clock);
        var output = new BufferBlock<FlowMessage>();
        using var outputLink = bridge.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });
        var unmatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new ActionBlock<FlowMessage>(message =>
        {
            if (message.ToFlowEvent().Name == RequestReplyEvents.Unmatched) unmatched.TrySetResult();
        });
        using var events = bridge.Events.LinkTo(observer, new DataflowLinkOptions { PropagateCompletion = true });
        var id = new CorrelationId("external-reused");
        var first = new Context(() => "first", id);
        var second = new Context(() => "second", id);
        await bridge.StartAsync();
        (await bridge.Incoming.SendAsync(first)).ShouldBeTrue();
        var firstRequest = await output.ReceiveAsync().WaitAsync(Deadline);
        var oldReply = firstRequest.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("old", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        if (expireFirst) clock.Advance(TimeSpan.FromSeconds(1));
        else (await bridge.Responses.SendAsync(oldReply)).ShouldBeTrue();
        await first.Settled.Task.WaitAsync(Deadline);
        if (expireFirst) first.Error.ShouldBeOfType<TimeoutException>();
        else first.Response.ShouldBe("old");

        (await bridge.Incoming.SendAsync(second)).ShouldBeTrue();
        var secondRequest = await output.ReceiveAsync().WaitAsync(Deadline);
        firstRequest.CorrelationId.ShouldNotBe(secondRequest.CorrelationId);
        secondRequest.CorrelationId.ShouldNotBe(id);
        first.CorrelationId.ShouldBe(id);
        second.CorrelationId.ShouldBe(id);
        (await bridge.Responses.SendAsync(oldReply)).ShouldBeTrue();
        await unmatched.Task.WaitAsync(Deadline);
        second.Settled.Task.IsCompleted.ShouldBeFalse();
        bridge.InFlightCount.ShouldBe(1);
        (await bridge.Responses.SendAsync(secondRequest.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("new", new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
        await second.Settled.Task.WaitAsync(Deadline);
        bridge.Complete();
        await bridge.Completion.WaitAsync(Deadline);
        await observer.Completion.WaitAsync(Deadline);
        second.Response.ShouldBe("new");
        second.ReplyCalls.ShouldBe(1);
        second.FailCalls.ShouldBe(0);
        first.ReplyCalls.ShouldBe(expireFirst ? 0 : 1);
        first.FailCalls.ShouldBe(expireFirst ? 1 : 0);
        bridge.InFlightCount.ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_callers_with_one_transport_id_receive_their_own_out_of_order_replies()
    {
        await using var bridge = new RequestReplyCoordinator<object, string>(new RequestReplyOptions { Capacity = 3 });
        var output = new BufferBlock<FlowMessage>();
        using var link = bridge.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });
        var id = new CorrelationId("shared");
        var first = new Context(() => "one", id);
        var second = new Context(() => "two", id);
        await bridge.StartAsync();
        (await bridge.Incoming.SendAsync(first)).ShouldBeTrue();
        (await bridge.Incoming.SendAsync(second)).ShouldBeTrue();
        var one = await output.ReceiveAsync().WaitAsync(Deadline);
        var two = await output.ReceiveAsync().WaitAsync(Deadline);
        one.Value!.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("one");
        two.Value!.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("two");
        one.CorrelationId.ShouldNotBe(two.CorrelationId);
        (await bridge.Responses.SendAsync(two.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("second", new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
        await second.Settled.Task.WaitAsync(Deadline);
        first.Settled.Task.IsCompleted.ShouldBeFalse();
        (await bridge.Responses.SendAsync(one.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("first", new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
        await first.Settled.Task.WaitAsync(Deadline);
        bridge.Complete();
        await bridge.Completion.WaitAsync(Deadline);
        first.Response.ShouldBe("first");
        second.Response.ShouldBe("second");
        first.ReplyCalls.ShouldBe(1);
        second.ReplyCalls.ShouldBe(1);
        first.FailCalls.ShouldBe(0);
        second.FailCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ingress_materialization_failure_settles_every_queued_caller(bool duringSerialization)
    {
        await using var bridge = new RequestReplyCoordinator<object, string>(new RequestReplyOptions { Capacity = 3 });
        var failure = new InvalidOperationException("request extraction failed");
        var first = new Context(() => duringSerialization ? new InvalidPayload(failure) : throw failure);
        var second = new Context(() => "second");
        var third = new Context(() => "third");
        var contexts = new[] { first, second, third };
        foreach (var context in contexts) (await bridge.Incoming.SendAsync(context)).ShouldBeTrue();
        await bridge.StartAsync();
        (await Should.ThrowAsync<InvalidOperationException>(() => bridge.Completion.WaitAsync(Deadline)))
            .ShouldBeSameAs(failure);
        foreach (var context in contexts)
        {
            context.Settled.Task.IsCompletedSuccessfully.ShouldBeTrue();
            context.Error.ShouldBeSameAs(failure);
            context.FailCalls.ShouldBe(1);
            context.ReplyCalls.ShouldBe(0);
            context.AckCalls.ShouldBe(0);
        }
        first.Reads.ShouldBe(1);
        second.Reads.ShouldBe(0);
        third.Reads.ShouldBe(0);
        bridge.InFlightCount.ShouldBe(0);
        bridge.Incoming.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        bridge.Responses.Completion.IsCompleted.ShouldBeTrue();
        (await bridge.Incoming.SendAsync(new Context(() => "late"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Slow_terminal_callbacks_remain_bounded_and_are_joined_after_production_stops()
    {
        await using var bridge = new RequestReplyCoordinator<object, string>(new RequestReplyOptions { Capacity = 2 });
        var output = new BufferBlock<FlowMessage>();
        using var link = bridge.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Context(() => "one", replyHold: release.Task);
        var second = new Context(() => "two", replyHold: release.Task);
        await bridge.StartAsync();
        try
        {
            foreach (var context in new[] { first, second })
            {
                (await bridge.Incoming.SendAsync(context)).ShouldBeTrue();
                var request = await output.ReceiveAsync().WaitAsync(Deadline);
                (await bridge.Responses.SendAsync(request.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("reply", new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
                await context.ReplyEntered.Task.WaitAsync(Deadline);
            }
            bridge.InFlightCount.ShouldBe(0);
            var rejected = new Context(() => "not processed");
            (await bridge.Incoming.SendAsync(rejected)).ShouldBeTrue();
            await rejected.Settled.Task.WaitAsync(Deadline);
            rejected.Reads.ShouldBe(0);
            rejected.FailCalls.ShouldBe(1);
            rejected.Error.ShouldBeOfType<InvalidOperationException>().Message.ShouldContain("caller limit");
            bridge.StopProduction();
            await bridge.ProductionCompletion.WaitAsync(Deadline);
            bridge.Responses.Complete();
            bridge.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await bridge.Completion.WaitAsync(Deadline);
        foreach (var context in new[] { first, second })
        {
            context.Settled.Task.IsCompletedSuccessfully.ShouldBeTrue();
            context.ReplyCalls.ShouldBe(1);
            context.FailCalls.ShouldBe(0);
            context.Response.ShouldBe("reply");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Diagnostic_failure_after_terminal_notification_does_not_notify_the_caller_twice(
        bool timeout, bool responseError)
    {
        var clock = new DiagnosticFailureClock();
        var bridge = new RequestReplyCoordinator<object, string>(
            new RequestReplyOptions { Timeout = TimeSpan.FromSeconds(1) }, clock);
        var context = new Context(() => "request", replied: () => clock.FailReads = true,
            failed: () => clock.FailReads = true);
        await bridge.StartAsync();
        var receive = bridge.Output.ReceiveAsync();
        (await bridge.Incoming.SendAsync(context)).ShouldBeTrue();
        var request = await receive.WaitAsync(Deadline);
        if (timeout) clock.Advance(TimeSpan.FromSeconds(1));
        else (await bridge.Responses.SendAsync(responseError
            ? request.WithError(new FlowError("broken", "response error", "test"))
            : request.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("reply", new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
        var error = await Record.ExceptionAsync(() => bridge.Completion.WaitAsync(Deadline));
        error.ShouldBeSameAs(clock.Failure);
        context.Response.ShouldBe(timeout || responseError ? null : "reply");
        context.ReplyCalls.ShouldBe(timeout || responseError ? 0 : 1);
        context.FailCalls.ShouldBe(timeout || responseError ? 1 : 0);
        context.AckCalls.ShouldBe(0);
        if (timeout) context.Error.ShouldBeOfType<TimeoutException>();
        else if (responseError) context.Error.ShouldNotBeNull().Message.ShouldBe("broken: response error");
        else context.Error.ShouldBeNull();
        bridge.InFlightCount.ShouldBe(0);
        var disposal = await Record.ExceptionAsync(() => bridge.DisposeAsync().AsTask().WaitAsync(Deadline));
        disposal.ShouldBeSameAs(clock.Failure);
        await bridge.DisposeAsync();
    }

    [Fact]
    public async Task Diagnostic_failure_after_capacity_rejection_does_not_repeat_failure_notification()
    {
        var clock = new DiagnosticFailureClock();
        var bridge = new RequestReplyCoordinator<object, string>(new RequestReplyOptions { Capacity = 1 }, clock);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Context(() => "request", replyHold: release.Task);
        var rejected = new Context(() => "not read", failed: () => clock.FailReads = true);
        await bridge.StartAsync();
        try
        {
            var receive = bridge.Output.ReceiveAsync();
            (await bridge.Incoming.SendAsync(first)).ShouldBeTrue();
            var request = await receive.WaitAsync(Deadline);
            (await bridge.Responses.SendAsync(request.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("reply", new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
            await first.ReplyEntered.Task.WaitAsync(Deadline);
            (await bridge.Incoming.SendAsync(rejected)).ShouldBeTrue();
            await rejected.Settled.Task.WaitAsync(Deadline);
        }
        finally { release.TrySetResult(); }
        (await Record.ExceptionAsync(() => bridge.Completion.WaitAsync(Deadline))).ShouldBeSameAs(clock.Failure);
        first.ReplyCalls.ShouldBe(1);
        first.FailCalls.ShouldBe(0);
        first.Response.ShouldBe("reply");
        rejected.Reads.ShouldBe(0);
        rejected.FailCalls.ShouldBe(1);
        rejected.ReplyCalls.ShouldBe(0);
        rejected.AckCalls.ShouldBe(0);
        rejected.Error.ShouldBeOfType<InvalidOperationException>().Message.ShouldContain("caller limit");
        (await Record.ExceptionAsync(() => bridge.DisposeAsync().AsTask().WaitAsync(Deadline)))
            .ShouldBeSameAs(clock.Failure);
        await bridge.DisposeAsync();
    }

    private sealed class DiagnosticFailureClock : TimeProvider
    {
        private readonly FakeTimeProvider _inner = new();
        public bool FailReads { get; set; }
        public InvalidOperationException Failure { get; } = new("diagnostic timestamp failed");
        public void Advance(TimeSpan elapsed) => _inner.Advance(elapsed);
        public override DateTimeOffset GetUtcNow() => FailReads ? throw Failure : _inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => _inner.CreateTimer(callback, state, dueTime, period);
    }

    private sealed class InvalidPayload(Exception failure)
    {
        public string Value => throw failure;
    }

    private sealed class Context(Func<object> request, CorrelationId? id = null, Task? replyHold = null,
        Action? replied = null, Action? failed = null)
        : IRequestContext<object, string>
    {
        public object Request { get { Reads++; return request(); } }
        public int Reads { get; private set; }
        public CorrelationId? CorrelationId => id;
        public TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReplyEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Response { get; private set; }
        public Exception? Error { get; private set; }
        public int ReplyCalls { get; private set; }
        public int FailCalls { get; private set; }
        public int AckCalls { get; private set; }
        public async Task ReplyAsync(string response, CancellationToken cancellationToken = default)
        {
            ReplyCalls++;
            Response = response;
            replied?.Invoke();
            ReplyEntered.TrySetResult();
            if (replyHold is not null) await replyHold;
            Settled.TrySetResult();
        }
        public Task FailAsync(Exception error, CancellationToken cancellationToken = default)
        { Error = error; FailCalls++; failed?.Invoke(); Settled.TrySetResult(); return Task.CompletedTask; }
        public Task AcknowledgeAsync(CancellationToken cancellationToken = default)
        { AckCalls++; Settled.TrySetResult(); return Task.CompletedTask; }
    }
}
