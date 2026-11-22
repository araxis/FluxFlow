using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Polly;
using Polly.Retry;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Resilience.Tests;

public sealed class RetryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static FlowValue Error(bool transient = true) => new FlowError("failed", "Test failure", "test", transient);
    private static RetryStrategyOptions<FlowMessage> RetryPolicy() => new()
    {
        MaxRetryAttempts = 2, Delay = TimeSpan.Zero,
        ShouldHandle = args => ValueTask.FromResult(args.Outcome.Result?.Error?.IsTransient == true)
    };

    [Fact]
    public async Task Ports_retry_original_payload_and_keep_reply_open_during_completion()
    {
        await using var retry = new Retry(new() { RetryPolicy = RetryPolicy() });
        var requests = Requests(retry);
        var output = Output(retry);
        var original = FlowMessage.Create((FlowValue)"original", CorrelationId.New(),
            headers: new Dictionary<string, FlowValue> { ["key"] = "stable" });
        await retry.Input.SendAsync(original);
        retry.Complete();
        var attempts = new List<FlowMessage>();
        FlowMessage? final = null;
        for (var i = 0; i < 3; i++)
        {
            var attempt = await requests.ReceiveAsync(Deadline);
            attempts.Add(attempt);
            attempt.Value.ShouldBeSameAs(original.Value);
            attempt.TraceId.ShouldBe(original.TraceId);
            attempt.CausationId.ShouldBe(original.MessageId);
            attempt.Headers["key"].ShouldBeSameAs(original.Headers["key"]);
            final = attempt.WithValue(i < 2 ? Error() : (FlowValue)"success");
            (await retry.Reply.SendAsync(final)).ShouldBeTrue();
        }
        var result = await output.ReceiveAsync(Deadline);
        result.Value.Json.GetString().ShouldBe("success");
        result.CorrelationId.ShouldBe(original.CorrelationId);
        result.CausationId.ShouldBe(final!.MessageId);
        attempts.Select(x => x.MessageId).Distinct().Count().ShouldBe(3);
        attempts.Select(x => x.CorrelationId).Distinct().Count().ShouldBe(3);
        await retry.Completion.WaitAsync(Deadline);
        await retry.Reply.Completion.WaitAsync(Deadline);
        output.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 1)]
    public async Task Exhaustion_and_non_retryable_results_return_final_error(bool transient, int count)
    {
        await using var retry = new Retry(new() { RetryPolicy = RetryPolicy() });
        var requests = Requests(retry);
        var output = Output(retry);
        var error = Error(transient);
        await retry.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        retry.Complete();
        for (var i = 0; i < count; i++)
            await retry.Reply.SendAsync((await requests.ReceiveAsync(Deadline)).WithValue(error));
        (await output.ReceiveAsync(Deadline)).Value.ShouldBeSameAs(error);
        await retry.Completion.WaitAsync(Deadline);
        requests.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Timed_out_attempt_late_reply_does_not_resolve_next_attempt()
    {
        await using var retry = new Retry(new()
        {
            AttemptTimeout = TimeSpan.FromMilliseconds(300),
            RetryPolicy = new() { MaxRetryAttempts = 1, Delay = TimeSpan.Zero,
                ShouldHandle = new PredicateBuilder<FlowMessage>().Handle<TimeoutException>() }
        });
        var requests = Requests(retry);
        var output = Output(retry);
        await retry.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        retry.Complete();
        var first = await requests.ReceiveAsync(Deadline);
        var second = await requests.ReceiveAsync(Deadline);
        second.CorrelationId.ShouldNotBe(first.CorrelationId);
        await retry.Reply.SendAsync(first.WithValue((FlowValue)"stale"));
        await retry.Reply.SendAsync(second.WithValue((FlowValue)"current"));
        (await output.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("current");
        await retry.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Incoming_error_bypasses_and_timeout_does_not_stop_next_message()
    {
        await using var retry = new Retry(new() { AttemptTimeout = TimeSpan.FromMilliseconds(300) });
        var requests = Requests(retry);
        var output = Output(retry);
        var incoming = FlowMessage.Create(Error());
        await retry.Input.SendAsync(incoming);
        (await output.ReceiveAsync(Deadline)).ShouldBeSameAs(incoming);
        await retry.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await requests.ReceiveAsync(Deadline);
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        await retry.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        retry.Complete();
        await retry.Reply.SendAsync((await requests.ReceiveAsync(Deadline)).WithValue((FlowValue)"ok"));
        (await output.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("ok");
        await retry.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Cancellation_interrupts_retry_delay_and_closes_reply()
    {
        using var stopped = new CancellationTokenSource();
        var retrying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var policy = RetryPolicy();
        policy.Delay = TimeSpan.FromMinutes(1);
        policy.OnRetry = _ => { retrying.TrySetResult(); return ValueTask.CompletedTask; };
        await using var retry = new Retry(new() { Execution = new() { CancellationToken = stopped.Token }, RetryPolicy = policy });
        var requests = Requests(retry);
        await retry.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await retry.Reply.SendAsync((await requests.ReceiveAsync(Deadline)).WithValue(Error()));
        await retrying.Task.WaitAsync(Deadline);
        stopped.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => retry.Completion.WaitAsync(Deadline));
        retry.Reply.Completion.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Concurrent_requests_accept_out_of_order_replies_and_keep_input_order()
    {
        await using var retry = new Retry(new()
        { Execution = new() { MaxDegreeOfParallelism = 2, BoundedCapacity = 2 }, RetryPolicy = RetryPolicy() });
        var requests = Requests(retry);
        var output = Output(retry);
        await retry.Input.SendAsync(FlowMessage.Create((FlowValue)"one"));
        await retry.Input.SendAsync(FlowMessage.Create((FlowValue)"two"));
        retry.Complete();
        var a = await requests.ReceiveAsync(Deadline);
        var b = await requests.ReceiveAsync(Deadline);
        await retry.Reply.SendAsync(b.WithValue(b.Value));
        await retry.Reply.SendAsync(a.WithValue(Error()));
        var again = await requests.ReceiveAsync(Deadline);
        again.Value.ShouldBeSameAs(a.Value);
        await retry.Reply.SendAsync(again.WithValue(again.Value));
        (await output.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("one");
        (await output.ReceiveAsync(Deadline)).Value.Json.GetString().ShouldBe("two");
        await retry.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public void Invalid_options_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new Retry(null!));
        Should.Throw<ArgumentNullException>(() => new Retry(new() { RetryPolicy = null! }));
        Should.Throw<ArgumentOutOfRangeException>(() => new Retry(new() { AttemptTimeout = TimeSpan.Zero }));
        Should.Throw<ArgumentOutOfRangeException>(() => new Retry(new() { OutputCapacity = 0 }))
            .ParamName.ShouldBe(nameof(RetryOptions.OutputCapacity));
    }

    [Fact]
    public async Task Precanceled_construction_initializes_all_ports_before_lifetime_observation()
    {
        using var stopped = new CancellationTokenSource();
        stopped.Cancel();
        await using var retry = new Retry(new() { Execution = new() { CancellationToken = stopped.Token } });
        await Should.ThrowAsync<OperationCanceledException>(() => retry.Completion.WaitAsync(Deadline));
        retry.Input.Completion.IsCompleted.ShouldBeTrue();
        retry.Request.Completion.IsFaulted.ShouldBeTrue();
        retry.Reply.Completion.IsFaulted.ShouldBeTrue();
        retry.Output.Completion.IsFaulted.ShouldBeTrue();
        retry.Events.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    private static BufferBlock<FlowMessage> Requests(Retry retry)
    {
        var sink = new BufferBlock<FlowMessage>();
        retry.Request.LinkTo(sink, new() { PropagateCompletion = true });
        return sink;
    }

    private static BufferBlock<FlowMessage> Output(Retry retry)
    {
        var sink = new BufferBlock<FlowMessage>();
        retry.Output.LinkTo(sink, new() { PropagateCompletion = true });
        return sink;
    }
}
