using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Diagnostics;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting.DependencyInjection;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Observability.Composition.Tests;

public sealed class ObservabilityCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        ObservabilityCompositionPortNames.Input);
    private static readonly ApplicationAddress Output = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        ObservabilityCompositionPortNames.Output);
    private static readonly ApplicationAddress Events = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        CompositionComponentEvents.PortName);

    [Fact]
    public void Register_observability_nodes_registers_canonical_metadata()
    {
        var registry = new CompositionNodeRegistry();
        RegisterAll(registry);

        AssertMetadata<FlowCounterSnapshot>(
            registry,
            ObservabilityCompositionNodeTypes.Counter);
        AssertMetadata<FlowLogEntry<JsonElement>>(
            registry,
            ObservabilityCompositionNodeTypes.Logger);
        AssertMetadata<FlowMetricSnapshot>(
            registry,
            ObservabilityCompositionNodeTypes.Metrics);
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_observability_metadata()
    {
        var metadata = MetadataByType();

        metadata.Keys.ShouldBe([
            ObservabilityCompositionNodeTypes.Counter,
            ObservabilityCompositionNodeTypes.Logger,
            ObservabilityCompositionNodeTypes.Metrics
        ], ignoreOrder: false);

        foreach (var item in metadata.Values)
        {
            item.Category.ShouldBe(new ComponentCategory("Observability"));
            item.SuggestedEditorWidth.ShouldBe(460);
            ComponentDesignMetadataValidator.Validate(item).ShouldBeEmpty();
            item.Attributes.ContainsKey(new ComponentAttributeName("omittedOptions"))
                .ShouldBeFalse();
            item.Attributes.ContainsKey(new ComponentAttributeName("omittedOptionsReason"))
                .ShouldBeFalse();
        }

        AssertResources(
            metadata[ObservabilityCompositionNodeTypes.Counter],
            [
                (ObservabilityCompositionResourceNames.Engine, 0, false, nameof(IFlowExpressionEngine)),
                (ObservabilityCompositionResourceNames.ContextFactory, 1, false, "IFlowMapContextFactory<JsonElement>"),
                (ObservabilityCompositionResourceNames.Clock, 2, false, nameof(TimeProvider))
            ]);
        var engine = metadata[ObservabilityCompositionNodeTypes.Counter]
            .Resources
            .Single(resource => resource.Name.Value == ObservabilityCompositionResourceNames.Engine);
        AttributeValue(engine.Attributes, ResourceDesignMetadataAttributeNames.RequiredWhenAnyOption)
            .ShouldBe("predicate,expression");

        AssertResources(
            metadata[ObservabilityCompositionNodeTypes.Logger],
            [
                (ObservabilityCompositionResourceNames.Clock, 0, false, nameof(TimeProvider)),
                (ObservabilityCompositionResourceNames.AttributeSelectorPrefix + "{name}", 1, false, "IObservabilityValueSelector<JsonElement>")
            ]);
        var attributeSelector = metadata[ObservabilityCompositionNodeTypes.Logger]
            .Resources[1];
        AttributeValue(attributeSelector.Attributes, ResourceDesignMetadataAttributeNames.Option)
            .ShouldBe("attributeSelectors");

        AssertResources(
            metadata[ObservabilityCompositionNodeTypes.Metrics],
            [
                (ObservabilityCompositionResourceNames.SizeSelector, 0, false, "IObservabilityValueSelector<JsonElement>"),
                (ObservabilityCompositionResourceNames.Clock, 1, false, nameof(TimeProvider))
            ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_canonical_ports()
    {
        var metadata = MetadataByType();

        AssertPorts(
            metadata[ObservabilityCompositionNodeTypes.Counter],
            nameof(FlowCounterSnapshot));
        AssertPorts(
            metadata[ObservabilityCompositionNodeTypes.Logger],
            "FlowLogEntry<JsonElement>");
        AssertPorts(
            metadata[ObservabilityCompositionNodeTypes.Metrics],
            nameof(FlowMetricSnapshot));
    }

    [Fact]
    public void Design_metadata_provider_describes_counter_options()
    {
        var metadata = MetadataByType()[ObservabilityCompositionNodeTypes.Counter];
        var defaults = new FlowCounterOptions();

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "name",
            "predicate",
            "expression",
            "expressionId",
            "expressionName",
            "boundedCapacity"
        ], ignoreOrder: false);
        AssertOption(metadata, "name", OptionValueKind.Text, null);
        AssertOption(metadata, "predicate", OptionValueKind.Expression, null);
        AssertOption(metadata, "expression", OptionValueKind.Expression, null);
        AssertOption(metadata, "expressionId", OptionValueKind.Text, null);
        AssertOption(metadata, "expressionName", OptionValueKind.Text, null);
        AssertOption(metadata, "boundedCapacity", OptionValueKind.Number,
            defaults.BoundedCapacity, 1);
    }

    [Fact]
    public void Design_metadata_provider_describes_logger_options()
    {
        var metadata = MetadataByType()[ObservabilityCompositionNodeTypes.Logger];
        var defaults = new FlowLoggerOptions();

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "level",
            "category",
            "messageTemplate",
            "attributeSelectors",
            "boundedCapacity"
        ], ignoreOrder: false);
        var level = metadata.Options[0];
        level.Kind.ShouldBe(OptionValueKind.Enum);
        level.DefaultValue.ShouldBe(defaults.Level);
        level.Choices.Select(choice => choice.Value.Value).ShouldBe([
            FlowLogLevel.Trace.ToString(),
            FlowLogLevel.Debug.ToString(),
            FlowLogLevel.Information.ToString(),
            FlowLogLevel.Warning.ToString(),
            FlowLogLevel.Error.ToString(),
            FlowLogLevel.Critical.ToString()
        ], ignoreOrder: false);
        AssertOption(metadata, "category", OptionValueKind.Text, defaults.Category);
        AssertOption(metadata, "messageTemplate", OptionValueKind.MultilineText, null);
        var selectors = metadata.Options[3];
        selectors.Kind.ShouldBe(OptionValueKind.Json);
        selectors.DefaultValue.ShouldBeOfType<string[]>().ShouldBeEmpty();
        AssertOption(metadata, "boundedCapacity", OptionValueKind.Number,
            defaults.BoundedCapacity, 1);
    }

    [Fact]
    public void Design_metadata_provider_describes_metrics_options()
    {
        var metadata = MetadataByType()[ObservabilityCompositionNodeTypes.Metrics];
        var defaults = new FlowMetricsOptions();

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "name",
            "boundedCapacity"
        ], ignoreOrder: false);
        AssertOption(metadata, "name", OptionValueKind.Text, null);
        AssertOption(metadata, "boundedCapacity", OptionValueKind.Number,
            defaults.BoundedCapacity, 1);
    }

    [Fact]
    public void Design_metadata_provider_describes_option_hints()
    {
        var metadata = MetadataByType();
        var counter = OptionsByName(metadata[ObservabilityCompositionNodeTypes.Counter]);
        AssertOptionHints(counter["name"], "Counter",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(counter["predicate"], "Filtering",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Expression,
            OptionDesignMetadataAttributeValues.Expression,
            ObservabilityCompositionResourceNames.Engine);
        AssertOptionHints(counter["expression"], "Filtering",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Expression,
            OptionDesignMetadataAttributeValues.Expression,
            ObservabilityCompositionResourceNames.Engine);
        AssertOptionHints(counter["expressionId"], "Diagnostics",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(counter["expressionName"], "Diagnostics",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(counter["boundedCapacity"], "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);

        var logger = OptionsByName(metadata[ObservabilityCompositionNodeTypes.Logger]);
        AssertOptionHints(logger["level"], "Logging",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(logger["category"], "Logging",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(logger["messageTemplate"], "Logging",
            OptionDesignMetadataAttributeValues.Primary);
        AssertOptionHints(logger["attributeSelectors"], "Attributes",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Json,
            relatedResource: ObservabilityCompositionResourceNames.AttributeSelectorPrefix + "{name}");
        AssertOptionHints(logger["boundedCapacity"], "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);

        var metrics = OptionsByName(metadata[ObservabilityCompositionNodeTypes.Metrics]);
        AssertOptionHints(metrics["name"], "Metrics",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(metrics["boundedCapacity"], "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public void Design_metadata_provider_describes_resource_picker_hints()
    {
        var metadata = MetadataByType();
        var counter = ResourcesByName(metadata[ObservabilityCompositionNodeTypes.Counter]);
        AssertResourceHints(counter[ObservabilityCompositionResourceNames.Engine],
            ResourceDesignMetadataAttributeValues.ExpressionEngine,
            "expression-engine:{name}");
        AssertResourceHints(counter[ObservabilityCompositionResourceNames.ContextFactory],
            ResourceDesignMetadataAttributeValues.ContextFactory,
            "context-factory:{name}");
        AssertResourceHints(counter[ObservabilityCompositionResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");

        var logger = ResourcesByName(metadata[ObservabilityCompositionNodeTypes.Logger]);
        AssertResourceHints(logger[ObservabilityCompositionResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");
        AssertResourceHints(
            logger[ObservabilityCompositionResourceNames.AttributeSelectorPrefix + "{name}"],
            ResourceDesignMetadataAttributeValues.Selector,
            ObservabilityCompositionResourceNames.AttributeSelectorPrefix + "{name}");

        var metrics = ResourcesByName(metadata[ObservabilityCompositionNodeTypes.Metrics]);
        AssertResourceHints(metrics[ObservabilityCompositionResourceNames.SizeSelector],
            ResourceDesignMetadataAttributeValues.Selector,
            "selector:{name}");
        AssertResourceHints(metrics[ObservabilityCompositionResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentDesignMetadataCatalog.FromProviders(
            [new ObservabilityComponentDesignMetadataProvider()]);

        catalog.All.Count.ShouldBe(3);
        catalog.TryGet(new ComponentType(ObservabilityCompositionNodeTypes.Counter), out _)
            .ShouldBeTrue();
        catalog.TryGet(new ComponentType(ObservabilityCompositionNodeTypes.Logger), out _)
            .ShouldBeTrue();
        catalog.TryGet(new ComponentType(ObservabilityCompositionNodeTypes.Metrics), out _)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Hosted_counter_without_predicate_uses_clock_without_engine()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-23T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var result = await RunNodeAsync<FlowCounterSnapshot>(
            ObservabilityCompositionNodeTypes.Counter,
            Json("item"),
            Properties(
                ("name", "accepted"),
                (ObservabilityCompositionResourceNames.Clock, "Resources.fixed")),
            ["fixed"],
            context => context.Services.AddExternalFluxFlowResource<TimeProvider>(
                ApplicationAddress.Resource("fixed"),
                clock));

        result.IsError.ShouldBeFalse();
        var snapshot = result.Value;
        snapshot.Name.ShouldBe("accepted");
        snapshot.InputType.ShouldBe(typeof(JsonElement).FullName);
        snapshot.Timestamp.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Hosted_counter_resolves_engine_and_context_factory()
    {
        await WithNodeAsync(
            ObservabilityCompositionNodeTypes.Counter,
            async ports =>
            {
                var rejectedReceive = ports.ReceiveAsync<FlowCounterSnapshot>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, Message(false))).IsAccepted.ShouldBeTrue();
                var rejected = (await rejectedReceive).Message.ShouldNotBeNull();
                rejected.IsError.ShouldBeFalse();

                var acceptedReceive = ports.ReceiveAsync<FlowCounterSnapshot>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, Message(true))).IsAccepted.ShouldBeTrue();
                var accepted = (await acceptedReceive).Message.ShouldNotBeNull();
                accepted.Value.RejectedCount.ShouldBe(1);
            },
            Properties(
                ("predicate", "accepted"),
                (ObservabilityCompositionResourceNames.Engine, "Resources.primary"),
                (ObservabilityCompositionResourceNames.ContextFactory, "Resources.context")),
            ["primary", "context"],
            context =>
            {
                context.Services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                    ApplicationAddress.Resource("primary"),
                    new TestExpressionEngine((_, map, _) => map.Variables["accepted"]));
                context.Services.AddExternalFluxFlowResource<IFlowMapContextFactory<JsonElement>>(
                    ApplicationAddress.Resource("context"),
                    new TestContextFactory(value => new FlowMapContext
                    {
                        Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["input"] = value,
                            ["accepted"] = value.GetProperty("accepted").GetBoolean()
                        }
                    }));
            });
    }

    [Fact]
    public async Task Missing_counter_engine_is_a_preparation_failure()
    {
        await using var host = await StartHostAsync(
            ObservabilityCompositionNodeTypes.Counter,
            Properties(("predicate", "accepted")));

        AssertPreparationFailure(host, "engine");
    }

    [Fact]
    public async Task Hosted_logger_binds_options_and_resolves_selectors()
    {
        var result = await RunNodeAsync<FlowLogEntry<JsonElement>>(
            ObservabilityCompositionNodeTypes.Logger,
            Json(new { kind = "alpha", size = 3 }),
            Properties(
                ("level", "Warning"),
                ("category", "workflow.test"),
                ("messageTemplate", "Observed {kind}:{size} #{sequence}"),
                ("attributeSelectors", new[] { "kind", "size" }),
                (ObservabilityCompositionResourceNames.AttributeSelector("kind"), "Resources.kind"),
                (ObservabilityCompositionResourceNames.AttributeSelector("size"), "Resources.size")),
            ["kind", "size"],
            context =>
            {
                context.Services.AddExternalFluxFlowResource<IObservabilityValueSelector<JsonElement>>(
                    ApplicationAddress.Resource("kind"),
                    new JsonValueSelector((input, _) => input.GetProperty("kind").GetString()));
                context.Services.AddExternalFluxFlowResource<IObservabilityValueSelector<JsonElement>>(
                    ApplicationAddress.Resource("size"),
                    new JsonValueSelector((input, _) => input.GetProperty("size").GetInt32()));
            });

        var entry = result.Value;
        entry.Level.ShouldBe(FlowLogLevel.Warning);
        entry.Category.ShouldBe("workflow.test");
        entry.Message.ShouldBe("Observed alpha:3 #1");
        entry.Attributes["kind"].ShouldBe("alpha");
    }

    [Fact]
    public async Task Missing_logger_selector_is_a_preparation_failure()
    {
        await using var host = await StartHostAsync(
            ObservabilityCompositionNodeTypes.Logger,
            Properties(("attributeSelectors", "kind")));

        AssertPreparationFailure(host, "attribute:kind");
    }

    [Fact]
    public async Task Hosted_metrics_resolves_selector_and_uses_clock_for_rates()
    {
        var firstTimestamp = DateTimeOffset.Parse("2026-07-23T12:00:00Z");
        var clock = new FakeTimeProvider(firstTimestamp);
        await WithNodeAsync(
            ObservabilityCompositionNodeTypes.Metrics,
            async ports =>
            {
                var firstReceive = ports.ReceiveAsync<FlowMetricSnapshot>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(Json("ab"))))
                    .IsAccepted.ShouldBeTrue();
                var first = (await firstReceive).Message.ShouldNotBeNull();
                first.Value.LastSize.ShouldBe(2);

                clock.Advance(TimeSpan.FromSeconds(2));
                var secondReceive = ports.ReceiveAsync<FlowMetricSnapshot>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(Json("abcd"))))
                    .IsAccepted.ShouldBeTrue();
                var second = (await secondReceive).Message.ShouldNotBeNull().Value;
                second.Count.ShouldBe(2);
                second.TotalSize.ShouldBe(6);
                second.AverageSize.ShouldBe(3);
                second.CurrentRatePerSecond.ShouldBe(0.5d);
                second.AverageRatePerSecond.ShouldBe(1d);
            },
            Properties(
                (ObservabilityCompositionResourceNames.SizeSelector, "Resources.size"),
                (ObservabilityCompositionResourceNames.Clock, "Resources.fixed")),
            ["size", "fixed"],
            context =>
            {
                context.Services.AddExternalFluxFlowResource<IObservabilityValueSelector<JsonElement>>(
                    ApplicationAddress.Resource("size"),
                    new JsonValueSelector((input, _) => input.GetString()!.Length));
                context.Services.AddExternalFluxFlowResource<TimeProvider>(
                    ApplicationAddress.Resource("fixed"),
                    clock);
            });
    }

    [Fact]
    public async Task Hosted_counter_failure_is_data_and_later_input_continues()
    {
        var calls = 0;
        await WithNodeAsync(
            ObservabilityCompositionNodeTypes.Counter,
            async ports =>
            {
                var failureReceive = ports.ReceiveAsync<FlowCounterSnapshot>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(Json(1))))
                    .IsAccepted.ShouldBeTrue();
                var failure = (await failureReceive).Message.ShouldNotBeNull();
                failure.Error.ShouldNotBeNull().Code
                    .ShouldBe(ObservabilityErrorCodeNames.CounterPredicateFailed);

                var successReceive = ports.ReceiveAsync<FlowCounterSnapshot>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(Json(2))))
                    .IsAccepted.ShouldBeTrue();
                var success = (await successReceive).Message.ShouldNotBeNull();
                success.IsError.ShouldBeFalse();
                success.Value.Count.ShouldBe(1);
            },
            Properties(
                ("predicate", "accepted"),
                (ObservabilityCompositionResourceNames.Engine, "Resources.primary")),
            ["primary"],
            context => context.Services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                ApplicationAddress.Resource("primary"),
                new TestExpressionEngine((_, _, _) =>
                {
                    if (++calls == 1)
                        throw new InvalidOperationException("predicate failed");
                    return true;
                })));
    }

    [Fact]
    public async Task Hosted_logger_selector_failure_is_an_in_band_error()
    {
        var result = await RunNodeAsync<FlowLogEntry<JsonElement>>(
            ObservabilityCompositionNodeTypes.Logger,
            Json("item"),
            Properties(
                ("attributeSelectors", "broken"),
                (ObservabilityCompositionResourceNames.AttributeSelector("broken"), "Resources.broken")),
            ["broken"],
            context => context.Services.AddExternalFluxFlowResource<IObservabilityValueSelector<JsonElement>>(
                ApplicationAddress.Resource("broken"),
                new JsonValueSelector((_, _) =>
                    throw new InvalidOperationException("selector failed"))));

        result.IsError.ShouldBeTrue();
        result.Error.ShouldNotBeNull().Code
            .ShouldBe(ObservabilityErrorCodeNames.LoggerAttributeSelectorFailed);
    }

    [Fact]
    public async Task Hosted_metrics_selector_failure_is_partial_and_continues()
    {
        var calls = 0;
        await WithNodeAsync(
            ObservabilityCompositionNodeTypes.Metrics,
            async ports =>
            {
                var firstReceive = ports.ReceiveAsync<FlowMetricSnapshot>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(Json("first"))))
                    .IsAccepted.ShouldBeTrue();
                var partial = (await firstReceive).Message.ShouldNotBeNull();
                partial.IsError.ShouldBeTrue();
                partial.Error!.Code.ShouldBe(
                    ObservabilityErrorCodeNames.MetricsSizeSelectorFailed);

                var secondReceive = ports.ReceiveAsync<FlowMetricSnapshot>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(Json("second"))))
                    .IsAccepted.ShouldBeTrue();
                var success = (await secondReceive).Message.ShouldNotBeNull();
                success.IsError.ShouldBeFalse();
                success.Value.Count.ShouldBe(2);
                success.Value.TotalSize.ShouldBe(3);
            },
            Properties((ObservabilityCompositionResourceNames.SizeSelector, "Resources.size")),
            ["size"],
            context => context.Services.AddExternalFluxFlowResource<IObservabilityValueSelector<JsonElement>>(
                ApplicationAddress.Resource("size"),
                new JsonValueSelector((_, _) =>
                {
                    if (++calls == 1)
                        throw new InvalidOperationException("size failed");
                    return 3;
                })));
    }

    [Fact]
    public async Task Hosted_observability_component_exposes_events()
    {
        await WithNodeAsync(
            ObservabilityCompositionNodeTypes.Logger,
            async ports =>
            {
                var eventReceive = ports.ReceiveAsync<CompositionComponentEvent>(Events, Timeout);
                var input = FlowMessage.Create(Json("item"));
                (await ports.SendAsync(Input, input)).IsAccepted.ShouldBeTrue();

                var eventMessage = (await eventReceive).Message.ShouldNotBeNull();
                eventMessage.CorrelationId.ShouldBe(input.CorrelationId);
                eventMessage.Value.Name.ShouldBe(ObservabilityDiagnosticNames.LoggerEmitted);
            });
    }

    [Theory]
    [InlineData(ObservabilityCompositionNodeTypes.Counter, "boundedCapacity", 0)]
    [InlineData(ObservabilityCompositionNodeTypes.Logger, "boundedCapacity", 0)]
    [InlineData(ObservabilityCompositionNodeTypes.Logger, "level", "unsupported")]
    [InlineData(ObservabilityCompositionNodeTypes.Metrics, "boundedCapacity", 0)]
    public async Task Invalid_configuration_is_a_preparation_failure(
        string nodeType,
        string option,
        object value)
    {
        await using var host = await StartHostAsync(
            nodeType,
            Properties((option, value)));

        AssertPreparationFailure(host, option);
    }

    private static void RegisterAll(CompositionNodeRegistry registry)
        => registry
            .RegisterCounter()
            .RegisterLogger()
            .RegisterMetrics();

    private static void AssertMetadata<TOutput>(
        CompositionNodeRegistry registry,
        string nodeType)
    {
        var registration = registry.Registrations[nodeType];
        registration.Inputs[ObservabilityCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(JsonElement));
        registration.Outputs[ObservabilityCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(TOutput));
    }

    private static Dictionary<string, ComponentDesignMetadata> MetadataByType()
        => new ObservabilityComponentDesignMetadataProvider()
            .GetMetadata()
            .ToDictionary(metadata => metadata.Type.Value, StringComparer.Ordinal);

    private static Dictionary<string, OptionDesignMetadata> OptionsByName(
        ComponentDesignMetadata metadata)
        => metadata.Options.ToDictionary(option => option.Name.Value, StringComparer.Ordinal);

    private static Dictionary<string, ResourceDesignMetadata> ResourcesByName(
        ComponentDesignMetadata metadata)
        => metadata.Resources.ToDictionary(resource => resource.Name.Value, StringComparer.Ordinal);

    private static void AssertPorts(ComponentDesignMetadata metadata, string outputType)
    {
        metadata.Ports.Count.ShouldBe(2);
        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(ObservabilityCompositionPortNames.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType?.Value.ShouldBe(nameof(JsonElement));
        input.IsPrimary.ShouldBeTrue();
        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(ObservabilityCompositionPortNames.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(1);
        output.ValueType?.Value.ShouldBe(outputType);
        output.IsPrimary.ShouldBeTrue();
    }

    private static void AssertOption(
        ComponentDesignMetadata metadata,
        string name,
        OptionValueKind kind,
        object? defaultValue,
        double? min = null)
    {
        var option = metadata.Options.Single(item => item.Name.Value == name);
        option.Kind.ShouldBe(kind);
        option.DefaultValue.ShouldBe(defaultValue);
        option.Min.ShouldBe(min);
    }

    private static void AssertResources(
        ComponentDesignMetadata metadata,
        IReadOnlyList<(string Name, int Order, bool IsRequired, string ValueType)> expected)
        => metadata.Resources.Select(resource => (
            resource.Name.Value,
            resource.Order,
            resource.IsRequired,
            resource.ValueType?.Value!)).ShouldBe(expected);

    private static void AssertOptionHints(
        OptionDesignMetadata option,
        string section,
        string importance,
        string? editor = null,
        string? syntax = null,
        string? relatedResource = null)
    {
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Section)
            .ShouldBe(section);
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Importance)
            .ShouldBe(importance);
        AssertOptionalAttribute(option.Attributes, OptionDesignMetadataAttributeNames.Editor, editor);
        AssertOptionalAttribute(option.Attributes, OptionDesignMetadataAttributeNames.Syntax, syntax);
        AssertOptionalAttribute(
            option.Attributes,
            OptionDesignMetadataAttributeNames.RelatedResource,
            relatedResource);
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

    private static void AssertOptionalAttribute(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name,
        string? expected)
    {
        var attributeName = new ComponentAttributeName(name);
        if (expected is null)
            attributes.ContainsKey(attributeName).ShouldBeFalse();
        else
            attributes[attributeName].Value.ShouldBe(expected);
    }

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;

    private static async Task<FlowMessage<TOutput>> RunNodeAsync<TOutput>(
        string nodeType,
        JsonElement input,
        IReadOnlyDictionary<string, object?>? properties = null,
        IReadOnlyList<string>? resources = null,
        Action<ApplicationRuntimeServicesContext>? configureRuntime = null)
    {
        FlowMessage<TOutput>? result = null;
        await WithNodeAsync(
            nodeType,
            async ports =>
            {
                var receive = ports.ReceiveAsync<TOutput>(Output, Timeout);
                var message = FlowMessage.Create(input, new CorrelationId(nodeType));
                (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();
                result = (await receive).Message.ShouldNotBeNull();
            },
            properties,
            resources,
            configureRuntime);
        return result.ShouldNotBeNull();
    }

    private static async Task WithNodeAsync(
        string nodeType,
        Func<ApplicationPortRuntime, Task> run,
        IReadOnlyDictionary<string, object?>? properties = null,
        IReadOnlyList<string>? resources = null,
        Action<ApplicationRuntimeServicesContext>? configureRuntime = null)
    {
        await using var host = await StartHostAsync(
            nodeType,
            properties,
            resources,
            configureRuntime);
        host.StartResult.Succeeded.ShouldBeTrue();
        await run(host.GetRequiredPorts());
    }

    private static ValueTask<CanonicalApplicationTestHost> StartHostAsync(
        string nodeType,
        IReadOnlyDictionary<string, object?>? properties = null,
        IReadOnlyList<string>? resources = null,
        Action<ApplicationRuntimeServicesContext>? configureRuntime = null)
        => CanonicalApplicationTestHost.StartAsync(
            SingleComponent(nodeType, properties, resources, componentName: "node"),
            RegisterAll,
            configureRuntimeServices: configureRuntime);

    private static void AssertPreparationFailure(
        CanonicalApplicationTestHost host,
        string expectedMessage)
    {
        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        host.StartResult.Update.Failures.ShouldContain(failure =>
            failure.Stage == ApplicationRevisionFailureStage.Preparation &&
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString().Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private static FlowMessage<JsonElement> Message(bool accepted)
        => FlowMessage.Create(Json(new { accepted }));

    private static JsonElement Json<T>(T value)
        => JsonSerializer.SerializeToElement(value);

    private sealed class TestExpressionEngine(
        Func<string, FlowMapContext, Type, object?> evaluate)
        : IFlowExpressionEngine
    {
        public string Name => "test";

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => evaluate(expression, context, resultType);
    }

    private sealed class TestContextFactory(
        Func<JsonElement, FlowMapContext> create)
        : IFlowMapContextFactory<JsonElement>
    {
        public FlowMapContext Create(JsonElement input) => create(input);
    }

    private sealed class JsonValueSelector(
        Func<JsonElement, ObservabilityNodeContext, object?> selector)
        : IObservabilityValueSelector<JsonElement>
    {
        public object? Select(JsonElement input, ObservabilityNodeContext context)
            => selector(input, context);
    }
}
