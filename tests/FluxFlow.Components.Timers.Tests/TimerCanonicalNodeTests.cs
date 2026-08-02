using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerCanonicalNodeTests
{
    [Fact]
    public async Task Interval_emits_flow_value_tick_without_error_port()
    {
        var startedAt = new DateTimeOffset(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = new TimerIntervalNode(
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
        var tick = message.Value;
        tick.Timestamp.ShouldBe(startedAt);
        tick.Name.ShouldBe("heartbeat");
        tick.Sequence.ShouldBe(1);
        tick.Interval.ShouldBe(TimeSpan.FromSeconds(5));
        message.CausationId.ShouldBeNull();
        typeof(TimerIntervalNode).GetProperty("Errors").ShouldBeNull();
    }

    [Fact]
    public async Task Schedule_emits_flow_value_tick()
    {
        var startedAt = new DateTimeOffset(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = new TimerScheduleNode(
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
            .ShouldHaveSingleItem().Value;
        tick.Cron.ShouldBe("* * * * * *");
        tick.TimeZoneId.ShouldBe(TimeZoneInfo.Utc.Id);
        tick.DueAt.ShouldBe(startedAt.AddSeconds(1));
        typeof(TimerScheduleNode).GetProperty("Errors").ShouldBeNull();
    }

    [Fact]
    public async Task Pre_canceled_interval_start_does_not_consume_start_state()
    {
        await using var node = new TimerIntervalNode(
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
        await using var node = new TimerDelayNode(
            new TimerDelaySettings { Delay = TimeSpan.FromMilliseconds(20) },
            clock);
        var output = TimerTestSink.Link(node.Output);
        var input = FlowMessage.Create(
            JsonSerializer.SerializeToElement("one"),
            new CorrelationId("delay-correlation"));
        var scheduled = clock.TimerScheduled;

        await node.Input.SendAsync(input);
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromMilliseconds(20));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var message = (await TimerTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        message.IsError.ShouldBeFalse();
        message.Value.ShouldBe(input.Value);
        message.CorrelationId.ShouldBe(input.CorrelationId);
        message.TraceId.ShouldBe(input.TraceId);
        message.CausationId.ShouldBe(input.MessageId);
        typeof(TimerDelayNode).GetProperty("Errors").ShouldBeNull();
    }

    [Fact]
    public async Task Delay_emits_failure_result_and_continues()
    {
        await using var node = new TimerDelayNode(
            new TimerDelaySettings { Delay = TimeSpan.FromMilliseconds(1) },
            new ThrowOnFirstTimerProvider());
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(JsonSerializer.SerializeToElement("bad")));
        await node.Input.SendAsync(FlowMessage.Create(JsonSerializer.SerializeToElement("good")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var results = await TimerTestSink.DrainUntilCompletedAsync(output);
        results.Count.ShouldBe(2);
        results[0].IsError.ShouldBeTrue();
        results[0].Error!.Code.ShouldBe(TimerErrorCodeNames.DelayFailed);
        results[1].IsError.ShouldBeFalse();
        results[1].Value.GetString().ShouldBe("good");
    }

    [Fact]
    public async Task Throttle_emits_failure_result_and_continues_in_order()
    {
        await using var node = new TimerThrottleNode(
            new TimerThrottleSettings
            {
                Interval = TimeSpan.FromMilliseconds(1),
                EmitFirstImmediately = false
            },
            new ThrowOnFirstTimerProvider());
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(JsonSerializer.SerializeToElement("bad")));
        await node.Input.SendAsync(FlowMessage.Create(JsonSerializer.SerializeToElement("good")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var results = await TimerTestSink.DrainUntilCompletedAsync(output);
        results.Select(item => item.IsError).ShouldBe([true, false]);
        results[0].Error!.Code.ShouldBe(TimerErrorCodeNames.ThrottleFailed);
        results[1].Value.GetString().ShouldBe("good");
        typeof(TimerThrottleNode).GetProperty("Errors").ShouldBeNull();
    }

    [Fact]
    public async Task Debounce_emits_only_latest_result_and_flushes_on_completion()
    {
        await using var node = new TimerDebounceNode(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromHours(1) });
        var output = TimerTestSink.Link(node.Output);
        var first = FlowMessage.Create(JsonSerializer.SerializeToElement("one"));
        var latest = FlowMessage.Create(JsonSerializer.SerializeToElement("two"));

        await node.Input.SendAsync(first);
        await node.Input.SendAsync(latest);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var message = (await TimerTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        message.IsError.ShouldBeFalse();
        message.Value.GetString().ShouldBe("two");
        message.CorrelationId.ShouldBe(latest.CorrelationId);
        message.CausationId.ShouldBe(latest.MessageId);
        typeof(TimerDebounceNode).GetProperty("Errors").ShouldBeNull();
    }

    [Fact]
    public async Task Debounce_emits_timer_creation_failure_once_and_continues()
    {
        await using var node = new TimerDebounceNode(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromMilliseconds(1) },
            new ThrowOnFirstTimerProvider());
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(JsonSerializer.SerializeToElement("bad")));
        await node.Input.SendAsync(FlowMessage.Create(JsonSerializer.SerializeToElement("good")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var results = await TimerTestSink.DrainUntilCompletedAsync(output);
        results.Count.ShouldBe(2);
        results[0].IsError.ShouldBeTrue();
        results[0].Error!.Code.ShouldBe(TimerErrorCodeNames.DebounceFailed);
        results[1].IsError.ShouldBeFalse();
        results[1].Value.GetString().ShouldBe("good");
    }

    [Fact]
    public async Task Debounce_timer_and_completion_race_emits_exactly_once()
    {
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var clock = new TrackingFakeTimeProvider();
            await using var node = new TimerDebounceNode(
                new TimerDebounceSettings { QuietPeriod = TimeSpan.FromMilliseconds(1) },
                clock);
            var output = TimerTestSink.Link(node.Output);
            var scheduled = clock.TimerScheduled;
            await node.Input.SendAsync(FlowMessage.Create(JsonSerializer.SerializeToElement(iteration)));
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
                .ShouldHaveSingleItem().Value.GetInt64().ShouldBe(iteration);
        }
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
