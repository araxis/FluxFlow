using System.Threading.RateLimiting;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.RateLimit.Tests;

public sealed class RateLimitTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static ConcurrencyLimiter Limiter(int queue = 1) => new(new()
    { PermitLimit = 1, QueueLimit = queue, QueueProcessingOrder = QueueProcessingOrder.OldestFirst });

    [Fact]
    public async Task Lease_is_held_until_reply_and_complete_keeps_reply_open()
    {
        using var limiter = Limiter();
        await using var component = new RateLimit(new() { Limiter = limiter });
        var requests = Sink(component.Request);
        var output = Sink(component.Output);
        var original = FlowMessage.Create((FlowValue)"one", CorrelationId.New());
        await component.Input.SendAsync(original);
        var first = await requests.ReceiveAsync(Deadline);
        await component.Input.SendAsync(FlowMessage.Create((FlowValue)"two"));
        await WaitUntil(() => limiter.GetStatistics()!.CurrentQueuedCount == 1);
        limiter.GetStatistics()!.CurrentAvailablePermits.ShouldBe(0);
        requests.Count.ShouldBe(0);
        component.Complete();
        var reply = first.WithValue((FlowValue)"done");
        await component.Reply.SendAsync(reply);
        var second = await requests.ReceiveAsync(Deadline);
        second.CorrelationId.ShouldNotBe(first.CorrelationId);
        await component.Reply.SendAsync(second.WithValue((FlowValue)new FlowError("test", "failed", "test")));
        var result = await output.ReceiveAsync(Deadline);
        result.CorrelationId.ShouldBe(original.CorrelationId);
        result.CausationId.ShouldBe(reply.MessageId);
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        await component.Completion.WaitAsync(Deadline);
        limiter.GetStatistics()!.CurrentAvailablePermits.ShouldBe(1);
        await component.DisposeAsync();
        using var lease = limiter.AttemptAcquire();
        lease.IsAcquired.ShouldBeTrue(); // the component does not own the limiter
    }

    [Fact]
    public async Task Full_limiter_queue_rejects_without_sending_request()
    {
        using var limiter = Limiter(0);
        await using var component = new RateLimit(new()
        { Limiter = limiter, Execution = new() { MaxDegreeOfParallelism = 2, EnsureOrdered = false } });
        var requests = Sink(component.Request);
        var output = Sink(component.Output);
        await component.Input.SendAsync(FlowMessage.Create((FlowValue)"one"));
        var first = await requests.ReceiveAsync(Deadline);
        await component.Input.SendAsync(FlowMessage.Create((FlowValue)"two"));
        var rejected = await output.ReceiveAsync(Deadline);
        rejected.Error!.Code.ShouldBe("rate_limit_rejected");
        requests.Count.ShouldBe(0);
        await component.Reply.SendAsync(first.WithValue(first.Value));
        await output.ReceiveAsync(Deadline);
        component.Complete();
        await component.Completion.WaitAsync(Deadline);
    }

    [Theory]
    [InlineData("fixed")]
    [InlineData("sliding")]
    [InlineData("token")]
    public async Task Time_based_permit_is_not_returned_by_reply(string kind)
    {
        using RateLimiter limiter = kind switch
        {
            "fixed" => new FixedWindowRateLimiter(new() { PermitLimit = 1, QueueLimit = 0, Window = TimeSpan.FromHours(1), AutoReplenishment = false }),
            "sliding" => new SlidingWindowRateLimiter(new() { PermitLimit = 1, QueueLimit = 0, Window = TimeSpan.FromHours(1), SegmentsPerWindow = 2, AutoReplenishment = false }),
            _ => new TokenBucketRateLimiter(new() { TokenLimit = 1, TokensPerPeriod = 1, QueueLimit = 0, ReplenishmentPeriod = TimeSpan.FromHours(1), AutoReplenishment = false })
        };
        await using var component = new RateLimit(new() { Limiter = limiter });
        var requests = Sink(component.Request);
        var output = Sink(component.Output);
        await component.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        var request = await requests.ReceiveAsync(Deadline);
        await component.Reply.SendAsync(request.WithValue((FlowValue)"ok"));
        await output.ReceiveAsync(Deadline);
        await component.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        component.Complete();
        var rejected = await output.ReceiveAsync(Deadline);
        rejected.Error!.Code.ShouldBe("rate_limit_rejected");
        if (kind == "sliding") rejected.Error.Details.ShouldBeNull();
        else rejected.Error.Details!.Value.GetProperty("retryAfterSeconds").GetDouble().ShouldBeGreaterThan(0);
        await component.Completion.WaitAsync(Deadline);
        requests.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Cancellation_releases_active_lease_and_cancels_queue()
    {
        using var limiter = Limiter();
        using var cancellation = new CancellationTokenSource();
        await using var component = new RateLimit(new()
        { Limiter = limiter, Execution = new() { CancellationToken = cancellation.Token, MaxDegreeOfParallelism = 2 } });
        var requests = Sink(component.Request);
        await component.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await requests.ReceiveAsync(Deadline);
        await component.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await WaitUntil(() => limiter.GetStatistics()!.CurrentQueuedCount == 1);
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => component.Completion.WaitAsync(Deadline));
        limiter.GetStatistics()!.CurrentAvailablePermits.ShouldBe(1);
        limiter.GetStatistics()!.CurrentQueuedCount.ShouldBe(0);
    }

    [Fact]
    public async Task Timeout_releases_lease_and_late_reply_does_not_resolve_next_exchange()
    {
        using var limiter = Limiter();
        await using var component = new RateLimit(new() { Limiter = limiter, ResponseTimeout = TimeSpan.FromMilliseconds(300) });
        var requests = Sink(component.Request);
        var output = Sink(component.Output);
        await component.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        var first = await requests.ReceiveAsync(Deadline);
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        limiter.GetStatistics()!.CurrentAvailablePermits.ShouldBe(1);
        await component.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        var second = await requests.ReceiveAsync(Deadline);
        await component.Reply.SendAsync(first.WithValue((FlowValue)"stale"));
        await component.Reply.SendAsync(second.WithValue((FlowValue)"current"));
        (await output.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("current");
        component.Complete();
        await component.Completion.WaitAsync(Deadline);
    }

    private static BufferBlock<FlowMessage> Sink(ISourceBlock<FlowMessage> port)
    {
        var sink = new BufferBlock<FlowMessage>();
        port.LinkTo(sink, new() { PropagateCompletion = true });
        return sink;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        while (!condition()) await Task.Delay(1, cancellation.Token);
    }
}
