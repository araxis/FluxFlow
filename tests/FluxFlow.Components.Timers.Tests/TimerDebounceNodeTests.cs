using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerDebounceNodeTests
{
    [Fact]
    public async Task Debounce_emits_latest_pending_on_completion_with_lineage()
    {
        await using var node = new TimerDebounceNode(
            new TimerDebounceSettings
            {
                Name = "quiet",
                QuietPeriod = TimeSpan.FromMilliseconds(40),
                BoundedCapacity = 4
            });
        var output = TimerTestSink.Link(node.Output);
        var first = FlowMessage.Create(JsonSerializer.SerializeToElement("one"));
        var latest = FlowMessage.Create(JsonSerializer.SerializeToElement("two"));

        await node.Input.SendAsync(first);
        await node.Input.SendAsync(latest);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var emitted = (await TimerTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        emitted.Value.GetString().ShouldBe("two");
        emitted.CorrelationId.ShouldBe(latest.CorrelationId);
        emitted.TraceId.ShouldBe(latest.TraceId);
        emitted.CausationId.ShouldBe(latest.MessageId);
    }

    [Fact]
    public async Task Debounce_emits_after_quiet_period_elapses()
    {
        var clock = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerDebounceNode(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromMilliseconds(40) },
            clock);
        var output = TimerTestSink.Link(node.Output);

        var scheduled = clock.TimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create(JsonSerializer.SerializeToElement("one")));
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        output.TryReceive(out _).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(40));

        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Value.GetString().ShouldBe("one");
    }

    [Fact]
    public async Task Debounce_flushes_pending_input_on_completion()
    {
        await using var node = new TimerDebounceNode(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromSeconds(1000) });
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(JsonSerializer.SerializeToElement("one")));
        node.Complete();

        var value = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        value.Value.GetString().ShouldBe("one");
    }

    [Fact]
    public async Task Debounce_emits_latest_per_quiet_window()
    {
        var clock = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerDebounceNode(
            new TimerDebounceSettings
            {
                QuietPeriod = TimeSpan.FromMilliseconds(25),
                BoundedCapacity = 8
            },
            clock);
        var output = TimerTestSink.Link(node.Output);

        var scheduled1 = clock.TimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create(JsonSerializer.SerializeToElement(1)));
        await scheduled1.WaitAsync(TimeSpan.FromSeconds(30));
        var scheduled2 = clock.TimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create(JsonSerializer.SerializeToElement(2)));
        await scheduled2.WaitAsync(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromMilliseconds(25));
        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));

        var scheduled3 = clock.TimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create(JsonSerializer.SerializeToElement(3)));
        await scheduled3.WaitAsync(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromMilliseconds(25));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));

        first.Value.GetInt64().ShouldBe(2);
        second.Value.GetInt64().ShouldBe(3);
    }

    [Fact]
    public async Task Debounce_timer_and_completion_race_emits_pending_value_exactly_once()
    {
        for (var iteration = 0; iteration < 50; iteration++)
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

    [Fact]
    public async Task Debounce_emits_result_event()
    {
        await using var node = new TimerDebounceNode(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromMilliseconds(1) });
        var output = TimerTestSink.Link(node.Output);
        var events = TimerTestSink.Link(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(JsonSerializer.SerializeToElement("hello")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await TimerTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var flowEvent = (await TimerTestSink.DrainUntilCompletedAsync(events))
            .ShouldHaveSingleItem();
        flowEvent.Name.ShouldBe(TimerDiagnosticNames.DebounceEmitted);
        flowEvent.Attributes["resultKind"].ShouldBe(TimerResultKinds.Debounced);
        flowEvent.Attributes["nodeType"].ShouldBe("timer.debounce");
    }

    [Fact]
    public async Task Debounce_dispose_flushes_and_completes_output()
    {
        await using var node = new TimerDebounceNode(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromSeconds(1000) });
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(JsonSerializer.SerializeToElement("one")));
        await node.DisposeAsync();

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        (await TimerTestSink.DrainUntilCompletedAsync(output))
            .Select(message => message.Value.GetString())
            .ShouldBe(["one"]);
    }

    [Fact]
    public async Task Debounce_dispose_after_fault_does_not_throw()
    {
        var node = new TimerDebounceNode(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromMilliseconds(1) });
        TimerTestSink.Link(node.Output);

        node.Fault(new InvalidOperationException("boom"));
        await node.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));

        await Should.ThrowAsync<InvalidOperationException>(() => node.Completion);
    }

    [Fact]
    public void Debounce_rejects_non_positive_quiet_period()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerDebounceNode(
                new TimerDebounceSettings { QuietPeriod = TimeSpan.Zero }))
            .Message.ShouldContain("QuietPeriod");

    [Fact]
    public void Debounce_rejects_invalid_bounded_capacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerDebounceNode(
                new TimerDebounceSettings
                {
                    QuietPeriod = TimeSpan.FromMilliseconds(1),
                    BoundedCapacity = 0
                }))
            .Message.ShouldContain("BoundedCapacity");

    [Fact]
    public void Debounce_rejects_null_settings()
        => Should.Throw<ArgumentNullException>(() => new TimerDebounceNode(null!));
}
