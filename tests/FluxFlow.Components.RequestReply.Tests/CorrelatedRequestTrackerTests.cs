using FluxFlow.Components.RequestReply;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.RequestReply.Tests;

public sealed class CorrelatedRequestTrackerTests
{
    [Fact]
    public async Task Complete_MatchesByCorrelationId_AndEvicts()
    {
        var completed = new TaskCompletionSource<CompletedRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var tracker = new CorrelatedRequestTracker<string, string>(
            (correlationId, context, response, _) =>
            {
                completed.TrySetResult(new CompletedRequest(correlationId, context, response.Payload));
                return ValueTask.CompletedTask;
            },
            (_, _, _, _) => ValueTask.CompletedTask);
        var id = new CorrelationId("trace-1");

        tracker.TryAdd(id, "request-context").ShouldBe(CorrelatedRequestStartResult.Accepted);
        (await tracker.TryCompleteAsync(FlowMessage.Create("response", id))).ShouldBeTrue();

        var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        result.CorrelationId.ShouldBe(id);
        result.Context.ShouldBe("request-context");
        result.Response.ShouldBe("response");
        tracker.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Complete_ReturnsFalse_WhenResponseIsUnmatched()
    {
        await using var tracker = new CorrelatedRequestTracker<string, string>(
            (_, _, _, _) => ValueTask.CompletedTask,
            (_, _, _, _) => ValueTask.CompletedTask);

        (await tracker.TryCompleteAsync(FlowMessage.Create(
            "response",
            new CorrelationId("missing")))).ShouldBeFalse();
    }

    [Fact]
    public async Task Add_RejectsDuplicateCorrelationId()
    {
        await using var tracker = new CorrelatedRequestTracker<string, string>(
            (_, _, _, _) => ValueTask.CompletedTask,
            (_, _, _, _) => ValueTask.CompletedTask);
        var id = new CorrelationId("trace-1");

        tracker.TryAdd(id, "first").ShouldBe(CorrelatedRequestStartResult.Accepted);
        tracker.TryAdd(id, "second").ShouldBe(CorrelatedRequestStartResult.DuplicateCorrelationId);
        tracker.PendingCount.ShouldBe(1);
    }

    [Fact]
    public async Task Timeout_FailsPendingRequest_AndEvicts()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-06-20T00:00:00+00:00"));
        var failed = new TaskCompletionSource<FailedRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var tracker = new CorrelatedRequestTracker<string, string>(
            (_, _, _, _) => ValueTask.CompletedTask,
            (correlationId, context, error, _) =>
            {
                failed.TrySetResult(new FailedRequest(correlationId, context, error));
                return ValueTask.CompletedTask;
            },
            new CorrelatedRequestTrackerOptions
            {
                Timeout = TimeSpan.FromMilliseconds(200),
                SweepInterval = TimeSpan.FromMilliseconds(100)
            },
            clock);
        var id = new CorrelationId("slow");

        tracker.TryAdd(id, "request-context").ShouldBe(CorrelatedRequestStartResult.Accepted);
        clock.Advance(TimeSpan.FromMilliseconds(300));

        var result = await failed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        result.CorrelationId.ShouldBe(id);
        result.Context.ShouldBe("request-context");
        result.Error.ShouldBeOfType<TimeoutException>();
        tracker.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Dispose_FailsPendingRequests_AndStopsNewRequests()
    {
        var failed = new TaskCompletionSource<FailedRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = new CorrelatedRequestTracker<string, string>(
            (_, _, _, _) => ValueTask.CompletedTask,
            (correlationId, context, error, _) =>
            {
                failed.TrySetResult(new FailedRequest(correlationId, context, error));
                return ValueTask.CompletedTask;
            });
        var id = new CorrelationId("pending");

        tracker.TryAdd(id, "request-context").ShouldBe(CorrelatedRequestStartResult.Accepted);
        await tracker.DisposeAsync();

        var result = await failed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        result.CorrelationId.ShouldBe(id);
        result.Context.ShouldBe("request-context");
        result.Error.ShouldBeOfType<OperationCanceledException>();
        tracker.PendingCount.ShouldBe(0);
        tracker.TryAdd(new CorrelationId("after"), "after-context")
            .ShouldBe(CorrelatedRequestStartResult.Stopped);
    }

    [Fact]
    public void Options_reject_invalid_values_when_assigned()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new CorrelatedRequestTrackerOptions
        {
            Timeout = TimeSpan.Zero
        }).Message.ShouldContain("Timeout");
        Should.Throw<ArgumentOutOfRangeException>(() => new CorrelatedRequestTrackerOptions
        {
            SweepInterval = TimeSpan.Zero
        }).Message.ShouldContain("Sweep interval");
    }

    private sealed record CompletedRequest(
        CorrelationId CorrelationId,
        string Context,
        string Response);

    private sealed record FailedRequest(
        CorrelationId CorrelationId,
        string Context,
        Exception Error);
}
