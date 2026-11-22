using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.RateLimit.Tests;

public sealed class RequestReplyTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private static RateLimit Create(int concurrency = 1, int buffer = 1, TimeSpan? timeout = null,
        bool stopOnTimeout = true, CancellationToken cancellation = default) => new(new()
    {
        Limiter = new ConcurrencyLimiter(new() { PermitLimit = concurrency, QueueLimit = 0 }), OwnsLimiter = true,
        PendingCapacity = concurrency, StopDispatchOnTimeout = stopOnTimeout,
        EmitRequestReplyResults = true,
        ResponseTimeout = timeout ?? Deadline,
        Execution = new() { BoundedCapacity = concurrency + buffer, MaxDegreeOfParallelism = concurrency,
            EnsureOrdered = false, CancellationToken = cancellation }
    });

    [Fact]
    public async Task Concurrent_feedback_matches_requests_and_preserves_outer_correlation_with_bounded_admission()
    {
        await using var component = Create(2);
        var requests = Sink(component.Request); var results = Sink(component.Output);
        var one = FlowMessage.Create((FlowValue)"one", CorrelationId.New());
        var two = FlowMessage.Create((FlowValue)"two", CorrelationId.New());
        await component.Input.SendAsync(one);
        var first = await requests.ReceiveAsync(Deadline);
        await component.Input.SendAsync(two);
        var second = await requests.ReceiveAsync(Deadline);
        await component.Input.SendAsync(FlowMessage.Create((FlowValue)"three"));
        var fourthAdmission = component.Input.SendAsync(FlowMessage.Create((FlowValue)"four"));
        fourthAdmission.IsCompleted.ShouldBeFalse();
        requests.Count.ShouldBe(0);

        await component.Nack.SendAsync(second.WithValue((FlowValue)"unsupported"));
        var rejected = await results.ReceiveAsync(Deadline);
        rejected.CorrelationId.ShouldBe(two.CorrelationId);
        Result(rejected, RequestReplyOutcome.NACK).Reply!.Json.GetString().ShouldBe("unsupported");
        Result(rejected, RequestReplyOutcome.NACK).Request.ShouldBeSameAs(two.Value);
        (await fourthAdmission.WaitAsync(Deadline)).ShouldBeTrue();
        var third = await requests.ReceiveAsync(Deadline);
        third.Value.Json.GetString().ShouldBe("three");
        // A duplicate cannot free another slot while the first and third requests are active.
        await component.Reply.SendAsync(second.WithValue((FlowValue)"duplicate"));
        await component.Reply.SendAsync(first.WithValue((FlowValue)"done"));
        var completed = await results.ReceiveAsync(Deadline);
        completed.CorrelationId.ShouldBe(one.CorrelationId);
        Result(completed, RequestReplyOutcome.ACK).Reply!.Json.GetString().ShouldBe("done");
        var fourth = await requests.ReceiveAsync(Deadline);
        fourth.Value.Json.GetString().ShouldBe("four");
        var error = new FlowError("specific", "Keep this reason", "worker", details: (FlowValue)"details");
        await component.Nack.SendAsync(third.WithValue(error));
        Result(await results.ReceiveAsync(Deadline), RequestReplyOutcome.NACK).Reply.ShouldBeSameAs(error);
        await component.Reply.SendAsync(fourth.WithValue((FlowValue)"last"));
        Result(await results.ReceiveAsync(Deadline), RequestReplyOutcome.ACK).Reply!.Json.GetString().ShouldBe("last");
        component.Complete();
        await component.Completion.WaitAsync(Deadline);
        results.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Timeout_policy_is_explicit_and_late_feedback_cannot_complete_next_request(bool stop)
    {
        await using var component = Create(timeout: TimeSpan.FromMilliseconds(250), stopOnTimeout: stop);
        var requests = Sink(component.Request); var results = Sink(component.Output);
        var original = FlowMessage.Create((FlowValue)"one", CorrelationId.New());
        var queued = FlowMessage.Create((FlowValue)"two", CorrelationId.New());
        await component.Input.SendAsync(original);
        var first = await requests.ReceiveAsync(Deadline);
        await component.Input.SendAsync(queued);
        var expiredMessage = await results.ReceiveAsync(Deadline);
        var expired = Result(expiredMessage, RequestReplyOutcome.Timeout);
        expiredMessage.CorrelationId.ShouldBe(original.CorrelationId);
        expiredMessage.TraceId.ShouldBe(original.TraceId);
        expired.RequestId.ShouldBe(original.MessageId.ToString());
        expired.Request.ShouldBeSameAs(original.Value);
        expired.Reply.ShouldBeNull();
        expired.Request.Json.GetString().ShouldBe("one");
        expired.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
        if (stop)
        {
            var notDispatchedMessage = await results.ReceiveAsync(Deadline);
            var notDispatched = Result(notDispatchedMessage, RequestReplyOutcome.NotDispatched);
            notDispatchedMessage.CorrelationId.ShouldBe(queued.CorrelationId);
            notDispatchedMessage.TraceId.ShouldBe(queued.TraceId);
            notDispatched.RequestId.ShouldBe(queued.MessageId.ToString());
            notDispatched.Request.ShouldBeSameAs(queued.Value);
            notDispatched.Request.Json.GetString().ShouldBe("two");
            notDispatched.Reply.ShouldBeNull();
            notDispatched.Elapsed.ShouldBe(TimeSpan.Zero);
            requests.Count.ShouldBe(0);
            await component.Reply.SendAsync(first.WithValue((FlowValue)"late"));
            await component.Input.SendAsync(FlowMessage.Create((FlowValue)"three"));
            Result(await results.ReceiveAsync(Deadline), RequestReplyOutcome.NotDispatched);
        }
        else
        {
            var second = await requests.ReceiveAsync(Deadline);
            await component.Nack.SendAsync(first.WithValue((FlowValue)"late"));
            await component.Reply.SendAsync(second.WithValue((FlowValue)"current"));
            Result(await results.ReceiveAsync(Deadline), RequestReplyOutcome.ACK).Reply!.Json.GetString().ShouldBe("current");
        }
        component.Complete();
        await component.Completion.WaitAsync(Deadline);
        results.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Cancellation_closes_pending_feedback_and_postponed_admission()
    {
        using var cancellation = new CancellationTokenSource();
        await using var component = Create(buffer: 0, cancellation: cancellation.Token);
        var requests = Sink(component.Request);
        var results = Sink(component.Output);
        await component.Input.SendAsync(FlowMessage.Create((FlowValue)"one"));
        var first = await requests.ReceiveAsync(Deadline);
        var pending = component.Input.SendAsync(FlowMessage.Create((FlowValue)"two"));
        pending.IsCompleted.ShouldBeFalse();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => component.Completion.WaitAsync(Deadline));
        (await pending.WaitAsync(Deadline)).ShouldBeFalse();
        (await component.Reply.SendAsync(first)).ShouldBeFalse();
        (await component.Nack.SendAsync(first)).ShouldBeFalse();
        results.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Competing_ACK_and_NACK_complete_each_request_once()
    {
        await using var component = Create(4, buffer: 4);
        var requests = Sink(component.Request);
        var results = Sink(component.Output);
        var originals = Enumerable.Range(0, 32)
            .Select(index => FlowMessage.Create((FlowValue)index.ToString(), CorrelationId.New())).ToArray();
        var admission = Task.WhenAll(originals.Select(message => component.Input.SendAsync(message)));
        for (var index = 0; index < originals.Length; index++)
        {
            var request = await requests.ReceiveAsync(Deadline);
            // Feedback without this request's correlation must not resolve it.
            await component.Reply.SendAsync(FlowMessage.Create((FlowValue)"unrelated"));
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ack = Task.Run(async () => { await start.Task; return await component.Reply.SendAsync(request.WithValue((FlowValue)"accepted")); });
            var nack = Task.Run(async () => { await start.Task; return await component.Nack.SendAsync(request.WithValue((FlowValue)"rejected")); });
            start.SetResult();
            (await Task.WhenAll(ack, nack).WaitAsync(Deadline)).ShouldAllBe(accepted => accepted);
        }
        (await admission.WaitAsync(Deadline)).ShouldAllBe(accepted => accepted);
        component.Complete();
        await component.Completion.WaitAsync(Deadline);
        var completed = new List<FlowMessage>();
        while (results.TryReceive(out var result)) completed.Add(result);
        completed.Count.ShouldBe(originals.Length);
        completed.Select(message => message.CorrelationId).ShouldBe(originals.Select(message => message.CorrelationId), ignoreOrder: true);
        foreach (var result in completed)
        {
            result.IsError.ShouldBeFalse();
            var outcome = result.Value.ShouldBeOfType<RequestReplyResult>();
            outcome.Outcome.ShouldBeOneOf(RequestReplyOutcome.ACK, RequestReplyOutcome.NACK);
            outcome.Reply!.Json.GetString().ShouldBe(outcome.Outcome == RequestReplyOutcome.ACK ? "accepted" : "rejected");
        }
    }

    [Fact]
    public async Task Complete_drains_accepted_work_with_ACK_and_NACK_then_closes_feedback()
    {
        await using var component = Create(2, buffer: 1);
        var requests = Sink(component.Request);
        var results = Sink(component.Output);
        await component.Input.SendAsync(FlowMessage.Create((FlowValue)"one"));
        var first = await requests.ReceiveAsync(Deadline);
        await component.Input.SendAsync(FlowMessage.Create((FlowValue)"two"));
        var second = await requests.ReceiveAsync(Deadline);
        await component.Input.SendAsync(FlowMessage.Create((FlowValue)"queued"));
        component.Complete();
        (await component.Input.SendAsync(FlowMessage.Create((FlowValue)"after stop"))).ShouldBeFalse();
        component.Completion.IsCompleted.ShouldBeFalse();
        (await component.Nack.SendAsync(second.WithValue((FlowValue)"rejected"))).ShouldBeTrue();
        Result(await results.ReceiveAsync(Deadline), RequestReplyOutcome.NACK);
        var queued = await requests.ReceiveAsync(Deadline);
        queued.Value.Json.GetString().ShouldBe("queued");
        (await component.Reply.SendAsync(first.WithValue((FlowValue)"done"))).ShouldBeTrue();
        Result(await results.ReceiveAsync(Deadline), RequestReplyOutcome.ACK).Reply!.Json.GetString().ShouldBe("done");
        (await component.Reply.SendAsync(queued.WithValue((FlowValue)"drained"))).ShouldBeTrue();
        Result(await results.ReceiveAsync(Deadline), RequestReplyOutcome.ACK).Reply!.Json.GetString().ShouldBe("drained");
        await component.Completion.WaitAsync(Deadline);
        (await component.Reply.SendAsync(first)).ShouldBeFalse();
        (await component.Nack.SendAsync(second)).ShouldBeFalse();
        requests.Count.ShouldBe(0);
        results.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Internal_failure_remains_an_engine_error()
    {
        using var limiter = new ConcurrencyLimiter(new() { PermitLimit = 1, QueueLimit = 0 });
        await using var component = new RateLimit(new() { Limiter = limiter, EmitRequestReplyResults = true });
        var results = Sink(component.Output);
        limiter.Dispose();
        await component.Input.SendAsync(FlowMessage.Create((FlowValue)"work"));
        (await results.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        component.Complete();
        await component.Completion.WaitAsync(Deadline);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Feedback_port_decides_outcome_and_preserves_payload_and_message_context(bool nack, bool errorReply)
    {
        await using var component = Create();
        var requests = Sink(component.Request);
        var results = Sink(component.Output);
        var requestValue = new FlowError("input", "Handled by this workflow", "upstream", details: (FlowValue)"original");
        var original = FlowMessage.Create(requestValue, CorrelationId.New());
        await component.Input.SendAsync(original);
        var request = await requests.ReceiveAsync(Deadline);
        request.Value.ShouldBeSameAs(requestValue);
        FlowValue value = errorReply
            ? new FlowError("worker", "Keep this error as data", "worker", details: (FlowValue)"complete details")
            : new JsonFlowValue(JsonSerializer.SerializeToElement(new { Bytes = new byte[] { 0, 255, 128, 7 }, Text = "payload" }));
        var feedback = request.WithValue(value, new Dictionary<string, FlowValue> { ["custom"] = "preserve" });
        (await (nack ? component.Nack : component.Reply).SendAsync(feedback)).ShouldBeTrue();
        var output = await results.ReceiveAsync(Deadline);
        var result = Result(output, nack ? RequestReplyOutcome.NACK : RequestReplyOutcome.ACK);
        result.RequestId.ShouldBe(original.MessageId.ToString());
        result.Request.ShouldBeSameAs(requestValue);
        result.Reply.ShouldBeSameAs(value);
        result.Json.GetProperty("Request").GetRawText().ShouldBe(requestValue.Json.GetRawText());
        JsonNode.DeepEquals(JsonNode.Parse(result.Json.GetProperty("Reply").GetRawText()),
            JsonNode.Parse(value.Json.GetRawText())).ShouldBeTrue();
        if (!errorReply)
            result.Json.GetProperty("Reply").GetProperty("Bytes").GetBytesFromBase64().ShouldBe(new byte[] { 0, 255, 128, 7 });
        output.CorrelationId.ShouldBe(original.CorrelationId);
        output.TraceId.ShouldBe(original.TraceId);
        output.CausationId.ShouldBe(feedback.MessageId);
        output.Headers["custom"].Json.GetString().ShouldBe("preserve");
        component.Complete();
        await component.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Null_feedback_is_an_ACK_or_NACK_result_not_a_timeout()
    {
        await using var component = Create();
        var requests = Sink(component.Request);
        var results = Sink(component.Output);
        foreach (var outcome in new[] { RequestReplyOutcome.ACK, RequestReplyOutcome.NACK })
        {
            await component.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
            var request = await requests.ReceiveAsync(Deadline);
            await (outcome == RequestReplyOutcome.ACK ? component.Reply : component.Nack)
                .SendAsync(request.WithValue(JsonFlowValue.Null));
            var result = Result(await results.ReceiveAsync(Deadline), outcome);
            result.Request.ShouldBeSameAs(JsonFlowValue.Null);
            result.Reply.ShouldBeSameAs(JsonFlowValue.Null);
            result.Json.GetProperty("Reply").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        component.Complete();
        await component.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Private_limiter_is_disposed_after_completion()
    {
        var limiter = new ConcurrencyLimiter(new() { PermitLimit = 1, QueueLimit = 0 });
        var component = new RateLimit(new() { Limiter = limiter, OwnsLimiter = true });
        component.Complete();
        await component.Completion.WaitAsync(Deadline);
        await component.DisposeAsync();
        Should.Throw<ObjectDisposedException>(() => limiter.AttemptAcquire());
    }

    private static BufferBlock<FlowMessage> Sink(ISourceBlock<FlowMessage> source)
    {
        var sink = new BufferBlock<FlowMessage>();
        source.LinkTo(sink, new() { PropagateCompletion = true });
        return sink;
    }

    private static RequestReplyResult Result(FlowMessage message, RequestReplyOutcome outcome)
    {
        message.IsError.ShouldBeFalse();
        var result = message.Value.ShouldBeOfType<RequestReplyResult>();
        result.Outcome.ShouldBe(outcome);
        result.Json.GetProperty("Outcome").GetString().ShouldBe(outcome.ToString());
        var restored = JsonSerializer.Deserialize<FlowMessage>(JsonSerializer.Serialize(message))!;
        restored.IsError.ShouldBeFalse();
        restored.CorrelationId.ShouldBe(message.CorrelationId);
        restored.TraceId.ShouldBe(message.TraceId);
        restored.CausationId.ShouldBe(message.CausationId);
        JsonNode.DeepEquals(JsonNode.Parse(restored.Value.Json.GetRawText()), JsonNode.Parse(result.Json.GetRawText())).ShouldBeTrue();
        return result;
    }
}
