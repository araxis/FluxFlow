using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Nodes;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Metrics.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MetricTraceCollection
{
    public const string Name = "Metric operation listeners";
}

[Collection(MetricTraceCollection.Name)]
public sealed class MetricsAggregateTraceTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(-1, 1, false)]
    [InlineData(-1, 1, true)]
    [InlineData(-1, 64, false)]
    [InlineData(-1, 64, true)]
    [InlineData(0, 1, false)]
    [InlineData(0, 1, true)]
    [InlineData(0, 64, false)]
    [InlineData(0, 64, true)]
    [InlineData(1, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, 64, false)]
    [InlineData(1, 64, true)]
    [InlineData(3, 1, false)]
    [InlineData(3, 1, true)]
    [InlineData(3, 64, false)]
    [InlineData(3, 64, true)]
    public async Task Final_snapshot_uses_one_bounded_link_not_worker_or_history_parent(
        int sampling, int count, bool valid)
    {
        using var traces = sampling < 0 ? null : new Traces((ActivitySamplingResult)sampling);
        using var worker = new Activity("construction").SetIdFormat(ActivityIdFormat.W3C).Start();
        await using var node = new MetricsAggregateNode(new() { EmitEverySample = false, BoundedCapacity = 1 });
        var target = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(target, new() { PropagateCompletion = true });
        using var caller = new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start();
        FlowMessage? last = null;
        var inputs = new List<FlowMessage>();
        for (var index = 1; index <= count; index++)
        {
            last = Sample(index, valid: valid);
            inputs.Add(last);
            (await node.Input.SendAsync(last).WaitAsync(Deadline)).ShouldBeTrue();
        }
        var rejected = Sample(-1, size: -1);
        (await node.Input.SendAsync(rejected).WaitAsync(Deadline)).ShouldBeTrue();
        node.Complete();
        await node.Completion.WaitAsync(Deadline);
        var error = await target.ReceiveAsync().WaitAsync(Deadline);
        var result = await target.ReceiveAsync().WaitAsync(Deadline);
        await target.Completion.WaitAsync(Deadline);
        error.IsError.ShouldBeTrue();
        error.CausationId.ShouldBe(rejected.MessageId);
        result.IsError.ShouldBeFalse();
        result.CorrelationId.ShouldBe(last!.CorrelationId);
        result.TraceId.ShouldBe(last.TraceId);
        result.CausationId.ShouldBe(last.MessageId);
        result.MessageId.ShouldNotBe(last.MessageId);
        result.Headers["tenant"].ShouldBe("north");
        var snapshot = result.Value.Deserialize<MetricSnapshotOutput>()!;
        snapshot.SampleCount.ShouldBe(count);
        snapshot.TotalValue.ShouldBe(count * (count + 1) / 2d);
        snapshot.Latest!.Value.ShouldBe(count);
        target.TryReceive(out _).ShouldBeFalse();
        if (sampling <= 0)
        {
            result.Headers.ShouldBeSameAs(last.Headers);
            traces?.Stopped.ShouldBeEmpty();
        }
        else
        {
            var activities = traces!.Stopped.ToArray();
            activities.Length.ShouldBe(count + 2);
            var final = activities[^1];
            final.OperationName.ShouldBe("flow.process");
            final.Kind.ShouldBe(ActivityKind.Consumer);
            final.ParentSpanId.ShouldBe(default);
            final.TraceId.ShouldNotBe(worker.TraceId);
            final.TraceId.ShouldNotBe(caller.TraceId);
            final.Links.Select(item => item.Context).ShouldBe(valid ? new[] { Context(last) } : []);
            Context(result).ShouldBe(final.Context);
            final.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
            final.Status.ShouldBe(ActivityStatusCode.Unset);
            final.GetTagItem("flow.message_id").ShouldBeNull();
            if (sampling == 3)
            {
                final.GetTagItem("flow.message.count").ShouldBe(1);
                final.GetTagItem("flow.processing.outcome").ShouldBe("snapshot");
            }
            else final.TagObjects.ShouldBeEmpty();
            for (var index = 0; index < count; index++)
            {
                activities[index].ParentSpanId.ShouldBe(valid ? Context(inputs[index]).SpanId : default);
                activities[index].Links.Count().ShouldBe(valid ? 1 : 0);
            }
            traces.Started.ToArray().ShouldBe(activities);
        }
        Activity.Current.ShouldBeSameAs(caller);
    }

    [Theory]
    [InlineData(true, 3, false)]
    [InlineData(false, 0, false)]
    [InlineData(false, 3, true)]
    public async Task Streams_without_a_final_snapshot_do_not_start_a_final_scope(
        bool every, int count, bool rejected)
    {
        using var traces = new Traces();
        await using var node = new MetricsAggregateNode(new() { EmitEverySample = every });
        var target = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(target, new() { PropagateCompletion = true });
        var inputs = Enumerable.Range(1, count).Select(index => Sample(index, size: rejected ? -1 : 1)).ToArray();
        foreach (var input in inputs) (await node.Input.SendAsync(input).WaitAsync(Deadline)).ShouldBeTrue();
        node.Complete();
        await node.Completion.WaitAsync(Deadline);
        var activities = traces.Stopped.ToArray();
        activities.Length.ShouldBe(count);
        for (var index = 0; index < count; index++)
        {
            var result = await target.ReceiveAsync().WaitAsync(Deadline);
            result.IsError.ShouldBe(rejected);
            result.CausationId.ShouldBe(inputs[index].MessageId);
            Context(result).ShouldBe(activities[index].Context);
            activities[index].ParentSpanId.ShouldBe(Context(inputs[index]).SpanId);
            activities[index].GetTagItem("flow.message.count").ShouldBeNull();
            if (!rejected) result.Value!.Deserialize<MetricSnapshotOutput>()!.SampleCount.ShouldBe(index + 1);
        }
        target.TryReceive(out _).ShouldBeFalse();
        await target.Completion.WaitAsync(Deadline);
        traces.Started.ToArray().ShouldBe(activities);
    }

    [Theory]
    [InlineData("sample")]
    [InlineData("start")]
    [InlineData("stop")]
    public async Task Listener_failure_does_not_change_final_snapshot(string failure)
    {
        using var traces = new Traces(failure: failure);
        using var background = new Activity("background").SetIdFormat(ActivityIdFormat.W3C).Start();
        await using var node = new MetricsAggregateNode(new() { EmitEverySample = false });
        var target = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(target, new() { PropagateCompletion = true });
        var input = Sample(7);
        (await node.Input.SendAsync(input).WaitAsync(Deadline)).ShouldBeTrue();
        node.Complete();
        await node.Completion.WaitAsync(Deadline);
        var result = await target.ReceiveAsync().WaitAsync(Deadline);
        await target.Completion.WaitAsync(Deadline);
        result.IsError.ShouldBeFalse();
        result.CausationId.ShouldBe(input.MessageId);
        result.CorrelationId.ShouldBe(input.CorrelationId);
        result.Value.Deserialize<MetricSnapshotOutput>()!.TotalValue.ShouldBe(7);
        result.Headers["tenant"].ShouldBe("north");
        if (failure == "stop") Context(result).ShouldBe(traces.Stopped.Last().Context);
        else result.Headers.ShouldBeSameAs(input.Headers);
        traces.Stopped.ToArray().ShouldBe(traces.Started.ToArray());
        target.TryReceive(out _).ShouldBeFalse();
        Activity.Current.ShouldBeSameAs(background);
    }

    [Theory]
    [InlineData("drain")]
    [InlineData("fault")]
    [InlineData("decline")]
    public async Task Final_snapshot_scope_covers_admission_and_preserves_terminal_outcome(string terminal)
    {
        var started = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var traces = new Traces(started: activity =>
        {
            if (activity.ParentSpanId == default) started.TrySetResult(activity);
        }, stopped: activity =>
        {
            if (activity.ParentSpanId == default) stopped.TrySetResult(activity);
        });
        using var caller = new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start();
        await using var node = new MetricsAggregateNode(new()
        {
            EmitEverySample = false, BoundedCapacity = 1, MaxGroups = 1
        });
        var target = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 1 });
        using var link = node.Output.LinkTo(target, new() { PropagateCompletion = true });
        try
        {
            var inputs = Enumerable.Range(1, 4).Select(index => Sample(index, group: $"group-{index}")).ToArray();
            foreach (var input in inputs) (await node.Input.SendAsync(input).WaitAsync(Deadline)).ShouldBeTrue();
            node.Complete();
            var final = await started.Task.WaitAsync(Deadline);
            stopped.Task.IsCompleted.ShouldBeFalse();
            node.Completion.IsCompleted.ShouldBeFalse();
            target.Count.ShouldBe(1);
            final.Duration.ShouldBe(TimeSpan.Zero);
            if (terminal == "drain")
            {
                var first = await target.ReceiveAsync().WaitAsync(Deadline);
                first.CausationId.ShouldBe(inputs[1].MessageId);
                (await stopped.Task.WaitAsync(Deadline)).ShouldBeSameAs(final);
                final.GetTagItem("flow.processing.outcome").ShouldBe("snapshot");
                final.Status.ShouldBe(ActivityStatusCode.Unset);
                node.Completion.IsCompleted.ShouldBeFalse();
                for (var index = 2; index < 4; index++)
                {
                    var partial = await target.ReceiveAsync().WaitAsync(Deadline);
                    partial.Error!.Code.ShouldBe(MetricsErrorCodeNames.GroupLimitReached);
                    partial.CausationId.ShouldBe(inputs[index].MessageId);
                }
                var result = await target.ReceiveAsync().WaitAsync(Deadline);
                result.Value!.Deserialize<MetricSnapshotOutput>()!.TotalValue.ShouldBe(10);
                result.Value!.Deserialize<MetricSnapshotOutput>()!.SampleCount.ShouldBe(4);
                result.CausationId.ShouldBe(inputs[3].MessageId);
                Context(result).ShouldBe(final.Context);
                await node.Completion.WaitAsync(Deadline);
                await target.Completion.WaitAsync(Deadline);
            }
            else
            {
                var ownerFailure = new InvalidOperationException("Stop final snapshot");
                if (terminal == "fault") node.Fault(ownerFailure);
                else node.Output.Complete();
                var error = await Should.ThrowAsync<InvalidOperationException>(async () => await node.Completion.WaitAsync(Deadline));
                if (terminal == "fault") error.ShouldBeSameAs(ownerFailure);
                (await stopped.Task.WaitAsync(Deadline)).ShouldBeSameAs(final);
                if (terminal == "fault")
                {
                    final.GetTagItem("flow.processing.outcome").ShouldBe("canceled");
                    final.Status.ShouldBe(ActivityStatusCode.Unset);
                    final.GetTagItem("error.type").ShouldBeNull();
                }
                else
                {
                    final.Status.ShouldBe(ActivityStatusCode.Error);
                    final.GetTagItem("error.type").ShouldBe(typeof(InvalidOperationException).FullName);
                    final.GetTagItem("flow.processing.outcome").ShouldBeNull();
                }
                var propagated = await Should.ThrowAsync<InvalidOperationException>(async () => await target.Completion.WaitAsync(Deadline));
                if (terminal == "fault") propagated.ShouldBeSameAs(ownerFailure);
            }
            final.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
            final.Links.Single().Context.ShouldBe(Context(inputs[3]));
            traces.Started.ToArray().ShouldBe(traces.Stopped.ToArray());
            Activity.Current.ShouldBeSameAs(caller);
        }
        finally
        {
            if (!node.Completion.IsCompleted) node.Fault(new OperationCanceledException("Test cleanup"));
        }
    }

    private static FlowMessage Sample(double value, bool valid = true, long size = 1, string group = "default")
        => FlowMessage.Create(FlowValue.From(new MetricSampleInput { Value = value, Size = size, Group = group }),
            correlationId: CorrelationId.New(), headers: new Dictionary<string, string>
            {
                ["tenant"] = "north",
                ["traceparent"] = valid ? $"00-{ActivityTraceId.CreateRandom()}-{ActivitySpanId.CreateRandom()}-01" : "invalid",
                ["tracestate"] = "region=west"
            });

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
        internal Traces(ActivitySamplingResult sampling = ActivitySamplingResult.AllDataAndRecorded,
            string? failure = null, Action<Activity>? started = null, Action<Activity>? stopped = null)
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
