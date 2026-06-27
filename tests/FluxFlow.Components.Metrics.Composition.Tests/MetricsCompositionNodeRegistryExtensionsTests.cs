using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Metrics;
using FluxFlow.Components.Metrics.Composition;
using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Diagnostics;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Metrics.Composition.Tests;

public sealed class MetricsCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void RegisterMetricsAggregate_registers_request_result_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterMetricsAggregate();

        var registration = registry.Registrations[MetricsCompositionNodeTypes.Aggregate];
        registration.Inputs[MetricsCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(MetricSampleInput));
        registration.Outputs[MetricsCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(MetricSnapshotOutput));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_metrics_metadata()
    {
        var metadata = MetricsDesignMetadata();

        metadata.Type.Value.ShouldBe(MetricsCompositionNodeTypes.Aggregate);
        metadata.DisplayName.ShouldBe("Metrics Aggregate");
        metadata.Category.ShouldBe("Metrics");
        metadata.SuggestedEditorWidth.ShouldBe(460);
        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Options.ShouldNotContain(option =>
            option.Name == MetricsCompositionResourceNames.Clock);
        AssertClockResource(metadata);
    }

    [Fact]
    public void Design_metadata_provider_describes_metrics_ports()
    {
        var metadata = MetricsDesignMetadata();

        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(MetricsCompositionPortNames.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType.ShouldBe(nameof(MetricSampleInput));
        input.IsPrimary.ShouldBeTrue();

        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(MetricsCompositionPortNames.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(1);
        output.ValueType.ShouldBe(nameof(MetricSnapshotOutput));
        output.IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public void Design_metadata_provider_describes_metrics_options()
    {
        var metadata = MetricsDesignMetadata();
        var defaults = new MetricsAggregateOptions();

        metadata.Options.Select(option => option.Name).ShouldBe([
            "rateWindowSeconds",
            "boundedCapacity",
            "maxGroups",
            "emitEverySample",
            "trackLatest",
            "trackMinMax",
            "trackSize",
            "groupByTag",
            "treatMissingValueAsZero"
        ], ignoreOrder: false);

        AssertOption(
            metadata,
            "rateWindowSeconds",
            OptionValueKind.Number,
            defaults.RateWindowSeconds,
            min: 0.000001);
        AssertOption(
            metadata,
            "boundedCapacity",
            OptionValueKind.Number,
            defaults.BoundedCapacity,
            min: 1);
        AssertOption(
            metadata,
            "maxGroups",
            OptionValueKind.Number,
            defaults.MaxGroups,
            min: 0);
        AssertOption(
            metadata,
            "emitEverySample",
            OptionValueKind.Boolean,
            defaults.EmitEverySample);
        AssertOption(
            metadata,
            "trackLatest",
            OptionValueKind.Boolean,
            defaults.TrackLatest);
        AssertOption(
            metadata,
            "trackMinMax",
            OptionValueKind.Boolean,
            defaults.TrackMinMax);
        AssertOption(
            metadata,
            "trackSize",
            OptionValueKind.Boolean,
            defaults.TrackSize);
        AssertOption(metadata, "groupByTag", OptionValueKind.Text, defaultValue: null);
        AssertOption(
            metadata,
            "treatMissingValueAsZero",
            OptionValueKind.Boolean,
            defaults.TreatMissingValueAsZero);
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var provider = new MetricsComponentDesignMetadataProvider();
        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.All.ShouldHaveSingleItem();
        catalog.TryGet(
            new ComponentType(MetricsCompositionNodeTypes.Aggregate),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull()
            .DisplayName.ShouldBe("Metrics Aggregate");
    }

    [Fact]
    public async Task Hosted_metrics_aggregate_binds_options_groups_and_preserves_correlation_id()
    {
        var start = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        await WithNodeAsync(async (input, output, _) =>
        {
            var snapshots = Link(output.Source);
            var first = FlowMessage.Create(new MetricSampleInput
            {
                Timestamp = start,
                Name = "messages",
                Value = 2,
                Size = 10,
                Tags = new Dictionary<string, string> { ["topic"] = "sensors/a" }
            });
            var second = FlowMessage.Create(
                new MetricSampleInput
                {
                    Timestamp = start.AddSeconds(1),
                    Name = "messages",
                    Value = 4,
                    Size = 20,
                    Tags = new Dictionary<string, string> { ["topic"] = "sensors/b" }
                },
                new CorrelationId("second"));

            (await input.Target.SendAsync(first).WaitAsync(Timeout)).ShouldBeTrue();
            (await input.Target.SendAsync(second).WaitAsync(Timeout)).ShouldBeTrue();

            await snapshots.ReceiveAsync().WaitAsync(Timeout);
            var snapshot = await snapshots.ReceiveAsync().WaitAsync(Timeout);

            snapshot.CorrelationId.ShouldBe(second.CorrelationId);
            snapshot.Payload.SampleCount.ShouldBe(2);
            snapshot.Payload.ValueCount.ShouldBe(2);
            snapshot.Payload.TotalValue.ShouldBe(6);
            snapshot.Payload.AverageValue.ShouldBe(3);
            snapshot.Payload.TotalSize.ShouldBe(30);
            snapshot.Payload.Groups.Keys.ShouldBe(["sensors/a", "sensors/b"], ignoreOrder: true);
            snapshot.Payload.Groups["sensors/a"].TotalSize.ShouldBe(10);
            snapshot.Payload.Groups["sensors/b"].TotalSize.ShouldBe(20);
        },
        node => node
            .Configure("rateWindowSeconds", 10)
            .Configure("groupByTag", "topic"));
    }

    [Fact]
    public async Task Hosted_metrics_aggregate_uses_optional_keyed_clock_for_missing_timestamps()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:00:42Z");
        var clock = new FakeTimeProvider(timestamp);

        await WithNodeAsync(
            async (input, output, _) =>
            {
                var snapshots = Link(output.Source);

                (await input.Target.SendAsync(FlowMessage.Create(new MetricSampleInput
                    {
                        Name = "items",
                        Value = 1
                    }))
                    .WaitAsync(Timeout)).ShouldBeTrue();

                var snapshot = await snapshots.ReceiveAsync().WaitAsync(Timeout);
                snapshot.Payload.Timestamp.ShouldBe(timestamp);
                snapshot.Payload.Latest.ShouldNotBeNull().Timestamp.ShouldBe(timestamp);
                snapshot.Payload.Groups["default"].LatestTimestamp.ShouldBe(timestamp);
            },
            node => node.Resource(MetricsCompositionResourceNames.Clock, "fixed"),
            services => services.AddKeyedSingleton<TimeProvider>("fixed", clock));
    }

    [Fact]
    public async Task Hosted_metrics_aggregate_emits_coalesced_final_snapshot_on_completion()
    {
        await WithNodeAsync(async (input, output, descriptor) =>
        {
            var snapshots = Link(output.Source);

            (await input.Target.SendAsync(FlowMessage.Create(new MetricSampleInput { Value = 1 }))
                .WaitAsync(Timeout)).ShouldBeTrue();
            (await input.Target.SendAsync(FlowMessage.Create(new MetricSampleInput { Value = 2 }))
                .WaitAsync(Timeout)).ShouldBeTrue();

            descriptor.Node.Complete();
            await descriptor.Completion.WaitAsync(Timeout);

            var snapshot = await snapshots.ReceiveAsync().WaitAsync(Timeout);
            snapshot.Payload.SampleCount.ShouldBe(2);
            snapshot.Payload.TotalValue.ShouldBe(3);
            snapshots.TryReceive(out _).ShouldBeFalse();
        },
        node => node.Configure("emitEverySample", false));
    }

    [Fact]
    public async Task Hosted_metrics_aggregate_exposes_events()
    {
        await WithNodeAsync(async (input, output, descriptor) =>
        {
            output.Source.LinkTo(DataflowBlock.NullTarget<FlowMessage<MetricSnapshotOutput>>());
            var events = Link(descriptor.Events.ShouldNotBeNull());
            var message = FlowMessage.Create(new MetricSampleInput
            {
                Value = 1,
                Group = "items"
            });

            (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();

            var @event = await events.ReceiveAsync().WaitAsync(Timeout);
            @event.Name.ShouldBe(MetricsDiagnosticNames.AggregateUpdated);
            @event.CorrelationId.ShouldBe(message.CorrelationId);
            @event.Attributes["sampleCount"].ShouldBe(1L);
        });
    }

    [Fact]
    public async Task Hosted_metrics_aggregate_emits_errors_and_continues_after_invalid_sample()
    {
        await WithNodeAsync(async (input, output, descriptor) =>
        {
            var snapshots = Link(output.Source);
            var errors = Link(descriptor.Errors.ShouldNotBeNull());
            var bad = FlowMessage.Create(
                new MetricSampleInput { Size = -1 },
                new CorrelationId("bad"));
            var good = FlowMessage.Create(
                new MetricSampleInput { Size = 3 },
                new CorrelationId("good"));

            (await input.Target.SendAsync(bad).WaitAsync(Timeout)).ShouldBeTrue();
            (await input.Target.SendAsync(good).WaitAsync(Timeout)).ShouldBeTrue();

            var error = await errors.ReceiveAsync().WaitAsync(Timeout);
            var snapshot = await snapshots.ReceiveAsync().WaitAsync(Timeout);

            error.Code.ShouldBe(MetricsErrorCodes.InvalidSample);
            error.CorrelationId.ShouldBe(bad.CorrelationId);
            snapshot.CorrelationId.ShouldBe(good.CorrelationId);
            snapshot.Payload.SampleCount.ShouldBe(1);
            snapshot.Payload.TotalSize.ShouldBe(3);
        });
    }

    [Fact]
    public async Task Invalid_configuration_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "metrics",
                    MetricsCompositionNodeTypes.Aggregate,
                    node => node.Configure("boundedCapacity", 0)))
                .Build())
            .RegisterNodes(registry => registry.RegisterMetricsAggregate())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(
                "greater than zero",
                StringComparison.OrdinalIgnoreCase));
    }

    private static ComponentDesignMetadata MetricsDesignMetadata()
        => new MetricsComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

    private static void AssertOption(
        ComponentDesignMetadata metadata,
        string name,
        OptionValueKind kind,
        object? defaultValue,
        double? min = null)
    {
        var option = metadata.Options.Single(option => option.Name == name);
        option.Kind.ShouldBe(kind);
        option.DefaultValue.ShouldBe(defaultValue);
        option.Min.ShouldBe(min);
    }

    private static void AssertClockResource(ComponentDesignMetadata metadata)
    {
        var resource = metadata.Resources.ShouldHaveSingleItem();

        resource.Name.Value.ShouldBe(MetricsCompositionResourceNames.Clock);
        resource.DisplayName.ShouldBe("Clock");
        resource.Order.ShouldBe(0);
        resource.IsRequired.ShouldBeFalse();
        resource.ValueType.ShouldBe(nameof(TimeProvider));
    }

    private static async Task WithNodeAsync(
        Func<
            CompositionInputPort<MetricSampleInput>,
            CompositionOutputPort<MetricSnapshotOutput>,
            ComposedNode,
            Task> run,
        Action<NodeDefinitionBuilder>? configureNode = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "node",
                    MetricsCompositionNodeTypes.Aggregate,
                    configureNode))
                .Build())
            .RegisterNodes(registry => registry.RegisterMetricsAggregate())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var descriptor = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem()
            .Descriptor;
        var input = descriptor.Inputs[MetricsCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<MetricSampleInput>>();
        var output = descriptor.Outputs[MetricsCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<MetricSnapshotOutput>>();

        await run(input, output, descriptor);
    }

    private static async Task BuildCompositionAsync(IServiceProvider provider)
    {
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();
        await hostedService.StartAsync(CancellationToken.None);
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }
}
