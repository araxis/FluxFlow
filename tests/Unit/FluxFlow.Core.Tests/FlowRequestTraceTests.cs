using FluxFlow.Core.Application;
using FluxFlow.Core.Coordination;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RequestTraceCollection
{
    public const string Name = "Request tracing";
}

[Collection(RequestTraceCollection.Name)]
public sealed class FlowRequestTraceTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_span_covers_reply_and_admission_cleanup(bool error)
    {
        using var traces = new Traces();
        using var caller = new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start();
        caller.TraceStateString = "region=west";
        var original = error ? FlowMessage.Create(new FlowError("test", "expected", "test"))
            : FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { data = 7 }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var originalHeaders = original.Headers;
        await using var exchanges = new FlowRequestReply();
        FlowMessage? sent = null;
        Activity? duringSend = null;
        var entered = Gate();
        var release = Gate();
        var operation = exchanges.RequestAsync("main.sink.reply", original, async (message, _) =>
        {
            sent = message;
            duringSend = Activity.Current;
            entered.TrySetResult();
            await release.Task;
        }, Deadline);
        try
        {
            await entered.Task.WaitAsync(Deadline);
            var span = traces.Started.ShouldHaveSingleItem();
            span.ShouldBeSameAs(duringSend);
            span.OperationName.ShouldBe("flow.request");
            span.Kind.ShouldBe(ActivityKind.Internal);
            span.ParentSpanId.ShouldBe(caller.SpanId);
            span.TraceId.ShouldBe(caller.TraceId);
            span.TraceStateString.ShouldBe("region=west");
            span.Links.ShouldHaveSingleItem().Context.ShouldBe(caller.Context);
            span.GetTagItem("flow.reply.address").ShouldBe("main.sink.reply");
            span.GetTagItem("flow.message_id").ShouldBe(original.MessageId.Value);
            span.GetTagItem("flow.message.is_error").ShouldBe(error);
            sent.ShouldNotBeNull().Value.ShouldBeSameAs(original.Value);
            sent.Error.ShouldBeSameAs(original.Error);
            sent.MessageId.ShouldBe(original.MessageId);
            sent.CausationId.ShouldBe(original.CausationId);
            sent.TraceId.ShouldBe(original.TraceId);
            sent.CorrelationId.ShouldNotBe(original.CorrelationId);
            sent.Headers["traceparent"].Json.GetString().ShouldBe(span.Id);
            original.Headers.ShouldBeSameAs(originalHeaders);
            original.Headers["traceparent"].Json.GetString().ShouldBe(caller.Id);
            traces.Stopped.ShouldBeEmpty();
            operation.IsCompleted.ShouldBeFalse();
            exchanges.Reply("wrong", sent).ShouldBeFalse();
            exchanges.Reply("main.sink.reply", sent).ShouldBeTrue();
            exchanges.PendingCount.ShouldBe(0);
            operation.IsCompleted.ShouldBeFalse();
            traces.Stopped.ShouldBeEmpty();
            release.TrySetResult();
            (await operation.WaitAsync(Deadline)).ShouldBeSameAs(sent);
            traces.Stopped.ShouldHaveSingleItem().ShouldBeSameAs(span);
            span.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
            span.GetTagItem("flow.request.outcome").ShouldBe("received");
            span.Status.ShouldBe(ActivityStatusCode.Unset);
            exchanges.Reply("main.sink.reply", sent).ShouldBeFalse();
            Activity.Current.ShouldBeSameAs(caller);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData("off", false)]
    [InlineData("off", true)]
    [InlineData("none", false)]
    [InlineData("none", true)]
    [InlineData("propagation", false)]
    [InlineData("propagation", true)]
    [InlineData("recorded", false)]
    [InlineData("recorded", true)]
    public async Task Request_sampling_preserves_parent_and_disabled_headers(string sampling, bool ambient)
    {
        FlowMessage original;
        ActivityContext creation;
        using (var producer = new Activity("producer").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            producer.TraceStateString = "origin=queue";
            creation = producer.Context;
            original = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(3, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        }
        using var caller = ambient ? new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start() : null;
        using var traces = sampling == "off" ? null : new Traces(sampling);
        await using var exchanges = new FlowRequestReply();
        FlowMessage? sent = null;
        Activity? duringSend = null;
        var result = await exchanges.RequestAsync("reply", original, (message, _) =>
        {
            sent = message;
            duringSend = Activity.Current;
            exchanges.Reply("reply", message).ShouldBeTrue();
            return ValueTask.CompletedTask;
        }, Deadline);
        result.ShouldBeSameAs(sent);
        exchanges.PendingCount.ShouldBe(0);
        Activity.Current.ShouldBeSameAs(caller);
        if (sampling is "off" or "none")
        {
            sent.ShouldNotBeNull().Headers.ShouldBeSameAs(original.Headers);
            duringSend.ShouldBeSameAs(caller);
            traces?.Started.ShouldBeEmpty();
            traces?.Stopped.ShouldBeEmpty();
        }
        else
        {
            var span = traces!.Started.ShouldHaveSingleItem();
            span.ParentSpanId.ShouldBe((caller?.Context ?? creation).SpanId);
            span.TraceId.ShouldBe((caller?.Context ?? creation).TraceId);
            span.Links.ShouldHaveSingleItem().Context.ShouldBe(creation);
            sent.ShouldNotBeNull().Headers["traceparent"].Json.GetString().ShouldBe(span.Id);
            if (ambient) sent.Headers.ContainsKey("tracestate").ShouldBeFalse();
            else sent.Headers["tracestate"].Json.GetString().ShouldBe("origin=queue");
            span.IsAllDataRequested.ShouldBe(sampling == "recorded");
            span.GetTagItem("flow.request.outcome").ShouldBe(sampling == "recorded" ? "received" : null);
            traces.Stopped.ShouldHaveSingleItem().ShouldBeSameAs(span);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    public async Task Request_without_valid_parent_starts_root(string? parent)
    {
        using var traces = new Traces();
        var original = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))), headers: parent is null ? null
            : new Dictionary<string, FlowValue> { ["traceparent"] = parent });
        await using var exchanges = new FlowRequestReply();
        await exchanges.RequestAsync("reply", original, (message, _) =>
        {
            exchanges.Reply("reply", message).ShouldBeTrue();
            return ValueTask.CompletedTask;
        }, Deadline);
        var span = traces.Stopped.ShouldHaveSingleItem();
        span.ParentSpanId.ShouldBe(default);
        span.Links.ShouldBeEmpty();
        span.TraceId.ShouldNotBe(default);
        original.Headers.GetValueOrDefault("traceparent")?.Json.GetString().ShouldBe(parent);
        Activity.Current.ShouldBeNull();
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("cancel")]
    [InlineData("stop")]
    [InlineData("failure")]
    public async Task Request_terminal_outcomes_preserve_exception_and_clear_pending(string outcome)
    {
        using var traces = new Traces();
        using var caller = new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start();
        var clock = new FakeTimeProvider();
        await using var exchanges = new FlowRequestReply(timeProvider: clock);
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("send failed");
        var entered = Gate();
        var exited = Gate();
        var operation = exchanges.RequestAsync("reply", FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), async (_, token) =>
        {
            entered.TrySetResult();
            try
            {
                if (outcome == "failure") throw failure;
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            }
            finally { exited.TrySetResult(); }
        }, Deadline, cancellation.Token);
        await entered.Task.WaitAsync(Deadline);
        if (outcome == "timeout") clock.Advance(Deadline);
        else if (outcome == "cancel") cancellation.Cancel();
        else if (outcome == "stop") exchanges.Stop();
        if (outcome == "failure")
            (await Should.ThrowAsync<InvalidOperationException>(async () => await operation.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        else if (outcome == "timeout")
            await Should.ThrowAsync<TimeoutException>(async () => await operation.WaitAsync(Deadline));
        else
        {
            var error = await Should.ThrowAsync<OperationCanceledException>(async () => await operation.WaitAsync(Deadline));
            if (outcome == "cancel") error.CancellationToken.ShouldBe(cancellation.Token);
        }
        exited.Task.IsCompletedSuccessfully.ShouldBeTrue();
        exchanges.PendingCount.ShouldBe(0);
        var span = traces.Stopped.ShouldHaveSingleItem();
        traces.Started.ShouldHaveSingleItem().ShouldBeSameAs(span);
        span.Status.ShouldBe(outcome is "failure" or "timeout" ? ActivityStatusCode.Error : ActivityStatusCode.Unset);
        span.GetTagItem("flow.request.outcome").ShouldBe(outcome == "failure" ? "failed" : outcome == "timeout" ? "timed_out" : "canceled");
        span.GetTagItem("error.type").ShouldBe(outcome == "failure" ? typeof(InvalidOperationException).FullName
            : outcome == "timeout" ? typeof(TimeoutException).FullName : null);
        Activity.Current.ShouldBeSameAs(caller);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("sample")]
    [InlineData("start")]
    [InlineData("stop")]
    public async Task Request_listener_failures_do_not_change_exchange_result(string failure)
    {
        using var traces = new Traces(failure: failure);
        using var caller = new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start();
        await using var exchanges = new FlowRequestReply();
        var original = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("request", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        FlowMessage? reply = null;
        var result = await exchanges.RequestAsync("reply", original, (message, _) =>
        {
            reply = message.WithValue((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("response", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            exchanges.Reply("reply", reply).ShouldBeTrue();
            return ValueTask.CompletedTask;
        }, Deadline);
        result.ShouldBeSameAs(reply);
        result.Value!.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("response");
        exchanges.PendingCount.ShouldBe(0);
        traces.Started.Count.ShouldBe(failure == "sample" ? 0 : 1);
        traces.Stopped.Count.ShouldBe(failure == "sample" ? 0 : 1);
        Activity.Current.ShouldBeSameAs(caller);
    }

    [Fact]
    public async Task Invalid_or_unregistered_requests_do_not_start_spans()
    {
        using var traces = new Traces();
        await using var exchanges = new FlowRequestReply(capacity: 1);
        var original = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        static ValueTask Send(FlowMessage _, CancellationToken token) => ValueTask.CompletedTask;
        await Should.ThrowAsync<ArgumentException>(() => exchanges.RequestAsync(" ", original, Send, Deadline));
        await Should.ThrowAsync<ArgumentNullException>(() => exchanges.RequestAsync("reply", null!, Send, Deadline));
        await Should.ThrowAsync<ArgumentNullException>(() => exchanges.RequestAsync("reply", original, null!, Deadline));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => exchanges.RequestAsync("reply", original, Send, TimeSpan.Zero));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => exchanges.RequestAsync("reply", original, Send, Deadline, canceled.Token));
        traces.Started.ShouldBeEmpty();
        var pending = exchanges.RequestAsync("reply", original, Send, Deadline);
        await Should.ThrowAsync<InvalidOperationException>(() => exchanges.RequestAsync("reply", original, Send, Deadline));
        traces.Started.Count.ShouldBe(1);
        exchanges.Stop();
        await Should.ThrowAsync<OperationCanceledException>(() => pending);
        await Should.ThrowAsync<OperationCanceledException>(() => exchanges.RequestAsync("reply", original, Send, Deadline));
        traces.Started.Count.ShouldBe(1);
        traces.Stopped.Count.ShouldBe(1);
        exchanges.PendingCount.ShouldBe(0);
    }


    [Fact]
    public async Task Concurrent_exchanges_share_trace_but_not_span_or_reply()
    {
        using var traces = new Traces();
        using var caller = new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start();
        await using var exchanges = new FlowRequestReply(capacity: 2);
        var original = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var sent = new List<FlowMessage>();
        ValueTask Send(FlowMessage message, CancellationToken _)
        {
            sent.Add(message);
            return ValueTask.CompletedTask;
        }
        var first = exchanges.RequestAsync("reply", original, Send, Deadline);
        var second = exchanges.RequestAsync("reply", original, Send, Deadline);
        var spans = traces.Started.ToArray();
        spans.Length.ShouldBe(2);
        spans[0].SpanId.ShouldNotBe(spans[1].SpanId);
        foreach (var span in spans)
        {
            span.TraceId.ShouldBe(caller.TraceId);
            span.ParentSpanId.ShouldBe(caller.SpanId);
        }
        sent[0].Headers["traceparent"].Json.GetString().ShouldBe(spans[0].Id);
        sent[1].Headers["traceparent"].Json.GetString().ShouldBe(spans[1].Id);
        sent[0].CorrelationId.ShouldNotBe(sent[1].CorrelationId);
        exchanges.Reply("reply", sent[1]).ShouldBeTrue();
        (await second.WaitAsync(Deadline)).ShouldBeSameAs(sent[1]);
        first.IsCompleted.ShouldBeFalse();
        traces.Stopped.ShouldHaveSingleItem().ShouldBeSameAs(spans[1]);
        exchanges.PendingCount.ShouldBe(1);
        exchanges.Reply("reply", sent[1]).ShouldBeFalse();
        exchanges.Reply("reply", sent[0]).ShouldBeTrue();
        (await first.WaitAsync(Deadline)).ShouldBeSameAs(sent[0]);
        traces.Stopped.ToArray().ShouldBe(new[] { spans[1], spans[0] });
        exchanges.PendingCount.ShouldBe(0);
        Activity.Current.ShouldBeSameAs(caller);
    }

    private sealed class DoubleNode : FlowNode
    {
        protected override async Task ProcessAsync(FlowMessage message)
            => await EmitAsync(message.WithValue((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(message.Value!.Json.Deserialize<int>() * 2, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
    }

    private sealed class Traces : IDisposable
    {
        private readonly ActivityListener _listener;
        internal ConcurrentQueue<Activity> Started { get; } = new();
        internal ConcurrentQueue<Activity> Stopped { get; } = new();
        internal Traces(string sampling = "recorded", string failure = "none")
        {
            _listener = new()
            {
                ShouldListenTo = source => source.Name == "FluxFlow.Core",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                {
                    if (failure == "sample") throw new InvalidOperationException("sample failed");
                    return sampling == "none" ? ActivitySamplingResult.None
                        : sampling == "propagation" ? ActivitySamplingResult.PropagationData
                        : ActivitySamplingResult.AllDataAndRecorded;
                },
                ActivityStarted = activity =>
                {
                    Started.Enqueue(activity);
                    if (failure == "start") throw new InvalidOperationException("start failed");
                },
                ActivityStopped = activity =>
                {
                    Stopped.Enqueue(activity);
                    if (failure == "stop") throw new InvalidOperationException("stop failed");
                }
            };
            ActivitySource.AddActivityListener(_listener);
        }
        public void Dispose() => _listener.Dispose();
    }
}
