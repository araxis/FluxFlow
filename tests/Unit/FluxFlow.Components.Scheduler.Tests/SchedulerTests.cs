using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Scheduler.Tests;

public sealed class SchedulerTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Interval_emits_independent_signals_and_stops_without_reply()
    {
        var time = new FakeTimeProvider();
        await using var scheduler = new Scheduler(new()
        {
            NextOccurrence = Schedules.Every(TimeSpan.FromSeconds(5)), TimeProvider = time
        });
        var sink = Sink(scheduler);
        await scheduler.StartAsync();
        time.Advance(TimeSpan.FromSeconds(4));
        sink.Count.ShouldBe(0);
        time.Advance(TimeSpan.FromSeconds(1));
        var first = await sink.ReceiveAsync(Deadline);
        // Allow the producer continuation to arm its next timer deterministically.
        await AdvanceUntilMessage(time, sink);
        var second = await sink.ReceiveAsync(Deadline);
        first.Value.ShouldBeSameAs(JsonFlowValue.Null);
        second.MessageId.ShouldNotBe(first.MessageId);
        scheduler.Complete();
        await scheduler.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task One_off_finishes_and_long_wait_is_cancelable()
    {
        var time = new FakeTimeProvider();
        await using (var scheduler = new Scheduler(new()
        {
            NextOccurrence = Schedules.At(time.GetUtcNow().AddDays(100)), TimeProvider = time
        }))
        {
            var sink = Sink(scheduler);
            await scheduler.StartAsync();
            time.Advance(TimeSpan.FromDays(100));
            (await sink.ReceiveAsync(Deadline)).Value.ShouldBeSameAs(JsonFlowValue.Null);
            await scheduler.Completion.WaitAsync(Deadline);
        }
        await using var waiting = new Scheduler(new()
        {
            NextOccurrence = Schedules.At(time.GetUtcNow().AddYears(1)), TimeProvider = time
        });
        await waiting.StartAsync();
        waiting.Complete();
        await waiting.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Expired_schedule_emits_nothing()
    {
        var time = new FakeTimeProvider();
        await using var scheduler = new Scheduler(new()
        {
            NextOccurrence = Schedules.At(time.GetUtcNow().AddSeconds(-1)), TimeProvider = time
        });
        var sink = Sink(scheduler);
        await scheduler.StartAsync();
        await scheduler.Completion.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
    }

    [Fact]
    public void Cron_and_option_validation()
    {
        var start = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
        Schedules.Cron("*/5 * * * *")(start).ShouldBe(start.AddMinutes(5));
        Schedules.Cron("*/5 * * * * *", includeSeconds: true)(start).ShouldBe(start.AddSeconds(5));
        var zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(2), "test", "test");
        Schedules.Cron("0 13 * * *", zone)(start).ShouldBe(start.AddHours(1));
        Should.Throw<ArgumentOutOfRangeException>(() => Schedules.Every(TimeSpan.Zero));
        Should.Throw<ArgumentNullException>(() => new Scheduler(null!));
        Should.Throw<ArgumentNullException>(() => new Scheduler(new() { NextOccurrence = null! }));
        Should.Throw<ArgumentNullException>(() => new Scheduler(new() { NextOccurrence = Schedules.At(start), TimeProvider = null! }));
        Should.Throw<ArgumentOutOfRangeException>(() => new Scheduler(new() { NextOccurrence = Schedules.At(start), OutputCapacity = 0 }));
    }

    [Fact]
    public async Task Delayed_tick_emits_once_then_skips_missed_occurrences()
    {
        var time = new FakeTimeProvider();
        var start = time.GetUtcNow();
        var resumed = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scheduler = new Scheduler(new()
        {
            TimeProvider = time,
            NextOccurrence = after =>
            {
                if (after >= start.AddSeconds(20)) resumed.TrySetResult(after);
                return after.AddSeconds(5);
            }
        });
        var sink = Sink(scheduler);
        await scheduler.StartAsync();
        time.Advance(TimeSpan.FromSeconds(20));
        await sink.ReceiveAsync(Deadline);
        (await resumed.Task.WaitAsync(Deadline)).ShouldBe(start.AddSeconds(20));
        sink.Count.ShouldBe(0);
        scheduler.Complete();
        await scheduler.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Invalid_custom_schedule_faults_instead_of_spinning()
    {
        await using var scheduler = new Scheduler(new() { NextOccurrence = after => after });
        await scheduler.StartAsync();
        await Should.ThrowAsync<InvalidOperationException>(() => scheduler.Completion.WaitAsync(Deadline));
    }

    private static BufferBlock<FlowMessage> Sink(Scheduler scheduler)
    {
        var sink = new BufferBlock<FlowMessage>();
        scheduler.Output.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    private static async Task AdvanceUntilMessage(FakeTimeProvider time, BufferBlock<FlowMessage> sink)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        while (sink.Count == 0)
        {
            await Task.Delay(1, deadline.Token);
            time.Advance(TimeSpan.FromSeconds(5));
        }
    }
}
