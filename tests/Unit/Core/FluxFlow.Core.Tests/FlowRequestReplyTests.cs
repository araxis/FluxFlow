using System.Text.Json;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class FlowRequestReplyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Inline_reply_is_registered_before_admission_and_preserves_owned_payload()
    {
        await using var exchanges = new FlowRequestReply();
        var original = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { Value = "owned" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        FlowMessage? sent = null;
        FlowMessage? reply = null;

        var result = await exchanges.RequestAsync("reply", original, (message, _) =>
        {
            sent = message;
            reply = message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("received", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            exchanges.Reply("reply", reply).ShouldBeTrue();
            return ValueTask.CompletedTask;
        }, Timeout);

        result.ShouldBeSameAs(reply);
        sent.ShouldNotBeNull().Value.ShouldBeSameAs(original.Value);
        sent.Headers.ShouldBeSameAs(original.Headers);
        sent.MessageId.ShouldBe(original.MessageId);
        sent.TraceId.ShouldBe(original.TraceId);
        sent.CorrelationId.ShouldNotBeNull().IsEmpty.ShouldBeFalse();
        sent.CorrelationId.ShouldNotBe(original.CorrelationId);
        exchanges.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_requests_sharing_a_trace_match_only_their_exchange_and_reply_address()
    {
        await using var exchanges = new FlowRequestReply(capacity: 2);
        var original = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("request", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        FlowMessage? firstSent = null;
        FlowMessage? secondSent = null;
        var first = exchanges.RequestAsync("reply", original, (message, _) =>
        {
            firstSent = message;
            return ValueTask.CompletedTask;
        }, Timeout);
        var second = exchanges.RequestAsync("reply", original, (message, _) =>
        {
            secondSent = message;
            return ValueTask.CompletedTask;
        }, Timeout);
        var firstReply = firstSent.ShouldNotBeNull().With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var secondReply = secondSent.ShouldNotBeNull().With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(2, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        firstReply.TraceId.ShouldBe(secondReply.TraceId);
        firstReply.CorrelationId.ShouldNotBe(secondReply.CorrelationId);
        exchanges.PendingCount.ShouldBe(2);

        exchanges.Reply("wrong", firstReply).ShouldBeFalse();
        exchanges.Reply("reply", original.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("unrelated", new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ShouldBeFalse();
        first.IsCompleted.ShouldBeFalse();
        second.IsCompleted.ShouldBeFalse();
        exchanges.Reply("reply", secondReply).ShouldBeTrue();
        (await second.WaitAsync(Timeout)).ShouldBeSameAs(secondReply);
        exchanges.Reply("reply", secondReply).ShouldBeFalse();
        first.IsCompleted.ShouldBeFalse();
        exchanges.PendingCount.ShouldBe(1);
        exchanges.Reply("reply", firstReply).ShouldBeTrue();

        (await first.WaitAsync(Timeout)).ShouldBeSameAs(firstReply);
        exchanges.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Admission_failure_preserves_the_original_exception_and_releases_capacity()
    {
        await using var exchanges = new FlowRequestReply(capacity: 1);
        var failure = new InvalidOperationException("Admission failed.");
        var request = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        var observed = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await exchanges.RequestAsync("reply", request, (_, _) => throw failure, Timeout));

        observed.ShouldBeSameAs(failure);
        exchanges.PendingCount.ShouldBe(0);
        var result = await exchanges.RequestAsync("reply", request, (message, _) =>
        {
            exchanges.Reply("reply", message);
            return ValueTask.CompletedTask;
        }, Timeout);
        result.Value.ShouldBeSameAs(request.Value);
    }

    [Fact]
    public async Task Deadline_cancels_and_joins_blocked_admission_without_leaving_a_pending_send()
    {
        var clock = new FakeTimeProvider();
        await using var exchanges = new FlowRequestReply(timeProvider: clock);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = exchanges.RequestAsync("reply", FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), async (_, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token); }
            finally { exited.TrySetResult(); }
        }, Timeout);
        await entered.Task.WaitAsync(Timeout);

        clock.Advance(Timeout);

        await Should.ThrowAsync<TimeoutException>(async () => await request.WaitAsync(Timeout));
        exited.Task.IsCompletedSuccessfully.ShouldBeTrue();
        exchanges.PendingCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Infinite_request_stays_pending_until_caller_cancellation_or_service_stop(bool stop)
    {
        var clock = new FakeTimeProvider();
        await using var exchanges = new FlowRequestReply(timeProvider: clock);
        using var cancellation = new CancellationTokenSource();
        var request = exchanges.RequestAsync("reply", FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))),
            (_, _) => ValueTask.CompletedTask, System.Threading.Timeout.InfiniteTimeSpan, cancellation.Token);

        clock.Advance(TimeSpan.FromDays(365));
        request.IsCompleted.ShouldBeFalse();
        exchanges.PendingCount.ShouldBe(1);
        if (stop) exchanges.Stop();
        else cancellation.Cancel();

        var failure = await Should.ThrowAsync<OperationCanceledException>(async () => await request.WaitAsync(Timeout));
        if (!stop) failure.CancellationToken.ShouldBe(cancellation.Token);
        exchanges.PendingCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timer_failure_cancels_and_joins_all_admitted_requests(bool duringCallback)
    {
        var clock = new FailingSchedulingTimeProvider();
        var exchanges = new FlowRequestReply(capacity: 3, timeProvider: clock);
        var exits = Enumerable.Range(0, 2)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var senders = 0;
        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("request", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var requests = Enumerable.Range(0, 2).Select(index => exchanges.RequestAsync("reply", message, async (_, token) =>
        {
            Interlocked.Increment(ref senders);
            try { await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token); }
            finally { exits[index].TrySetResult(); }
        }, TimeSpan.FromSeconds(index == 0 ? 1 : 5))).ToArray();
        senders.ShouldBe(2);
        exits.ShouldAllBe(exit => !exit.Task.IsCompleted);
        var failure = new InvalidOperationException("Scheduling failed.");
        clock.Failure = failure;
        var additionalSenders = 0;

        if (duringCallback)
            clock.Advance(TimeSpan.FromSeconds(1));
        else
        {
            var rejected = exchanges.RequestAsync("reply", message, (_, _) =>
            {
                additionalSenders++;
                return ValueTask.CompletedTask;
            }, Timeout);
            (await Should.ThrowAsync<InvalidOperationException>(() => rejected.WaitAsync(Timeout))).ShouldBeSameAs(failure);
        }

        if (duringCallback)
            await Should.ThrowAsync<TimeoutException>(() => requests[0].WaitAsync(Timeout));
        else
            (await Should.ThrowAsync<InvalidOperationException>(() => requests[0].WaitAsync(Timeout))).ShouldBeSameAs(failure);
        (await Should.ThrowAsync<InvalidOperationException>(() => requests[1].WaitAsync(Timeout))).ShouldBeSameAs(failure);
        exits.ShouldAllBe(exit => exit.Task.IsCompletedSuccessfully);
        exchanges.PendingCount.ShouldBe(0);
        await Should.ThrowAsync<OperationCanceledException>(() => exchanges.RequestAsync("reply", message, (_, _) =>
        {
            additionalSenders++;
            return ValueTask.CompletedTask;
        }, Timeout));
        additionalSenders.ShouldBe(0);
        (await Should.ThrowAsync<InvalidOperationException>(() => exchanges.DisposeAsync().AsTask())).ShouldBeSameAs(failure);
        await exchanges.DisposeAsync();
    }

    private sealed class FailingSchedulingTimeProvider : FakeTimeProvider
    {
        public Exception? Failure { get; set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new FailingTimer(this, base.CreateTimer(callback, state, dueTime, period));

        private sealed class FailingTimer(FailingSchedulingTimeProvider owner, ITimer inner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (owner.Failure is { } failure) throw failure;
                return inner.Change(dueTime, period);
            }

            public void Dispose() => inner.Dispose();
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    [Fact]
    public async Task Stopped_service_rejects_new_requests_without_invoking_the_sender()
    {
        await using var exchanges = new FlowRequestReply();
        exchanges.Stop();
        var calls = 0;

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await exchanges.RequestAsync("reply", FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), (_, _) =>
            {
                calls++;
                return ValueTask.CompletedTask;
            }, Timeout));

        calls.ShouldBe(0);
        exchanges.PendingCount.ShouldBe(0);
    }
}
