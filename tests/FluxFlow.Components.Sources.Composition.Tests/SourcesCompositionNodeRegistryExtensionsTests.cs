using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Sources.Composition;
using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Sources.Composition.Tests;

public sealed class SourcesCompositionNodeRegistryExtensionsTests
{
    [Fact]
    public void RegisterSourceNodes_registers_source_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterGeneratedSource<InputMessage>()
            .RegisterSequenceSource();

        registry.Registrations[SourcesCompositionNodeTypes.Generated]
            .Outputs[SourcesCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(InputMessage));
        registry.Registrations[SourcesCompositionNodeTypes.Sequence]
            .Outputs[SourcesCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(SourceSequenceItem));
    }

    [Fact]
    public void RegisterGeneratedSource_supports_multiple_custom_node_types()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterGeneratedSource<InputMessage>("source.generated.input")
            .RegisterGeneratedSource<string>("source.generated.string");

        registry.Registrations["source.generated.input"]
            .Outputs[SourcesCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(InputMessage));
        registry.Registrations["source.generated.string"]
            .Outputs[SourcesCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(string));
    }

    [Fact]
    public async Task Hosted_generated_source_binds_inline_items_and_emits_events()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "orders",
                    SourcesCompositionNodeTypes.Generated,
                    node => node
                        .Configure("name", "orders")
                        .Configure("outputType", "app.order")
                        .Configure("items", new[]
                        {
                            new InputMessage("A-100", 10),
                            new InputMessage("A-101", 20)
                        })
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry =>
                registry.RegisterGeneratedSource<InputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var runtime = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull();
        var sourceNode = runtime.Nodes.ShouldHaveSingleItem();
        var output = sourceNode.Descriptor.Outputs[SourcesCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var items = Link(output.Source);
        var events = Link(sourceNode.Descriptor.Events.ShouldNotBeNull());

        await runtime.StartAsync();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var emitted = await DrainUntilCompletedAsync(items);
        var eventNames = (await DrainUntilCompletedAsync(events))
            .Select(flowEvent => flowEvent.Name)
            .ToArray();

        emitted.Select(message => message.Payload.Id).ShouldBe(["A-100", "A-101"]);
        emitted.Select(message => message.Payload.Value).ShouldBe([10, 20]);
        emitted.Select(message => message.CorrelationId).Distinct().Count().ShouldBe(2);
        emitted.ShouldAllBe(message => !message.CorrelationId.IsEmpty);
        eventNames.ShouldContain("source.generated.started");
        eventNames.ShouldContain("source.generated.emitted");
        eventNames.ShouldContain("source.generated.completed");
    }

    [Fact]
    public async Task Hosted_generated_source_uses_keyed_clock_for_timing()
    {
        var startedAt = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<TimeProvider>("fixed", clock);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "feed",
                    SourcesCompositionNodeTypes.Generated,
                    node => node
                        .Resource(SourcesCompositionResourceNames.Clock, "fixed")
                        .Configure("name", "feed")
                        .Configure("outputType", "string")
                        .Configure("initialDelayMilliseconds", 15)
                        .Configure("intervalMilliseconds", 30)
                        .Configure("items", new[] { "one", "two" })))
                .Build())
            .RegisterNodes(registry =>
                registry.RegisterGeneratedSource<string>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var runtime = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull();
        var sourceNode = runtime.Nodes.ShouldHaveSingleItem();
        var output = sourceNode.Descriptor.Outputs[SourcesCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<string>>();
        var items = Link(output.Source);

        await runtime.StartAsync();
        await AdvanceUntilCompletedAsync(clock, runtime, TimeSpan.FromMilliseconds(30));

        var emitted = await DrainUntilCompletedAsync(items);

        emitted.Select(message => message.Payload).ShouldBe(["one", "two"]);
        clock.GetUtcNow().ShouldBe(startedAt.AddMilliseconds(60));
    }

    [Fact]
    public async Task Hosted_generated_source_missing_items_completes_empty()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "empty",
                    SourcesCompositionNodeTypes.Generated,
                    node => node
                        .Configure("name", "empty")
                        .Configure("outputType", "string")))
                .Build())
            .RegisterNodes(registry =>
                registry.RegisterGeneratedSource<string>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var runtime = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull();
        var sourceNode = runtime.Nodes.ShouldHaveSingleItem();
        var output = sourceNode.Descriptor.Outputs[SourcesCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<string>>();
        var items = Link(output.Source);

        await runtime.StartAsync();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await DrainUntilCompletedAsync(items)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Hosted_sequence_source_binds_settings_and_uses_keyed_clock()
    {
        var startedAt = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<TimeProvider>("fixed", clock);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "numbers",
                    SourcesCompositionNodeTypes.Sequence,
                    node => node
                        .Resource(SourcesCompositionResourceNames.Clock, "fixed")
                        .Configure("name", "numbers")
                        .Configure("start", 10)
                        .Configure("step", 5)
                        .Configure("count", 3)
                        .Configure("initialDelayMilliseconds", 10)
                        .Configure("intervalMilliseconds", 25)))
                .Build())
            .RegisterNodes(registry => registry.RegisterSequenceSource())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var runtime = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull();
        var sourceNode = runtime.Nodes.ShouldHaveSingleItem();
        var output = sourceNode.Descriptor.Outputs[SourcesCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<SourceSequenceItem>>();
        var items = Link(output.Source);

        await runtime.StartAsync();
        await AdvanceUntilCompletedAsync(clock, runtime, TimeSpan.FromMilliseconds(25));

        var emitted = await DrainUntilCompletedAsync(items);

        emitted.Select(message => message.Payload.Sequence).ShouldBe([1, 2, 3]);
        emitted.Select(message => message.Payload.Value).ShouldBe([10, 15, 20]);
        emitted.ShouldAllBe(message => message.Payload.Name == "numbers");
        emitted.ShouldAllBe(message => message.Payload.Timestamp >= startedAt);
        emitted.Select(message => message.CorrelationId).Distinct().Count().ShouldBe(3);
        emitted.ShouldAllBe(message => !message.CorrelationId.IsEmpty);
    }

    [Theory]
    [InlineData(SourcesCompositionNodeTypes.Generated, "maxItems")]
    [InlineData(SourcesCompositionNodeTypes.Sequence, "count")]
    public async Task Invalid_source_configuration_surfaces_factory_diagnostic(
        string nodeType,
        string expectedMessage)
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "source",
                    nodeType,
                    node =>
                    {
                        if (nodeType == SourcesCompositionNodeTypes.Generated)
                        {
                            node
                                .Configure("loop", true)
                                .Configure("items", new[] { "one" });
                        }
                        else
                        {
                            node.Configure("count", 0);
                        }
                    }))
                .Build())
            .RegisterNodes(registry => registry
                .RegisterGeneratedSource<string>()
                .RegisterSequenceSource())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task BuildCompositionAsync(ServiceProvider provider)
    {
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();
        await hostedService.StartAsync(CancellationToken.None);
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    private static async Task<List<T>> DrainUntilCompletedAsync<T>(
        BufferBlock<T> sink)
    {
        var items = new List<T>();
        while (await sink.OutputAvailableAsync().WaitAsync(TimeSpan.FromSeconds(5)))
        {
            while (sink.TryReceive(out var item))
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static async Task AdvanceUntilCompletedAsync(
        TrackingFakeTimeProvider clock,
        CompositionRuntime runtime,
        TimeSpan step)
    {
        var fired = 0;
        while (!runtime.Completion.IsCompleted)
        {
            var scheduled = clock.TimerScheduled;

            if (clock.CreatedTimerCount > fired)
            {
                clock.Advance(step);
                fired++;
                continue;
            }

            await Task.WhenAny(scheduled, runtime.Completion)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }

        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public sealed record InputMessage(string Id, int Value);

    private sealed class TrackingFakeTimeProvider : FakeTimeProvider
    {
        private readonly object _gate = new();
        private int _createdCount;
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
                _createdCount++;
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

        public int CreatedTimerCount
        {
            get
            {
                lock (_gate)
                {
                    return _createdCount;
                }
            }
        }

        private static TaskCompletionSource CreateSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
