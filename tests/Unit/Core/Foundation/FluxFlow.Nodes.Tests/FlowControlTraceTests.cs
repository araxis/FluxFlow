using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

[Collection(ProcessingTraceCollection.Name)]
public sealed class FlowControlTraceTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Control_activity_covers_accepted_work_through_source_drain(bool error, bool productionEnded)
    {
        using var traces = new Traces();
        var original = Message(error);
        using var background = new Activity("background").SetIdFormat(ActivityIdFormat.W3C).Start();
        var entered = new TaskCompletionSource<Activity?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = Gate();
        FlowMessage? received = null;
        Activity? afterAwait = null;
        await using var source = new Source(async (message, token) =>
        {
            received = message;
            entered.TrySetResult(Activity.Current);
            await release.Task.WaitAsync(token);
            afterAwait = Activity.Current;
        });
        try
        {
            if (productionEnded)
            {
                source.StopProduction();
                await source.ProductionCompletion.WaitAsync(Deadline);
            }
            source.Ack.ShouldBeOfType<ActionBlock<FlowMessage>>();
            (await source.Ack.SendAsync(original)).ShouldBeTrue();
            var activity = (await entered.Task.WaitAsync(Deadline)).ShouldNotBeNull();
            activity.Source.Name.ShouldBe("FluxFlow.Core");
            activity.OperationName.ShouldBe("flow.control");
            activity.Kind.ShouldBe(ActivityKind.Consumer);
            activity.ParentSpanId.ShouldBe(Context(original).SpanId);
            activity.TraceId.ShouldBe(Context(original).TraceId);
            activity.TraceStateString.ShouldBe("region=west");
            activity.Links.ShouldHaveSingleItem().Context.ShouldBe(Context(original));
            activity.GetTagItem("flow.component.type").ShouldBe(typeof(Source).FullName);
            activity.GetTagItem("flow.control.port").ShouldBe("Ack");
            activity.GetTagItem("flow.message_id").ShouldBe(original.MessageId.Value);
            activity.GetTagItem("flow.trace_id").ShouldBe(original.TraceId.Value);
            activity.GetTagItem("flow.message.is_error").ShouldBe(error);
            activity.Status.ShouldBe(ActivityStatusCode.Unset);
            received.ShouldBeSameAs(original);
            traces.Stopped.ShouldBeEmpty();
            activity.Duration.ShouldBe(TimeSpan.Zero);
            source.Complete();
            source.Completion.IsCompleted.ShouldBeFalse();
            (await source.Ack.SendAsync(original)).ShouldBeFalse();
            release.TrySetResult();
            await source.Completion.WaitAsync(Deadline);
            afterAwait.ShouldBeSameAs(activity);
            traces.Stopped.ShouldHaveSingleItem().ShouldBeSameAs(activity);
            activity.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
            source.Ack.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            Activity.Current.ShouldBeSameAs(background);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Control_sampling_preserves_carrier_without_inheriting_pump_context(int sampling)
    {
        using var traces = sampling < 0 ? null : new Traces((ActivitySamplingResult)sampling);
        var original = Message();
        using var background = new Activity("background").SetIdFormat(ActivityIdFormat.W3C).Start();
        Activity? observed = null;
        FlowMessage? derived = null;
        await using var source = new Source((message, _) =>
        {
            observed = Activity.Current;
            derived = message.With(FlowValue.From("derived"));
            return Task.CompletedTask;
        });
        (await source.Ack.SendAsync(original)).ShouldBeTrue();
        source.Complete();
        await source.Completion.WaitAsync(Deadline);
        var result = derived.ShouldNotBeNull();
        result.Value!.Deserialize<string>().ShouldBe("derived");
        result.CausationId.ShouldBe(original.MessageId);
        result.MessageId.ShouldNotBe(original.MessageId);
        if (sampling == 1)
        {
            var activity = observed.ShouldNotBeNull();
            activity.OperationName.ShouldBe("flow.control");
            activity.ParentSpanId.ShouldBe(Context(original).SpanId);
            activity.TagObjects.ShouldBeEmpty();
            activity.IsAllDataRequested.ShouldBeFalse();
#if NET10_0_OR_GREATER
            activity.Recorded.ShouldBeFalse();
#else
            // Older runtimes inherit a recorded parent even for propagation-only sampling.
            activity.Recorded.ShouldBeTrue();
#endif
            Context(result).ShouldBe(activity.Context);
            traces!.Stopped.ShouldHaveSingleItem().ShouldBeSameAs(activity);
        }
        else
        {
            observed.ShouldBeNull();
            Context(result).ShouldBe(Context(original));
            result.Headers.ShouldBeSameAs(original.Headers);
            traces?.Stopped.ShouldBeEmpty();
        }
        (traces?.Samples ?? 0).ShouldBe(sampling < 0 ? 0 : 1);
        Activity.Current.ShouldBeSameAs(background);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Control_without_valid_carrier_starts_a_root(bool malformed)
    {
        using var traces = new Traces();
        Activity.Current.ShouldBeNull();
        var original = FlowMessage.Create(FlowValue.From(7), headers: malformed
            ? new Dictionary<string, string> { ["traceparent"] = "invalid" } : null);
        using var background = new Activity("background").SetIdFormat(ActivityIdFormat.W3C).Start();
        FlowMessage? received = null;
        await using var source = new Source((message, _) => { received = message; return Task.CompletedTask; });
        (await source.Ack.SendAsync(original)).ShouldBeTrue();
        source.Complete();
        await source.Completion.WaitAsync(Deadline);
        received.ShouldBeSameAs(original);
        var activity = traces.Stopped.ShouldHaveSingleItem();
        activity.ParentSpanId.ShouldBe(default);
        activity.TraceId.ShouldNotBe(background.TraceId);
        activity.Links.ShouldBeEmpty();
        activity.OperationName.ShouldBe("flow.control");
        original.Headers.ContainsKey("traceparent").ShouldBe(malformed);
        Activity.Current.ShouldBeSameAs(background);
    }

    [Fact]
    public async Task Concurrent_controls_keep_independent_contexts()
    {
        using var traces = new Traces();
        var originals = new[] { Message(), Message(true) };
        Context(originals[0]).TraceId.ShouldNotBe(Context(originals[1]).TraceId);
        using var background = new Activity("background").SetIdFormat(ActivityIdFormat.W3C).Start();
        var entered = new[] { Gate(), Gate() };
        var release = new[] { Gate(), Gate() };
        var before = new Activity?[2];
        var after = new Activity?[2];
        var received = new FlowMessage?[2];
        async Task Handle(int index, FlowMessage message, CancellationToken token)
        {
            received[index] = message;
            before[index] = Activity.Current;
            entered[index].TrySetResult();
            await release[index].Task.WaitAsync(token);
            after[index] = Activity.Current;
        }
        await using var source = new Source((message, token) => Handle(0, message, token),
            (message, token) => Handle(1, message, token));
        try
        {
            (await source.Ack.SendAsync(originals[0])).ShouldBeTrue();
            (await source.Reject.SendAsync(originals[1])).ShouldBeTrue();
            await Task.WhenAll(entered.Select(gate => gate.Task)).WaitAsync(Deadline);
            traces.Stopped.ShouldBeEmpty();
            before[0].ShouldNotBeNull().ShouldNotBeSameAs(before[1]);
            source.Complete();
            release[1].TrySetResult();
            await source.Reject.Completion.WaitAsync(Deadline);
            source.Completion.IsCompleted.ShouldBeFalse();
            traces.Stopped.ShouldHaveSingleItem().ShouldBeSameAs(before[1]);
            release[0].TrySetResult();
            await source.Completion.WaitAsync(Deadline);
            traces.Stopped.Count.ShouldBe(2);
            for (var index = 0; index < 2; index++)
            {
                received[index].ShouldBeSameAs(originals[index]);
                var activity = before[index].ShouldNotBeNull();
                after[index].ShouldBeSameAs(activity);
                activity.TraceId.ShouldBe(Context(originals[index]).TraceId);
                activity.ParentSpanId.ShouldBe(Context(originals[index]).SpanId);
                activity.GetTagItem("flow.control.port").ShouldBe(index == 0 ? "Ack" : "Reject");
            }
            Activity.Current.ShouldBeSameAs(background);
        }
        finally { foreach (var gate in release) gate.TrySetResult(); }
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("cancellation")]
    [InlineData("stop")]
    public async Task Control_fault_and_cancellation_preserve_owner_outcome(string outcome)
    {
        using var traces = new Traces();
        var entered = Gate();
        var release = Gate();
        Exception failure = outcome == "cancellation"
            ? new OperationCanceledException("unrequested cancellation")
            : new InvalidOperationException("owner failure");
        Activity? observed = null;
        FlowMessage? received = null;
        var original = Message();
        await using var source = new Source(async (message, token) =>
        {
            received = message;
            observed = Activity.Current;
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            throw failure;
        });
        try
        {
            (await source.Ack.SendAsync(original)).ShouldBeTrue();
            await entered.Task.WaitAsync(Deadline);
            traces.Stopped.ShouldBeEmpty();
            if (outcome == "stop") source.Fault(failure);
            else release.TrySetResult();
            (await Should.ThrowAsync<Exception>(() => source.Completion.WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
            received.ShouldBeSameAs(original);
            var activity = traces.Stopped.ShouldHaveSingleItem();
            activity.ShouldBeSameAs(observed);
            activity.Status.ShouldBe(outcome == "stop" ? ActivityStatusCode.Unset : ActivityStatusCode.Error);
            activity.GetTagItem("flow.processing.outcome").ShouldBe(outcome == "stop" ? "canceled" : null);
            activity.GetTagItem("error.type").ShouldBe(outcome == "stop" ? null : failure.GetType().FullName);
            activity.StatusDescription.ShouldBeNull();
            source.ProductionCompletion.IsFaulted.ShouldBeTrue();
            source.Reject.Completion.IsFaulted.ShouldBeTrue();
            (await source.Reject.SendAsync(original)).ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData("sample")]
    [InlineData("start")]
    [InlineData("stop")]
    public async Task Control_listener_failure_does_not_change_processing(string failure)
    {
        using var traces = new Traces(failure: failure);
        var original = Message();
        using var background = new Activity("background").SetIdFormat(ActivityIdFormat.W3C).Start();
        FlowMessage? received = null;
        FlowMessage? derived = null;
        await using var source = new Source((message, _) =>
        {
            received = message;
            derived = message.With(FlowValue.From("processed"));
            return Task.CompletedTask;
        });
        (await source.Ack.SendAsync(original)).ShouldBeTrue();
        source.Complete();
        await source.Completion.WaitAsync(Deadline);
        received.ShouldBeSameAs(original);
        derived.ShouldNotBeNull().Value!.Deserialize<string>().ShouldBe("processed");
        traces.Samples.ShouldBe(1);
        traces.Stopped.Count.ShouldBe(failure == "sample" ? 0 : 1);
        Context(derived!).ShouldBe(failure == "stop" ? traces.Stopped.Single().Context : Context(original));
        source.Ack.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        source.Reject.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        Activity.Current.ShouldBeSameAs(background);
    }

    private static FlowMessage Message(bool error = false)
    {
        using var producer = new Activity("producer").SetIdFormat(ActivityIdFormat.W3C).Start();
        producer.TraceStateString = "region=west";
        producer.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        return error ? FlowMessage.CreateError(new FlowError("control.test", "Failure", "test"))
            : FlowMessage.Create(FlowValue.From(42));
    }

    private static ActivityContext Context(FlowMessage message)
    {
        message.Headers.TryGetValue("traceparent", out var parent);
        message.Headers.TryGetValue("tracestate", out var state);
        ActivityContext.TryParse(parent, state, out var context).ShouldBeTrue();
        return context;
    }

    private sealed class Source : FlowSource
    {
        public Source(Func<FlowMessage, CancellationToken, Task> ack,
            Func<FlowMessage, CancellationToken, Task>? reject = null)
        {
            Ack = AddControlInput("Ack", ack, 2);
            Reject = AddControlInput("Reject", reject ?? (static (_, _) => Task.CompletedTask), 2);
        }
        public ITargetBlock<FlowMessage> Ack { get; }
        public ITargetBlock<FlowMessage> Reject { get; }
        protected override Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Traces : IDisposable
    {
        private readonly ActivityListener _listener;
        private int _samples;
        public int Samples => Volatile.Read(ref _samples);
        public ConcurrentQueue<Activity> Stopped { get; } = new();

        public Traces(ActivitySamplingResult sampling = ActivitySamplingResult.AllDataAndRecorded, string? failure = null)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "FluxFlow.Core",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                {
                    Interlocked.Increment(ref _samples);
                    if (failure == "sample") throw new InvalidOperationException("sample failed");
                    return sampling;
                },
                ActivityStarted = _ => { if (failure == "start") throw new InvalidOperationException("start failed"); },
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
