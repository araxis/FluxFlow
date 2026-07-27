using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Metrics;
using FluxFlow.Components.Metrics.Composition;
using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Diagnostics;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Metrics.Composition.Tests;

public sealed class MetricsServiceCollectionExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "node", MetricsComponentDefinition.Ports.Input);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", MetricsComponentDefinition.Ports.Output);
    private static readonly ApplicationAddress Events =
        ApplicationAddress.WorkflowPort("main", "node", ComponentEvents.PortName);

    [Fact]
    public void AddMetricsComponents_registers_request_result_metadata()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddMetricsComponents());

        var registration = registry.Components[MetricsComponentDefinition.Types.Aggregate];
        registration.Inputs[MetricsComponentDefinition.Ports.Input].MessageType
            .ShouldBe(typeof(MetricSampleInput));
        registration.Outputs[MetricsComponentDefinition.Ports.Output].MessageType
            .ShouldBe(typeof(MetricSnapshotOutput));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_metrics_metadata()
    {
        var metadata = MetricsDesignMetadata();

        metadata.Type.Value.ShouldBe(MetricsComponentDefinition.Types.Aggregate);
        metadata.DisplayName?.Value.ShouldBe("Metrics Aggregate");
        metadata.Category.ShouldBe(new ComponentCategory("Metrics"));
        metadata.SuggestedEditorWidth.ShouldBe(460);
        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == MetricsComponentDefinition.Resources.Clock);
        AssertClockResource(metadata);
    }

    [Fact]
    public void Design_metadata_provider_describes_metrics_ports()
    {
        var metadata = MetricsDesignMetadata();

        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(MetricsComponentDefinition.Ports.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType?.Value.ShouldBe(nameof(MetricSampleInput));
        input.IsPrimary.ShouldBeTrue();

        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(MetricsComponentDefinition.Ports.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(1);
        output.ValueType?.Value.ShouldBe("MetricSnapshotOutput");
        output.IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public void Design_metadata_provider_describes_metrics_options()
    {
        var metadata = MetricsDesignMetadata();
        var defaults = new MetricsAggregateOptions();

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
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
    public void Design_metadata_provider_describes_metrics_option_hints()
    {
        var metadata = MetricsDesignMetadata();
        var options = OptionsByName(metadata);

        AssertOptionHints(
            options["rateWindowSeconds"],
            "Rate",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(
            options["boundedCapacity"],
            "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(
            options["maxGroups"],
            "Grouping",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(
            options["emitEverySample"],
            "Emission",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(
            options["trackLatest"],
            "Snapshot",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(
            options["trackMinMax"],
            "Snapshot",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(
            options["trackSize"],
            "Snapshot",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(
            options["groupByTag"],
            "Grouping",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            options["treatMissingValueAsZero"],
            "Aggregation",
            OptionDesignMetadataAttributeValues.Advanced);
    }

    [Fact]
    public void Design_metadata_provider_describes_metrics_resource_picker_hints()
    {
        var metadata = MetricsDesignMetadata();

        AssertResourceHints(
            metadata.Resources.ShouldHaveSingleItem(),
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentCatalogTestHost.CreateDesignMetadataCatalog(
            static services => services.AddMetricsComponents());

        catalog.All.ShouldHaveSingleItem();
        catalog.TryGet(
            new ComponentType(MetricsComponentDefinition.Types.Aggregate),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull()
            .DisplayName?.Value.ShouldBe("Metrics Aggregate");
    }

    [Fact]
    public async Task Hosted_metrics_aggregate_binds_options_groups_and_preserves_correlation_id()
    {
        var start = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        await WithNodeAsync(async (ports, _) =>
        {
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

            var firstReceive = ports.ReceiveAsync<MetricSnapshotOutput>(Output, Timeout);
            (await ports.SendAsync(Input, first)).IsAccepted.ShouldBeTrue();
            await firstReceive;
            var secondReceive = ports.ReceiveAsync<MetricSnapshotOutput>(Output, Timeout);
            (await ports.SendAsync(Input, second)).IsAccepted.ShouldBeTrue();
            var snapshot = (await secondReceive).Message.ShouldNotBeNull();

            snapshot.CorrelationId.ShouldBe(second.CorrelationId);
            snapshot.IsError.ShouldBeFalse();
            var value = snapshot.Value.ShouldNotBeNull();
            value.SampleCount.ShouldBe(2);
            value.ValueCount.ShouldBe(2);
            value.TotalValue.ShouldBe(6);
            value.AverageValue.ShouldBe(3);
            value.TotalSize.ShouldBe(30);
            value.Groups.Keys.ShouldBe(["sensors/a", "sensors/b"], ignoreOrder: true);
            value.Groups["sensors/a"].TotalSize.ShouldBe(10);
            value.Groups["sensors/b"].TotalSize.ShouldBe(20);
        },
        Properties(
            ("rateWindowSeconds", 10),
            ("groupByTag", "topic")));
    }

    [Fact]
    public async Task Hosted_metrics_aggregate_uses_optional_keyed_clock_for_missing_timestamps()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:00:42Z");
        var clock = new FakeTimeProvider(timestamp);

        await WithNodeAsync(
            async (ports, _) =>
            {
                var receive = ports.ReceiveAsync<MetricSnapshotOutput>(Output, Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(new MetricSampleInput
                {
                    Name = "items",
                    Value = 1
                }))).IsAccepted.ShouldBeTrue();

                var snapshot = (await receive).Message.ShouldNotBeNull();
                var value = snapshot.Value.ShouldNotBeNull();
                value.Timestamp.ShouldBe(timestamp);
                value.Latest.ShouldNotBeNull().Timestamp.ShouldBe(timestamp);
                value.Groups["default"].LatestTimestamp.ShouldBe(timestamp);
            },
            Properties((MetricsComponentDefinition.Resources.Clock, "Resources.fixed")),
            resources: ["fixed"],
            configureRuntime: context => context.Services.AddExternalFluxFlowResource<TimeProvider>(
                ApplicationAddress.Resource("fixed"),
                clock));
    }

    [Fact]
    public async Task Hosted_metrics_aggregate_emits_coalesced_final_snapshot_on_completion()
    {
        await WithNodeAsync(async (ports, host) =>
        {
            (await ports.SendAsync(
                Input,
                FlowMessage.Create(new MetricSampleInput { Value = 1 }))).IsAccepted.ShouldBeTrue();
            (await ports.SendAsync(
                Input,
                FlowMessage.Create(new MetricSampleInput { Value = 2 }))).IsAccepted.ShouldBeTrue();

            var finalReceive = ports.ReceiveAsync<MetricSnapshotOutput>(Output, Timeout);
            await host.RevisionHost.StopAsync();
            var snapshot = (await finalReceive).Message.ShouldNotBeNull();
            snapshot.IsError.ShouldBeFalse();
            snapshot.Value.ShouldNotBeNull().SampleCount.ShouldBe(2);
            snapshot.Value.TotalValue.ShouldBe(3);
        },
        Properties(("emitEverySample", false)));
    }

    [Fact]
    public async Task Hosted_metrics_aggregate_exposes_events()
    {
        await WithNodeAsync(async (ports, _) =>
        {
            var message = FlowMessage.Create(new MetricSampleInput
            {
                Value = 1,
                Group = "items"
            });

            var eventReceive = ports.ReceiveAsync<ComponentEvent>(Events, Timeout);
            (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();

            var eventMessage = (await eventReceive).Message.ShouldNotBeNull();
            var @event = eventMessage.Value;
            @event.Name.ShouldBe(MetricsDiagnosticNames.AggregateUpdated);
            eventMessage.CorrelationId.ShouldBe(message.CorrelationId);
            @event.Attributes["sampleCount"].ShouldBe("1");
        });
    }

    [Fact]
    public async Task Hosted_metrics_aggregate_emits_normal_failure_and_continues_after_invalid_sample()
    {
        await WithNodeAsync(async (ports, _) =>
        {
            var bad = FlowMessage.Create(
                new MetricSampleInput { Size = -1 },
                new CorrelationId("bad"));
            var good = FlowMessage.Create(
                new MetricSampleInput { Size = 3 },
                new CorrelationId("good"));

            var failureReceive = ports.ReceiveAsync<MetricSnapshotOutput>(Output, Timeout);
            (await ports.SendAsync(Input, bad)).IsAccepted.ShouldBeTrue();
            var failure = (await failureReceive).Message.ShouldNotBeNull();
            var snapshotReceive = ports.ReceiveAsync<MetricSnapshotOutput>(Output, Timeout);
            (await ports.SendAsync(Input, good)).IsAccepted.ShouldBeTrue();
            var snapshot = (await snapshotReceive).Message.ShouldNotBeNull();

            failure.CorrelationId.ShouldBe(bad.CorrelationId);
            failure.IsError.ShouldBeTrue();
            failure.Error.ShouldNotBeNull().Code
                .ShouldBe(MetricsErrorCodeNames.InvalidSample);
            snapshot.CorrelationId.ShouldBe(good.CorrelationId);
            snapshot.Value.ShouldNotBeNull().SampleCount.ShouldBe(1);
            snapshot.Value.TotalSize.ShouldBe(3);
        });
    }

    [Fact]
    public async Task Hosted_metrics_group_limit_is_a_normal_error_message()
    {
        await WithNodeAsync(
            async (ports, _) =>
            {
                var firstReceive = ports.ReceiveAsync<MetricSnapshotOutput>(Output, Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(
                    new MetricSampleInput { Group = "a", Value = 1 }))).IsAccepted.ShouldBeTrue();
                await firstReceive;
                var secondReceive = ports.ReceiveAsync<MetricSnapshotOutput>(Output, Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(
                    new MetricSampleInput { Group = "b", Value = 2 }))).IsAccepted.ShouldBeTrue();
                var partial = (await secondReceive).Message.ShouldNotBeNull();
                partial.IsError.ShouldBeTrue();
                partial.Error.ShouldNotBeNull().Code
                    .ShouldBe(MetricsErrorCodeNames.GroupLimitReached);
                partial.Error.Details!.Value.GetProperty("group").GetString().ShouldBe("b");
            },
            Properties(("maxGroups", 1)));
    }

    [Fact]
    public async Task Invalid_configuration_surfaces_factory_diagnostic()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                MetricsComponentDefinition.Types.Aggregate,
                Properties(("boundedCapacity", 0))),
            registry => registry.AddMetricsComponents());

        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        host.StartResult.Update.Diagnostics.ShouldContain(failure =>
            failure.Stage == ApplicationUpdateStage.ComponentPreparation &&
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!.Contains(
                "greater than zero",
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private static ComponentDesignMetadata MetricsDesignMetadata()
        => MetricsComponentDefinition.CreateMetadata()
            .ShouldHaveSingleItem();

    private static Dictionary<string, OptionDesignMetadata> OptionsByName(
        ComponentDesignMetadata metadata)
        => metadata.Options.ToDictionary(
            option => option.Name.Value,
            StringComparer.Ordinal);

    private static void AssertOption(
        ComponentDesignMetadata metadata,
        string name,
        OptionValueKind kind,
        object? defaultValue,
        double? min = null)
    {
        var option = metadata.Options.Single(option => option.Name.Value == name);
        option.Kind.ShouldBe(kind);
        option.DefaultValue.ShouldBe(defaultValue);
        option.Min.ShouldBe(min);
    }

    private static void AssertOptionHints(
        OptionDesignMetadata option,
        string section,
        string importance,
        string? editor = null)
    {
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Section)
            .ShouldBe(section);
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Importance)
            .ShouldBe(importance);

        if (editor is null)
        {
            option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.Editor))
                .ShouldBeFalse();
        }
        else
        {
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Editor)
                .ShouldBe(editor);
        }

        option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.Syntax))
            .ShouldBeFalse();
        option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.RelatedResource))
            .ShouldBeFalse();
    }

    private static void AssertClockResource(ComponentDesignMetadata metadata)
    {
        var resource = metadata.Resources.ShouldHaveSingleItem();

        resource.Name.Value.ShouldBe(MetricsComponentDefinition.Resources.Clock);
        resource.DisplayName?.Value.ShouldBe("Clock");
        resource.Order.ShouldBe(0);
        resource.IsRequired.ShouldBeFalse();
        resource.ValueType?.Value.ShouldBe(nameof(TimeProvider));
    }

    private static void AssertResourceHints(
        ResourceDesignMetadata resource,
        string pickerKind,
        string keyPattern)
    {
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.Ownership)
            .ShouldBe(ResourceDesignMetadataAttributeValues.HostOwned);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.PickerKind)
            .ShouldBe(pickerKind);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.KeyPattern)
            .ShouldBe(keyPattern);
    }

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;

    private static async Task WithNodeAsync(
        Func<ApplicationPorts, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?>? properties = null,
        IReadOnlyList<string>? resources = null,
        Action<ApplicationResourceRegistrationContext>? configureRuntime = null)
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                MetricsComponentDefinition.Types.Aggregate,
                properties,
                resources),
            registry => registry.AddMetricsComponents(),
            registerResources: configureRuntime);
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
    }
}
