using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class FlowValueTimerNodeTests
{
    [Fact]
    public async Task Interval_emits_flow_value_tick_without_error_port()
    {
        var startedAt = new DateTimeOffset(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = new FlowValueTimerIntervalNode(
            new TimerIntervalSettings
            {
                Name = "heartbeat",
                Interval = TimeSpan.FromSeconds(5),
                EmitImmediately = true,
                MaxTicks = 1
            },
            clock);
        var output = TimerTestSink.Link(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var message = (await TimerTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var tick = message.Payload.GetObject();
        tick["timestamp"].GetDateTimeOffset().ShouldBe(startedAt);
        tick["name"].GetString().ShouldBe("heartbeat");
        tick["sequence"].GetInteger().ShouldBe(1);
        tick["interval"].GetDuration().ShouldBe(TimeSpan.FromSeconds(5));
        message.CausationId.ShouldBeNull();
        typeof(FlowValueTimerIntervalNode).GetProperty("Errors").ShouldBeNull();
    }

    [Fact]
    public async Task Schedule_emits_flow_value_tick()
    {
        var startedAt = new DateTimeOffset(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = new FlowValueTimerScheduleNode(
            new TimerScheduleSettings
            {
                Name = "cron",
                Cron = "* * * * * *",
                MaxTicks = 1
            },
            clock);
        var output = TimerTestSink.Link(node.Output);
        var scheduled = clock.TimerScheduled;

        await node.StartAsync();
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromSeconds(1));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var tick = (await TimerTestSink.DrainUntilCompletedAsync(output))
            .ShouldHaveSingleItem().Payload.GetObject();
        tick["cron"].GetString().ShouldBe("* * * * * *");
        tick["timeZoneId"].GetString().ShouldBe(TimeZoneInfo.Utc.Id);
        tick["dueAt"].GetDateTimeOffset().ShouldBe(startedAt.AddSeconds(1));
        typeof(FlowValueTimerScheduleNode).GetProperty("Errors").ShouldBeNull();
    }

    [Fact]
    public async Task Pre_canceled_interval_start_does_not_consume_start_state()
    {
        await using var node = new FlowValueTimerIntervalNode(
            new TimerIntervalSettings
            {
                Interval = TimeSpan.FromSeconds(1),
                EmitImmediately = true,
                MaxTicks = 1
            });
        var output = TimerTestSink.Link(node.Output);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => node.StartAsync(cancellation.Token));
        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await TimerTestSink.DrainUntilCompletedAsync(output)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Delay_emits_success_result_with_lineage()
    {
        var clock = new TrackingFakeTimeProvider();
        await using var node = new FlowValueTimerDelayNode(
            new TimerDelaySettings { Delay = TimeSpan.FromMilliseconds(20) },
            clock);
        var output = TimerTestSink.Link(node.Output);
        var input = FlowMessage.Create(
            FlowValue.From("one"),
            new CorrelationId("delay-correlation"));
        var scheduled = clock.TimerScheduled;

        await node.Input.SendAsync(input);
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromMilliseconds(20));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var message = (await TimerTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        message.Payload.Kind.ShouldBe(TimerResultKinds.Delayed);
        message.Payload.IsError.ShouldBeFalse();
        message.Payload.Value.ShouldBe(input.Payload);
        message.CorrelationId.ShouldBe(input.CorrelationId);
        message.TraceId.ShouldBe(input.TraceId);
        message.CausationId.ShouldBe(input.MessageId);
        typeof(FlowValueTimerDelayNode).GetProperty("Errors").ShouldBeNull();
    }

    [Fact]
    public async Task Delay_emits_failure_result_and_continues()
    {
        await using var node = new FlowValueTimerDelayNode(
            new TimerDelaySettings { Delay = TimeSpan.FromMilliseconds(1) },
            new ThrowOnFirstTimerProvider());
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("bad")));
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("good")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var results = await TimerTestSink.DrainUntilCompletedAsync(output);
        results.Count.ShouldBe(2);
        results[0].Payload.Kind.ShouldBe(TimerResultKinds.DelayFailed);
        results[0].Payload.Error!.Code.ShouldBe(TimerErrorCodeNames.DelayFailed);
        results[1].Payload.Kind.ShouldBe(TimerResultKinds.Delayed);
        results[1].Payload.Value!.GetString().ShouldBe("good");
    }

    [Fact]
    public async Task Throttle_emits_failure_result_and_continues_in_order()
    {
        await using var node = new FlowValueTimerThrottleNode(
            new TimerThrottleSettings
            {
                Interval = TimeSpan.FromMilliseconds(1),
                EmitFirstImmediately = false
            },
            new ThrowOnFirstTimerProvider());
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("bad")));
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("good")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var results = await TimerTestSink.DrainUntilCompletedAsync(output);
        results.Select(item => item.Payload.Kind).ShouldBe([
            TimerResultKinds.ThrottleFailed,
            TimerResultKinds.Throttled
        ]);
        results[0].Payload.Error!.Code.ShouldBe(TimerErrorCodeNames.ThrottleFailed);
        results[1].Payload.Value!.GetString().ShouldBe("good");
        typeof(FlowValueTimerThrottleNode).GetProperty("Errors").ShouldBeNull();
    }

    [Fact]
    public async Task Debounce_emits_only_latest_result_and_flushes_on_completion()
    {
        await using var node = new FlowValueTimerDebounceNode(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromHours(1) });
        var output = TimerTestSink.Link(node.Output);
        var first = FlowMessage.Create(FlowValue.From("one"));
        var latest = FlowMessage.Create(FlowValue.From("two"));

        await node.Input.SendAsync(first);
        await node.Input.SendAsync(latest);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var message = (await TimerTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        message.Payload.Kind.ShouldBe(TimerResultKinds.Debounced);
        message.Payload.Value!.GetString().ShouldBe("two");
        message.CorrelationId.ShouldBe(latest.CorrelationId);
        message.CausationId.ShouldBe(latest.MessageId);
        typeof(FlowValueTimerDebounceNode).GetProperty("Errors").ShouldBeNull();
    }

    [Fact]
    public async Task Debounce_emits_timer_creation_failure_once_and_continues()
    {
        await using var node = new FlowValueTimerDebounceNode(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromMilliseconds(1) },
            new ThrowOnFirstTimerProvider());
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("bad")));
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("good")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var results = await TimerTestSink.DrainUntilCompletedAsync(output);
        results.Count.ShouldBe(2);
        results[0].Payload.Kind.ShouldBe(TimerResultKinds.DebounceFailed);
        results[0].Payload.Error!.Code.ShouldBe(TimerErrorCodeNames.DebounceFailed);
        results[1].Payload.Kind.ShouldBe(TimerResultKinds.Debounced);
        results[1].Payload.Value!.GetString().ShouldBe("good");
    }

    [Fact]
    public async Task Debounce_timer_and_completion_race_emits_exactly_once()
    {
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var clock = new TrackingFakeTimeProvider();
            await using var node = new FlowValueTimerDebounceNode(
                new TimerDebounceSettings { QuietPeriod = TimeSpan.FromMilliseconds(1) },
                clock);
            var output = TimerTestSink.Link(node.Output);
            var scheduled = clock.TimerScheduled;
            await node.Input.SendAsync(FlowMessage.Create(FlowValue.From(iteration)));
            await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
            using var barrier = new Barrier(2);

            var advance = Task.Run(() =>
            {
                barrier.SignalAndWait();
                clock.Advance(TimeSpan.FromMilliseconds(1));
            });
            var complete = Task.Run(() =>
            {
                barrier.SignalAndWait();
                node.Complete();
            });

            await Task.WhenAll(advance, complete).WaitAsync(TimeSpan.FromSeconds(30));
            await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
            (await TimerTestSink.DrainUntilCompletedAsync(output))
                .ShouldHaveSingleItem().Payload.Value!.GetInteger().ShouldBe(iteration);
        }
    }

    [Fact]
    public async Task Missing_input_is_a_normal_failure_result()
    {
        await using var node = new FlowValueTimerDelayNode(
            new TimerDelaySettings { Delay = TimeSpan.Zero });
        var output = TimerTestSink.Link(node.Output);
        await node.Input.SendAsync(FlowMessage.Create<FlowValue>(null!));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var result = (await TimerTestSink.DrainUntilCompletedAsync(output))
            .ShouldHaveSingleItem().Payload;
        result.IsError.ShouldBeTrue();
        result.Error!.Code.ShouldBe(TimerErrorCodeNames.MissingInput);
    }

    private sealed class ThrowOnFirstTimerProvider : TimeProvider
    {
        private int _calls;

        public override DateTimeOffset GetUtcNow()
            => new(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new InvalidOperationException("clock failed");

            return System.CreateTimer(callback, state, dueTime, period);
        }
    }
}
