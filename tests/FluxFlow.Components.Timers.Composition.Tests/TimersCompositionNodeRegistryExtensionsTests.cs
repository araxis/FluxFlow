using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Composition;
using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Timers.Composition.Tests;

public sealed class TimersCompositionNodeRegistryExtensionsTests
{
    [Fact]
    public void RegisterTimerNodes_registers_timer_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterTimerInterval()
            .RegisterTimerSchedule()
            .RegisterTimerDelay<InputMessage>()
            .RegisterTimerThrottle<InputMessage>()
            .RegisterTimerDebounce<InputMessage>();

        registry.Registrations[TimersCompositionNodeTypes.Interval]
            .Outputs[TimersCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(TimerTick));
        registry.Registrations[TimersCompositionNodeTypes.Schedule]
            .Outputs[TimersCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(ScheduleTick));

        AssertTransformMetadata(registry, TimersCompositionNodeTypes.Delay);
        AssertTransformMetadata(registry, TimersCompositionNodeTypes.Throttle);
        AssertTransformMetadata(registry, TimersCompositionNodeTypes.Debounce);
    }

    [Fact]
    public void RegisterTimerTransforms_supports_multiple_custom_node_types()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterTimerDelay<InputMessage>("timer.delay.input")
            .RegisterTimerDelay<string>("timer.delay.string")
            .RegisterTimerDebounce<InputMessage>("timer.debounce.input")
            .RegisterTimerThrottle<string>("timer.throttle.string");

        registry.Registrations["timer.delay.input"]
            .Inputs[TimersCompositionPortNames.Input].MessageType.ShouldBe(
                typeof(InputMessage));
        registry.Registrations["timer.delay.string"]
            .Inputs[TimersCompositionPortNames.Input].MessageType.ShouldBe(
                typeof(string));
        registry.Registrations["timer.debounce.input"]
            .Outputs[TimersCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(InputMessage));
        registry.Registrations["timer.throttle.string"]
            .Outputs[TimersCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(string));
    }

    [Fact]
    public async Task Hosted_interval_resolves_keyed_clock_and_emits_ticks()
    {
        var startedAt = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<TimeProvider>("fixed", clock);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "poll",
                    TimersCompositionNodeTypes.Interval,
                    node => node
                        .Resource(TimersCompositionResourceNames.Clock, "fixed")
                        .Configure("name", "poll")
                        .Configure("interval", TimeSpan.FromMilliseconds(10))
                        .Configure("emitImmediately", true)
                        .Configure("maxTicks", 2)
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterTimerInterval())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var runtime = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull();
        var intervalNode = runtime.Nodes.ShouldHaveSingleItem();
        var output = intervalNode.Descriptor.Outputs[TimersCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<TimerTick>>();
        var ticks = new BufferBlock<FlowMessage<TimerTick>>();
        var events = new BufferBlock<FlowEvent>();
        output.Source.LinkTo(ticks);
        intervalNode.Descriptor.Events.ShouldNotBeNull().LinkTo(events);

        var scheduled = clock.TimerScheduled;
        await runtime.StartAsync();
        await scheduled.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromMilliseconds(10));
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var first = await ticks.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await ticks.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var eventNames = Drain(events).Select(flowEvent => flowEvent.Name).ToArray();

        first.Payload.Name.ShouldBe("poll");
        first.Payload.Timestamp.ShouldBe(startedAt);
        second.Payload.Sequence.ShouldBe(2);
        second.Payload.Timestamp.ShouldBe(startedAt.AddMilliseconds(10));
        first.CorrelationId.ShouldNotBe(second.CorrelationId);
        eventNames.ShouldContain("timer.interval.started");
        eventNames.ShouldContain("timer.interval.tick");
        eventNames.ShouldContain("timer.interval.stopped");
    }

    [Fact]
    public async Task Hosted_schedule_binds_cron_and_emits_tick()
    {
        var startedAt = new DateTimeOffset(2026, 6, 2, 11, 59, 59, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<TimeProvider>("fixed", clock);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "schedule",
                    TimersCompositionNodeTypes.Schedule,
                    node => node
                        .Resource(TimersCompositionResourceNames.Clock, "fixed")
                        .Configure("name", "cron")
                        .Configure("cron", "* * * * * *")
                        .Configure("maxTicks", 1)
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterTimerSchedule())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var runtime = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull();
        var scheduleNode = runtime.Nodes.ShouldHaveSingleItem();
        var output = scheduleNode.Descriptor.Outputs[TimersCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<ScheduleTick>>();
        var ticks = new BufferBlock<FlowMessage<ScheduleTick>>();
        output.Source.LinkTo(ticks);

        var scheduled = clock.TimerScheduled;
        await runtime.StartAsync();
        await scheduled.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(1));
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var tick = await ticks.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        tick.Payload.Name.ShouldBe("cron");
        tick.Payload.Cron.ShouldBe("* * * * * *");
        tick.Payload.TimeZoneId.ShouldBe(TimeZoneInfo.Utc.Id);
        tick.Payload.DueAt.ShouldBe(startedAt.AddSeconds(1));
    }

    [Fact]
    public async Task Hosted_delay_preserves_correlation_and_binds_settings()
    {
        var clock = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var services = CreateTransformServices(
            TimersCompositionNodeTypes.Delay,
            node => node
                .Resource(TimersCompositionResourceNames.Clock, "fixed")
                .Configure("name", "hold")
                .Configure("delay", TimeSpan.FromMilliseconds(35))
                .Configure("boundedCapacity", 8),
            registry => registry.RegisterTimerDelay<InputMessage>(),
            clock);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var (input, output) = GetSingleTransform<InputMessage>(provider);
        var message = FlowMessage.Create(
            new InputMessage("one"),
            new CorrelationId("delay-correlation"));
        var scheduled = clock.TimerScheduled;

        (await input.Target.SendAsync(message)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        await scheduled.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromMilliseconds(35));

        var delayed = await output.Source.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        delayed.Payload.ShouldBe(message.Payload);
        delayed.CorrelationId.ShouldBe(new CorrelationId("delay-correlation"));
    }

    [Fact]
    public async Task Hosted_throttle_preserves_correlation_and_binds_settings()
    {
        var clock = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var services = CreateTransformServices(
            TimersCompositionNodeTypes.Throttle,
            node => node
                .Resource(TimersCompositionResourceNames.Clock, "fixed")
                .Configure("name", "rate")
                .Configure("interval", TimeSpan.FromMilliseconds(40))
                .Configure("emitFirstImmediately", false)
                .Configure("boundedCapacity", 8),
            registry => registry.RegisterTimerThrottle<InputMessage>(),
            clock);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var (input, output) = GetSingleTransform<InputMessage>(provider);
        var message = FlowMessage.Create(
            new InputMessage("one"),
            new CorrelationId("throttle-correlation"));
        var scheduled = clock.TimerScheduled;

        (await input.Target.SendAsync(message)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        await scheduled.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromMilliseconds(40));

        var throttled = await output.Source.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        throttled.Payload.ShouldBe(message.Payload);
        throttled.CorrelationId.ShouldBe(new CorrelationId("throttle-correlation"));
    }

    [Fact]
    public async Task Hosted_debounce_preserves_latest_correlation_and_binds_settings()
    {
        var clock = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var services = CreateTransformServices(
            TimersCompositionNodeTypes.Debounce,
            node => node
                .Resource(TimersCompositionResourceNames.Clock, "fixed")
                .Configure("name", "quiet")
                .Configure("quietPeriod", TimeSpan.FromMilliseconds(25))
                .Configure("boundedCapacity", 8),
            registry => registry.RegisterTimerDebounce<InputMessage>(),
            clock);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var (input, output) = GetSingleTransform<InputMessage>(provider);
        var first = FlowMessage.Create(
            new InputMessage("one"),
            new CorrelationId("debounce-old"));
        var latest = FlowMessage.Create(
            new InputMessage("two"),
            new CorrelationId("debounce-latest"));

        var scheduled1 = clock.TimerScheduled;
        (await input.Target.SendAsync(first)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        await scheduled1.WaitAsync(TimeSpan.FromSeconds(5));
        var scheduled2 = clock.TimerScheduled;
        (await input.Target.SendAsync(latest)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        await scheduled2.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromMilliseconds(25));

        var debounced = await output.Source.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        debounced.Payload.Value.ShouldBe("two");
        debounced.CorrelationId.ShouldBe(new CorrelationId("debounce-latest"));
    }

    [Fact]
    public async Task Invalid_timer_configuration_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "poll",
                    TimersCompositionNodeTypes.Interval,
                    node => node.Configure("interval", TimeSpan.Zero)))
                .Build())
            .RegisterNodes(registry => registry.RegisterTimerInterval())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains("Interval", StringComparison.Ordinal));
    }

    private static void AssertTransformMetadata(
        CompositionNodeRegistry registry,
        string nodeType)
    {
        registry.Registrations[nodeType]
            .Inputs[TimersCompositionPortNames.Input].MessageType.ShouldBe(
                typeof(InputMessage));
        registry.Registrations[nodeType]
            .Outputs[TimersCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(InputMessage));
    }

    private static IServiceCollection CreateTransformServices(
        string nodeType,
        Action<NodeDefinitionBuilder> configureNode,
        Action<CompositionNodeRegistry> registerNode,
        TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<TimeProvider>("fixed", clock);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "timer",
                    nodeType,
                    configureNode))
                .Build())
            .RegisterNodes(registerNode)
            .Configure(options => options.StartRuntimeWithHost = false);

        return services;
    }

    private static async Task BuildCompositionAsync(ServiceProvider provider)
    {
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();
        await hostedService.StartAsync(CancellationToken.None);
    }

    private static (
        CompositionInputPort<TMessage> Input,
        CompositionOutputPort<TMessage> Output) GetSingleTransform<TMessage>(
            IServiceProvider provider)
    {
        var runtime = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull();
        var timerNode = runtime.Nodes.ShouldHaveSingleItem();
        var input = timerNode.Descriptor.Inputs[TimersCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<TMessage>>();
        var output = timerNode.Descriptor.Outputs[TimersCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<TMessage>>();

        return (input, output);
    }

    private static List<T> Drain<T>(BufferBlock<T> sink)
    {
        var items = new List<T>();
        while (sink.TryReceive(out var item))
        {
            items.Add(item);
        }

        return items;
    }

    private sealed record InputMessage(string Value);

    private sealed class TrackingFakeTimeProvider : FakeTimeProvider
    {
        private readonly object _gate = new();
        private TaskCompletionSource _nextTimer = CreateSource();

        public TrackingFakeTimeProvider(DateTimeOffset startDateTime)
            : base(startDateTime)
        {
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            TaskCompletionSource signalled;
            lock (_gate)
            {
                signalled = _nextTimer;
                _nextTimer = CreateSource();
            }

            signalled.TrySetResult();
            return timer;
        }

        public Task TimerScheduled
        {
            get
            {
                lock (_gate)
                {
                    return _nextTimer.Task;
                }
            }
        }

        private static TaskCompletionSource CreateSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
