using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core;
using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class DirectTimerGraphTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Direct_timer_sources_and_operators_use_canonical_ports()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero));
        var cronClock = new TrackingFakeTimeProvider(clock.GetUtcNow());
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("timers");
        var interval = new TimerIntervalNode(new TimerIntervalSettings
        {
            Interval = TimeSpan.FromSeconds(1),
            EmitImmediately = true,
            MaxTicks = 1
        }, clock);
        var schedule = new TimerScheduleNode(new TimerScheduleSettings
        {
            Cron = "* * * * * *",
            MaxTicks = 1
        }, cronClock);
        var delay = new TimerDelayNode(new TimerDelaySettings { Delay = TimeSpan.Zero }, clock);
        var throttle = new TimerThrottleNode(new TimerThrottleSettings { Interval = TimeSpan.FromSeconds(1) }, clock);
        var debounce = new TimerDebounceNode(new TimerDebounceSettings { QuietPeriod = TimeSpan.FromHours(1) }, clock);
        AssertSourcePorts(interval, interval.Output, interval.Events);
        AssertSourcePorts(schedule, schedule.Output, schedule.Events);
        AssertOperatorPorts(delay);
        AssertOperatorPorts(throttle);
        AssertOperatorPorts(debounce);
        var start = workflow.Add("interval", interval);
        var scheduled = workflow.Add("schedule", schedule);
        var delayed = workflow.Add("delay", delay);
        var throttled = workflow.Add("throttle", throttle);
        var debounced = workflow.Add("debounce", debounce);
        var ticks = new Sink();
        var cron = new Sink();
        start.Output.LinkTo(delayed.Input);
        delayed.Output.LinkTo(throttled.Input);
        throttled.Output.LinkTo(debounced.Input);
        debounced.Output.LinkTo(workflow.Add("ticks", ticks).Input);
        scheduled.Output.LinkTo(workflow.Add("cron", cron).Input);
        var armed = cronClock.TimerScheduled;

        await app.StartAsync();
        await armed.WaitAsync(Deadline);
        cronClock.Advance(TimeSpan.FromSeconds(1));
        var intervalMessage = await ticks.ReceiveAsync();
        var scheduleMessage = await cron.ReceiveAsync();
        await app.Completion.WaitAsync(Deadline);

        intervalMessage.IsError.ShouldBeFalse();
        intervalMessage.Value.Json.Deserialize<TimerIntervalTick>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Sequence.ShouldBe(1);
        intervalMessage.CausationId.ShouldBeNull();
        scheduleMessage.IsError.ShouldBeFalse();
        var scheduledTick = scheduleMessage.Value.Json.Deserialize<TimerScheduleTick>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        scheduledTick.Sequence.ShouldBe(1);
        scheduledTick.DueAt.ShouldBe(new DateTimeOffset(2026, 9, 9, 10, 0, 1, TimeSpan.Zero));
        ticks.Count.ShouldBe(0);
        cron.Count.ShouldBe(0);
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        await app.DisposeAsync();
        clock.ActiveTimers.ShouldBe(0);
        cronClock.ActiveTimers.ShouldBe(0);
    }

    [Fact]
    public async Task Direct_timer_chain_preserves_binary_and_error_identity()
    {
        var clock = new TrackingFakeTimeProvider();
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("main");
        var delay = workflow.Add("delay", new TimerDelayNode(
            new TimerDelaySettings { Delay = TimeSpan.FromSeconds(5) }, clock));
        var throttle = workflow.Add("throttle", new TimerThrottleNode(
            new TimerThrottleSettings { Interval = TimeSpan.FromSeconds(5) }, clock));
        var debounce = workflow.Add("debounce", new TimerDebounceNode(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromSeconds(5) }, clock));
        var sink = new Sink();
        delay.Output.LinkTo(throttle.Input);
        throttle.Output.LinkTo(debounce.Input);
        debounce.Output.LinkTo(workflow.Add("sink", sink).Input);
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(FlowContent.FromBytes(
            new byte[] { 0, 127, 255 }, "application/octet-stream"), new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            correlationId: new CorrelationId("binary"), traceId: new TraceId("binary-trace"));
        await app.StartAsync();

        var delayed = clock.TimerScheduled;
        (await app.SendAsync(delay.Input, input)).ShouldBeTrue();
        await delayed.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
        var quiet = clock.TimerScheduled;
        clock.Advance(TimeSpan.FromSeconds(5));
        await quiet.WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
        clock.Advance(TimeSpan.FromSeconds(5));
        (await sink.ReceiveAsync()).ShouldBeSameAs(input);

        var error = input.WithError(new FlowError("upstream.failed", "Expected upstream error.", "upstream"));
        (await app.SendAsync(delay.Input, error)).ShouldBeTrue();
        (await sink.ReceiveAsync()).ShouldBeSameAs(error);
        await app.StopAsync().WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Direct_debounce_stop_flushes_only_latest_input()
    {
        var clock = new TrackingFakeTimeProvider();
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var node = workflow.Add("latest", new TimerDebounceNode(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromHours(1) }, clock));
        var sink = new Sink();
        node.Output.LinkTo(workflow.Add("sink", sink).Input);
        await app.StartAsync();
        var messages = Enumerable.Range(1, 3).Select(value =>
            FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToArray();
        foreach (var message in messages)
        {
            var armed = clock.TimerScheduled;
            (await app.SendAsync(node.Input, message)).ShouldBeTrue();
            await armed.WaitAsync(Deadline);
        }

        sink.Count.ShouldBe(0);
        var stopping = app.StopAsync();
        (await sink.ReceiveAsync()).ShouldBeSameAs(messages[2]);
        await stopping.WaitAsync(Deadline);
        await app.DisposeAsync();
        clock.ActiveTimers.ShouldBe(0);
        clock.Advance(TimeSpan.FromHours(2));
        sink.Count.ShouldBe(0);
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Direct_delay_fault_joins_scheduled_work()
    {
        var clock = new TrackingFakeTimeProvider();
        var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var instance = new TimerDelayNode(new TimerDelaySettings { Delay = TimeSpan.FromHours(1) }, clock);
        var node = workflow.Add("delay", instance);
        var sink = new Sink();
        node.Output.LinkTo(workflow.Add("sink", sink).Input);
        await app.StartAsync();
        var armed = clock.TimerScheduled;
        (await app.SendAsync(node.Input, FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("pending", new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
        await armed.WaitAsync(Deadline);
        clock.ActiveTimers.ShouldBe(1);
        var failure = new InvalidOperationException("stop pending delay");

        instance.Fault(failure);
        (await Should.ThrowAsync<InvalidOperationException>(
            () => app.Completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask().WaitAsync(Deadline));
        await instance.DisposeAsync().AsTask().WaitAsync(Deadline);

        instance.Completion.IsFaulted.ShouldBeTrue();
        clock.ActiveTimers.ShouldBe(0);
        clock.Advance(TimeSpan.FromHours(2));
        sink.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Direct_debounce_fault_disposes_pending_timer()
    {
        var clock = new TrackingFakeTimeProvider();
        var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var instance = new TimerDebounceNode(new TimerDebounceSettings
        {
            QuietPeriod = TimeSpan.FromHours(1)
        }, clock);
        var node = workflow.Add("latest", instance);
        var sink = new Sink();
        node.Output.LinkTo(workflow.Add("sink", sink).Input);
        await app.StartAsync();
        var armed = clock.TimerScheduled;
        (await app.SendAsync(node.Input, FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("pending", new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
        await armed.WaitAsync(Deadline);
        clock.ActiveTimers.ShouldBe(1);
        var failure = new InvalidOperationException("stop pending debounce");

        instance.Fault(failure);
        (await Should.ThrowAsync<InvalidOperationException>(
            () => app.Completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask().WaitAsync(Deadline));
        await instance.DisposeAsync().AsTask().WaitAsync(Deadline);

        instance.Completion.IsFaulted.ShouldBeTrue();
        clock.ActiveTimers.ShouldBe(0);
        clock.Advance(TimeSpan.FromHours(2));
        sink.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Direct_throttle_applies_bounded_admission_and_cancellation()
    {
        var clock = new TrackingFakeTimeProvider();
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("main");
        var instance = new TimerThrottleNode(new TimerThrottleSettings
        {
            Interval = TimeSpan.FromSeconds(5),
            EmitFirstImmediately = false,
            BoundedCapacity = 2
        }, clock);
        var node = workflow.Add("throttle", instance);
        var sink = new Sink();
        node.Output.LinkTo(workflow.Add("sink", sink).Input);
        var first = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("first", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var second = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("second", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        await app.StartAsync();

        var armed = clock.TimerScheduled;
        (await app.SendAsync(node.Input, first)).ShouldBeTrue();
        await armed.WaitAsync(Deadline);
        (await app.SendAsync(node.Input, second)).ShouldBeTrue();
        using var cancellation = new CancellationTokenSource();
        var pending = app.SendAsync(node.Input, FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("canceled", new JsonSerializerOptions(JsonSerializerDefaults.Web)))),
            cancellation.Token).AsTask();
        pending.IsCompleted.ShouldBeFalse();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => pending.WaitAsync(Deadline));
        sink.Count.ShouldBe(0);

        var next = clock.TimerScheduled;
        clock.Advance(TimeSpan.FromSeconds(5));
        (await sink.ReceiveAsync()).ShouldBeSameAs(first);
        await next.WaitAsync(Deadline);
        clock.Advance(TimeSpan.FromSeconds(5));
        (await sink.ReceiveAsync()).ShouldBeSameAs(second);
        await app.StopAsync().WaitAsync(Deadline);
        sink.Count.ShouldBe(0);
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        clock.ActiveTimers.ShouldBe(0);
    }

    private static void AssertOperatorPorts(FlowNode node)
    {
        node.Ports.ShouldBeSameAs(node.Ports);
        node.Ports.Inputs.ShouldHaveSingleItem().Key.ShouldBe("Input");
        node.Ports.Inputs["Input"].ShouldBeSameAs(node.Input);
        node.Ports.Outputs.ShouldHaveSingleItem().Key.ShouldBe("Output");
        node.Ports.Outputs["Output"].ShouldBeSameAs(node.Output);
        node.Ports.Events.ShouldBeSameAs(node.Events);
        node.Ports.Controls.ShouldBeEmpty();
    }

    private static void AssertSourcePorts(IFlowComponent node, ISourceBlock<FlowMessage> output,
        ISourceBlock<FlowMessage> events)
    {
        node.Ports.ShouldBeSameAs(node.Ports);
        node.Ports.Inputs.ShouldBeEmpty();
        node.Ports.Controls.ShouldBeEmpty();
        node.Ports.Outputs.ShouldHaveSingleItem().Key.ShouldBe("Output");
        node.Ports.Outputs["Output"].ShouldBeSameAs(output);
        node.Ports.Events.ShouldBeSameAs(events);
    }

    private sealed class Sink : IFlowComponent
    {
        private readonly BufferBlock<FlowMessage> _input = new(new DataflowBlockOptions { BoundedCapacity = 2 });
        public Sink() => Ports = new FlowPorts(
            inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = _input });
        public FlowPorts Ports { get; }
        public int Count => _input.Count;
        public Task Completion => _input.Completion;
        public Task<FlowMessage> ReceiveAsync() => _input.ReceiveAsync().WaitAsync(Deadline);
        public void Complete() => _input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_input).Fault(error);
        public async ValueTask DisposeAsync()
        {
            Complete();
            try { await Completion; }
            catch { /* The graph observes faulted completion. */ }
        }
    }
}
