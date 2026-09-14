using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class JoinTraceCollection
{
    public const string Name = "Join trace listeners";
}

[Collection(JoinTraceCollection.Name)]
public sealed class JoinTraceTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private sealed record Item(string Key, string Value);

    [Theory]
    [InlineData(-1, false, false)]
    [InlineData(-1, false, true)]
    [InlineData(-1, true, false)]
    [InlineData(-1, true, true)]
    [InlineData(0, false, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(0, true, true)]
    [InlineData(1, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(1, true, true)]
    [InlineData(3, false, false)]
    [InlineData(3, false, true)]
    [InlineData(3, true, false)]
    [InlineData(3, true, true)]
    public async Task Matched_join_links_both_inputs_without_changing_left_lineage(int sampling, bool rightFirst, bool shared)
    {
        using var traces = sampling < 0 ? null : new Traces((ActivitySamplingResult)sampling);
        FlowMessage left, right;
        using (var common = shared ? new Activity("common").SetIdFormat(ActivityIdFormat.W3C).Start() : null)
        {
            left = Message("left");
            right = Message("right");
        }
        var leftContext = Context(left);
        var rightContext = Context(right);
        (leftContext.TraceId == rightContext.TraceId).ShouldBe(shared);
        var originalHeaders = left.Headers;
        using var background = new Activity("worker").SetIdFormat(ActivityIdFormat.W3C).Start();
        var clock = new TrackingFakeTimeProvider(DateTimeOffset.UtcNow);
        await using var node = Create(clock);
        var sink = RoutingTestSink.Link(node.Output);
        var pending = clock.NextTimerScheduled;
        (await (rightFirst ? node.Right : node.Left).SendAsync(rightFirst ? right : left).WaitAsync(Deadline)).ShouldBeTrue();
        await pending.WaitAsync(Deadline);
        (await (rightFirst ? node.Left : node.Right).SendAsync(rightFirst ? left : right).WaitAsync(Deadline)).ShouldBeTrue();
        node.Complete();
        var output = (await RoutingTestSink.DrainUntilCompletedAsync(sink)).ShouldHaveSingleItem();
        await node.Completion.WaitAsync(Deadline);
        var match = output.Value!.Json.Deserialize<FlowJoinMatchedOutcome<Item, Item>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Match;
        match.Left.Value.ShouldBe("left");
        match.Right.Value.ShouldBe("right");
        output.CorrelationId.ShouldBe(left.CorrelationId);
        output.CausationId.ShouldBe(left.MessageId);
        output.TraceId.ShouldBe(left.TraceId);
        left.Headers.ShouldBeSameAs(originalHeaders);
        Context(left).ShouldBe(leftContext);
        Context(right).ShouldBe(rightContext);
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        Activity.Current.ShouldBeSameAs(background);
        if (sampling <= 0)
        {
            output.Headers.ShouldBeSameAs(left.Headers);
            traces?.Started.ShouldBeEmpty();
        }
        else
        {
            var aggregate = traces!.Stopped.Single(span => span.Links.Count() == 2);
            aggregate.ParentSpanId.ShouldBe(default);
            aggregate.TraceId.ShouldNotBe(background.TraceId);
            aggregate.Kind.ShouldBe(ActivityKind.Consumer);
            aggregate.Links.Select(link => link.Context).ShouldBe(new[] { leftContext, rightContext });
            Context(output).ShouldBe(aggregate.Context);
            aggregate.GetTagItem("flow.message.count").ShouldBe(sampling == 3 ? 2 : null);
            aggregate.GetTagItem("flow.message_id").ShouldBeNull();
            aggregate.GetTagItem("flow.processing.outcome").ShouldBe(sampling == 3 ? "matched" : null);
            aggregate.Status.ShouldBe(ActivityStatusCode.Unset);
            traces.Started.Count.ShouldBe(3);
            traces.Stopped.Count.ShouldBe(3);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Timeout_uses_expired_input_context_not_command_context(bool leftSide, bool complete, bool enabled)
    {
        using var traces = enabled ? new Traces() : null;
        var input = Message("expired");
        var origin = Context(input);
        using var background = new Activity("worker").SetIdFormat(ActivityIdFormat.W3C).Start();
        var clock = new TrackingFakeTimeProvider(DateTimeOffset.UtcNow);
        await using var node = Create(clock);
        var sink = RoutingTestSink.Link(node.Output);
        var pending = clock.NextTimerScheduled;
        (await (leftSide ? node.Left : node.Right).SendAsync(input).WaitAsync(Deadline)).ShouldBeTrue();
        await pending.WaitAsync(Deadline);
        sink.TryReceive(out _).ShouldBeFalse();
        if (complete) node.Complete();
        else clock.Advance(TimeSpan.FromMilliseconds(100));
        var output = await sink.ReceiveAsync(Deadline);
        node.Complete();
        await node.Completion.WaitAsync(Deadline);
        var timeout = output.Value!.Json.Deserialize<FlowJoinTimedOutOutcome<Item, Item>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Timeout;
        timeout.Side.ShouldBe(leftSide ? FlowJoinSide.Left : FlowJoinSide.Right);
        (leftSide ? timeout.Left : timeout.Right)!.Value.ShouldBe("expired");
        output.CorrelationId.ShouldBe(input.CorrelationId);
        output.CausationId.ShouldBe(input.MessageId);
        Context(input).ShouldBe(origin);
        sink.Count.ShouldBe(0);
        if (enabled)
        {
            var span = traces!.Stopped.Single(activity => Equals(activity.GetTagItem("flow.processing.outcome"), "timed_out"));
            span.ParentSpanId.ShouldBe(origin.SpanId);
            span.Links.ShouldHaveSingleItem().Context.ShouldBe(origin);
            Context(output).ShouldBe(span.Context);
            span.Status.ShouldBe(ActivityStatusCode.Unset);
            traces.Stopped.Count.ShouldBe(2);
        }
        else output.Headers.ShouldBeSameAs(input.Headers);
        Activity.Current.ShouldBeSameAs(background);
    }

    [Theory]
    [InlineData("sample")]
    [InlineData("start")]
    [InlineData("stop")]
    public async Task Join_listener_failures_preserve_match_and_next_command(string failure)
    {
        using var traces = new Traces(failure: failure);
        var first = Message("first");
        var second = Message("second");
        using var background = new Activity("worker").SetIdFormat(ActivityIdFormat.W3C).Start();
        await using var node = Create();
        var sink = RoutingTestSink.Link(node.Output);
        foreach (var input in new[] { first, second })
        {
            (await node.Left.SendAsync(input).WaitAsync(Deadline)).ShouldBeTrue();
            (await node.Right.SendAsync(input).WaitAsync(Deadline)).ShouldBeTrue();
            var output = await sink.ReceiveAsync(Deadline);
            output.Value!.Json.Deserialize<FlowJoinMatchedOutcome<Item, Item>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Match.Left.Value
                .ShouldBe(input.Value!.Json.Deserialize<Item>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Value);
            output.CorrelationId.ShouldBe(input.CorrelationId);
            output.CausationId.ShouldBe(input.MessageId);
            if (failure != "stop") output.Headers.ShouldBeSameAs(input.Headers);
        }
        node.Complete();
        await node.Completion.WaitAsync(Deadline);
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        traces.Started.Count.ShouldBe(failure == "sample" ? 0 : 6);
        traces.Stopped.Count.ShouldBe(failure == "sample" ? 0 : 6);
        Activity.Current.ShouldBeSameAs(background);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selector_failure_preserves_error_lineage_and_records_failure(bool enabled)
    {
        using var traces = enabled ? new Traces() : null;
        var input = Message("failure");
        using var background = new Activity("worker").SetIdFormat(ActivityIdFormat.W3C).Start();
        await using var node = new JoinNode<Item, Item>(new(), _ => throw new InvalidOperationException("selector failed"), item => item.Key);
        var sink = RoutingTestSink.Link(node.Output);
        (await node.Left.SendAsync(input).WaitAsync(Deadline)).ShouldBeTrue();
        node.Complete();
        var output = (await RoutingTestSink.DrainUntilCompletedAsync(sink)).ShouldHaveSingleItem();
        await node.Completion.WaitAsync(Deadline);
        output.IsError.ShouldBeTrue();
        output.Error!.Message.ShouldContain("selector failed");
        output.CorrelationId.ShouldBe(input.CorrelationId);
        output.CausationId.ShouldBe(input.MessageId);
        if (enabled)
        {
            var span = traces!.Stopped.ShouldHaveSingleItem();
            span.Status.ShouldBe(ActivityStatusCode.Error);
            span.GetTagItem("error.type").ShouldNotBeNull();
            span.ParentSpanId.ShouldBe(Context(input).SpanId);
            Context(output).ShouldBe(span.Context);
        }
        else output.Headers.ShouldBeSameAs(input.Headers);
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        Activity.Current.ShouldBeSameAs(background);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    public async Task Aggregate_scope_links_only_valid_inputs_and_restores_ambient(int sampling, int valid)
    {
        using var traces = new Traces((ActivitySamplingResult)sampling);
        var inputs = new[] { Message("first"), Message("second") };
        for (var index = 0; index < 2; index++)
            if ((valid & (1 << index)) == 0)
                inputs[index] = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(index, new JsonSerializerOptions(JsonSerializerDefaults.Web))), headers: new Dictionary<string, string> { ["traceparent"] = "invalid" });
        var expected = inputs.Where(message => message.Headers["traceparent"] != "invalid").Select(Context).ToArray();
        using var background = new Activity("worker").SetIdFormat(ActivityIdFormat.W3C).Start();
        Activity? current;
        using (var scope = FlowTracing.StartProcessing(inputs.AsSpan(), this))
        {
            current = Activity.Current;
            await Task.Yield();
            Activity.Current.ShouldBeSameAs(current);
            traces.Stopped.ShouldBeEmpty();
            if (sampling == 0) current.ShouldBeNull();
            else
            {
                current.ShouldNotBeNull().ParentSpanId.ShouldBe(default);
                current.Links.Select(link => link.Context).ShouldBe(expected);
                current.GetTagItem("flow.message.count").ShouldBe(2);
            }
            scope.Outcome("combined");
        }
        Activity.Current.ShouldBeSameAs(background);
        if (sampling == 0) traces.Started.ShouldBeEmpty();
        else
        {
            traces.Stopped.ShouldHaveSingleItem().ShouldBeSameAs(current);
            current!.GetTagItem("flow.processing.outcome").ShouldBe("combined");
        }
    }

    [Fact]
    public async Task Public_scopes_validate_inputs_and_suppress_only_the_current_scope()
    {
        using var traces = new Traces();
        var message = Message("input");
        using var caller = new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start();
        Should.Throw<ArgumentNullException>(() => FlowTracing.StartProcessing((FlowMessage)null!, this));
        Should.Throw<ArgumentNullException>(() => FlowTracing.StartProcessing(message, null!));
        Should.Throw<ArgumentException>(() => FlowTracing.StartProcessing(ReadOnlySpan<FlowMessage>.Empty, this));
        Should.Throw<ArgumentNullException>(() => FlowTracing.StartProcessing(new FlowMessage[] { message, null! }.AsSpan(), this));
        Should.Throw<ArgumentNullException>(() => FlowTracing.StartProcessing(new[] { message }.AsSpan(), null!));
        var empty = default(FlowTracing.OperationActivity);
        Should.Throw<ArgumentNullException>(() => empty.Failed(null!));
        Should.Throw<ArgumentNullException>(() => empty.Outcome(null!));
        using (default(FlowTracing.OperationActivity)) Activity.Current.ShouldBeSameAs(caller);
        using (FlowTracing.SuppressContext())
        {
            Activity.Current.ShouldBeNull();
            using (FlowTracing.SuppressContext())
            {
                await Task.Yield();
                Activity.Current.ShouldBeNull();
            }
            Activity.Current.ShouldBeNull();
        }
        Activity.Current.ShouldBeSameAs(caller);
        traces.Started.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, "drain")]
    [InlineData(false, "fault")]
    [InlineData(false, "decline")]
    [InlineData(true, "drain")]
    [InlineData(true, "fault")]
    [InlineData(true, "decline")]
    public async Task Blocked_join_emission_span_settles_with_admission_not_downstream(bool timeout, string terminal)
    {
        var left = Enumerable.Range(0, 3).Select(index => Message($"left-{index}")).ToArray();
        var right = Enumerable.Range(0, 3).Select(index => Message($"right-{index}")).ToArray();
        var thirdContext = Context(left[2]);
        var thirdStarted = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdStopped = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        var flushing = 0;
        using var traces = new Traces(started: activity =>
        {
            if (timeout && Volatile.Read(ref flushing) == 0) return;
            var links = activity.Links.ToArray();
            if (links.Length == (timeout ? 1 : 2) && links[0].Context == thirdContext)
                thirdStarted.TrySetResult(activity);
        }, stopped: activity =>
        {
            if (thirdStarted.Task.IsCompletedSuccessfully && ReferenceEquals(activity, thirdStarted.Task.Result))
                thirdStopped.TrySetResult(activity);
        });
        using var background = new Activity("worker").SetIdFormat(ActivityIdFormat.W3C).Start();
        var clock = new TrackingFakeTimeProvider(DateTimeOffset.UtcNow);
        await using var node = new JoinNode<Item, Item>(new JoinRoutingOptions
        {
            BoundedCapacity = 1,
            MaxPending = 4,
            TimeoutMilliseconds = 30_000
        }, item => item.Key, item => item.Key, clock: clock);
        var target = new PostponedTargetBlock<FlowMessage>();
        using var link = node.Output.LinkTo(target, new DataflowLinkOptions { PropagateCompletion = true });
        var ownerFailure = new InvalidOperationException("Stop blocked join");
        try
        {
            for (var index = 0; index < 3; index++)
            {
                var pending = clock.NextTimerScheduled;
                (await node.Left.SendAsync(left[index]).WaitAsync(Deadline)).ShouldBeTrue();
                await pending.WaitAsync(Deadline);
                if (!timeout)
                    (await node.Right.SendAsync(right[index]).WaitAsync(Deadline)).ShouldBeTrue();
            }
            if (timeout) Volatile.Write(ref flushing, 1);
            node.Complete();
            await target.WaitForOfferAsync(Deadline);
            var held = await thirdStarted.Task.WaitAsync(Deadline);
            var outcome = timeout ? "timed_out" : "matched";
            traces.Stopped.Count(activity => Equals(activity.GetTagItem("flow.processing.outcome"), outcome)).ShouldBe(2);
            held.Duration.ShouldBe(TimeSpan.Zero);
            thirdStopped.Task.IsCompleted.ShouldBeFalse();
            target.Accepted.ShouldBeEmpty();
            node.Completion.IsCompleted.ShouldBeFalse();
            Activity.Current.ShouldBeSameAs(background);

            if (terminal == "drain")
            {
                target.AcceptNext();
                await target.WaitForOfferAsync(Deadline);
                (await thirdStopped.Task.WaitAsync(Deadline)).ShouldBeSameAs(held);
                held.GetTagItem("flow.processing.outcome").ShouldBe(outcome);
                held.Status.ShouldBe(ActivityStatusCode.Unset);
                held.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
                target.Accepted.Count.ShouldBe(1);
                node.Completion.IsCompleted.ShouldBeFalse();
                target.AcceptNext();
                await target.WaitForOfferAsync(Deadline);
                target.AcceptNext();
                await node.Completion.WaitAsync(Deadline);
                await target.Completion.WaitAsync(Deadline);
                var results = target.Accepted;
                results.Count.ShouldBe(3);
                for (var index = 0; index < 3; index++)
                {
                    var value = timeout
                        ? results[index].Value!.Json.Deserialize<FlowJoinTimedOutOutcome<Item, Item>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Timeout.Left!.Value
                        : results[index].Value!.Json.Deserialize<FlowJoinMatchedOutcome<Item, Item>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Match.Left.Value;
                    value.ShouldBe($"left-{index}");
                    results[index].CorrelationId.ShouldBe(left[index].CorrelationId);
                    results[index].CausationId.ShouldBe(left[index].MessageId);
                }
                Context(results[2]).ShouldBe(held.Context);
            }
            else
            {
                if (terminal == "fault") node.Fault(ownerFailure);
                else node.Output.Complete();
                var observed = await Should.ThrowAsync<InvalidOperationException>(async () => await node.Completion.WaitAsync(Deadline));
                if (terminal == "fault") observed.ShouldBeSameAs(ownerFailure);
                (await thirdStopped.Task.WaitAsync(Deadline)).ShouldBeSameAs(held);
                if (terminal == "fault")
                {
                    held.GetTagItem("flow.processing.outcome").ShouldBe("canceled");
                    held.Status.ShouldBe(ActivityStatusCode.Unset);
                    held.GetTagItem("error.type").ShouldBeNull();
                }
                else
                {
                    held.Status.ShouldBe(ActivityStatusCode.Error);
                    held.GetTagItem("error.type").ShouldBe(typeof(InvalidOperationException).FullName);
                    held.GetTagItem("flow.processing.outcome").ShouldBeNull();
                }
                target.Accepted.ShouldBeEmpty();
                var targetError = await Should.ThrowAsync<InvalidOperationException>(async () => await target.Completion.WaitAsync(Deadline));
                if (terminal == "fault") targetError.ShouldBeSameAs(ownerFailure);
            }
            traces.Stopped.ToArray().ShouldBe(traces.Started.ToArray(), ignoreOrder: true);
            Activity.Current.ShouldBeSameAs(background);
        }
        finally
        {
            if (!node.Completion.IsCompleted) node.Fault(new OperationCanceledException("Test cleanup"));
        }
    }

    private static JoinNode<Item, Item> Create(TimeProvider? clock = null)
        => new(new JoinRoutingOptions { TimeoutMilliseconds = 100, BoundedCapacity = 4, MaxPending = 4 },
            item => item.Key, item => item.Key, clock: clock);

    private static FlowMessage Message(string value)
    {
        using var producer = new Activity("producer").SetIdFormat(ActivityIdFormat.W3C).Start();
        producer.TraceStateString = "region=west";
        return FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new Item("key", value), new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());
    }

    private static ActivityContext Context(FlowMessage message)
    {
        ActivityContext.TryParse(message.Headers.GetValueOrDefault("traceparent"),
            message.Headers.GetValueOrDefault("tracestate"), out var context).ShouldBeTrue();
        return context;
    }

    private sealed class Traces : IDisposable
    {
        private readonly ActivityListener _listener;
        internal ConcurrentQueue<Activity> Started { get; } = new();
        internal ConcurrentQueue<Activity> Stopped { get; } = new();
        internal Traces(ActivitySamplingResult sampling = ActivitySamplingResult.AllDataAndRecorded, string? failure = null,
            Action<Activity>? started = null, Action<Activity>? stopped = null)
        {
            _listener = new()
            {
                ShouldListenTo = source => source.Name == "FluxFlow.Core",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                {
                    if (failure == "sample") throw new InvalidOperationException("sample failed");
                    return sampling;
                },
                ActivityStarted = activity =>
                {
                    Started.Enqueue(activity);
                    started?.Invoke(activity);
                    if (failure == "start") throw new InvalidOperationException("start failed");
                },
                ActivityStopped = activity =>
                {
                    Stopped.Enqueue(activity);
                    stopped?.Invoke(activity);
                    if (failure == "stop") throw new InvalidOperationException("stop failed");
                }
            };
            ActivitySource.AddActivityListener(_listener);
        }
        public void Dispose() => _listener.Dispose();
    }
}
