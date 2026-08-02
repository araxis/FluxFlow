using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Diagnostics;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine;
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

public sealed class ObservabilityServiceCollectionExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        ObservabilityComponentDefinition.Ports.Input);
    private static readonly ApplicationAddress Output = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        ObservabilityComponentDefinition.Ports.Output);
    private static readonly ApplicationAddress Events = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        ComponentEvents.PortName);

    [Fact]
    public void AddObservability_registers_canonical_metadata()
    {
        var registry = ComponentCatalogTestHost.Create(AddObservability);

        AssertMetadata<FlowCounterSnapshot>(
            registry,
            ObservabilityComponentDefinition.Types.Counter);
        AssertMetadata<FlowLogEntry<JsonElement>>(
            registry,
            ObservabilityComponentDefinition.Types.Logger);
        AssertMetadata<FlowMetricSnapshot>(
            registry,
            ObservabilityComponentDefinition.Types.Metrics);
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_observability_metadata()
    {
        var metadata = MetadataByType();

        metadata.Keys.ShouldBe([
            ObservabilityComponentDefinition.Types.Counter,
            ObservabilityComponentDefinition.Types.Logger,
            ObservabilityComponentDefinition.Types.Metrics
        ], ignoreOrder: false);

        foreach (var item in metadata.Values)
        {
            item.Category.ShouldBe(new ComponentCategory("Observability"));
            item.SuggestedEditorWidth.ShouldBe(460);
            ComponentDesignMetadataValidator.Validate(item).ShouldBeEmpty();
        }

        AssertResources(
            metadata[ObservabilityComponentDefinition.Types.Counter],
            [
                (ObservabilityComponentDefinition.Resources.Engine, 0, false, nameof(IFlowExpressionEngine)),
                (ObservabilityComponentDefinition.Resources.ContextFactory, 1, false, "IFlowMapContextFactory<JsonElement>"),
                (ObservabilityComponentDefinition.Resources.Clock, 2, false, nameof(TimeProvider)),
                ("processing", int.MaxValue, false, "CompositionProcessingProfile")
            ]);
        var engine = metadata[ObservabilityComponentDefinition.Types.Counter]
            .Resources
            .Single(resource => resource.Name.Value == ObservabilityComponentDefinition.Resources.Engine);
        AttributeValue(engine.Attributes, ResourceDesignMetadataAttributeNames.RequiredWhenAnyOption)
            .ShouldBe("predicate");

        AssertResources(
            metadata[ObservabilityComponentDefinition.Types.Logger],
            [
                (ObservabilityComponentDefinition.Resources.Clock, 0, false, nameof(TimeProvider)),
                (ObservabilityComponentDefinition.Resources.AttributeSelector("{name}"), 1, false, "IObservabilityValueSelector<JsonElement>"),
                ("processing", int.MaxValue, false, "CompositionProcessingProfile")
            ]);
        var attributeSelector = metadata[ObservabilityComponentDefinition.Types.Logger]
            .Resources[1];
        AttributeValue(attributeSelector.Attributes, ResourceDesignMetadataAttributeNames.Option)
            .ShouldBe("attributeSelectors");

        AssertResources(
            metadata[ObservabilityComponentDefinition.Types.Metrics],
            [
                (ObservabilityComponentDefinition.Resources.SizeSelector, 0, false, "IObservabilityValueSelector<JsonElement>"),
                (ObservabilityComponentDefinition.Resources.Clock, 1, false, nameof(TimeProvider)),
                ("processing", int.MaxValue, false, "CompositionProcessingProfile")
            ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_canonical_ports()
    {
        var metadata = MetadataByType();

        AssertPorts(
            metadata[ObservabilityComponentDefinition.Types.Counter],
            nameof(FlowCounterSnapshot));
        AssertPorts(
            metadata[ObservabilityComponentDefinition.Types.Logger],
            "FlowLogEntry<JsonElement>");
        AssertPorts(
            metadata[ObservabilityComponentDefinition.Types.Metrics],
            nameof(FlowMetricSnapshot));
    }

    [Fact]
    public void Design_metadata_provider_describes_counter_options()
    {
        var metadata = MetadataByType()[ObservabilityComponentDefinition.Types.Counter];

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "predicate",
            "expressionId",
            "expressionName",
            "boundedCapacity",
            "processing"
        ], ignoreOrder: false);
        AssertOption(metadata, "predicate", OptionValueKind.Expression, null);
        AssertOption(metadata, "expressionId", OptionValueKind.Text, null);
        AssertOption(metadata, "expressionName", OptionValueKind.Text, null);
    }

    [Fact]
    public void Design_metadata_provider_describes_logger_options()
    {
        var metadata = MetadataByType()[ObservabilityComponentDefinition.Types.Logger];
        var defaults = new FlowLoggerOptions();

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "level",
            "category",
            "messageTemplate",
            "attributeSelectors",
            "boundedCapacity",
            "processing"
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
    }

    [Fact]
    public void Design_metadata_provider_describes_metrics_options()
    {
        var metadata = MetadataByType()[ObservabilityComponentDefinition.Types.Metrics];

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "boundedCapacity",
            "processing"
        ], ignoreOrder: false);
    }

    [Fact]
    public void Design_metadata_provider_describes_option_hints()
    {
        var metadata = MetadataByType();
        var counter = OptionsByName(metadata[ObservabilityComponentDefinition.Types.Counter]);
        AssertOptionHints(counter["predicate"], "Filtering",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Expression,
            OptionDesignMetadataAttributeValues.Expression,
            ObservabilityComponentDefinition.Resources.Engine);
        AssertOptionHints(counter["expressionId"], "Diagnostics",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(counter["expressionName"], "Diagnostics",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text);

        var logger = OptionsByName(metadata[ObservabilityComponentDefinition.Types.Logger]);
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
            relatedResource: ObservabilityComponentDefinition.Resources.AttributeSelector("{name}"));
    }

    [Fact]
    public void Design_metadata_provider_describes_resource_picker_hints()
    {
        var metadata = MetadataByType();
        var counter = ResourcesByName(metadata[ObservabilityComponentDefinition.Types.Counter]);
        AssertResourceHints(counter[ObservabilityComponentDefinition.Resources.Engine],
            ResourceDesignMetadataAttributeValues.ExpressionEngine,
            "expression-engine:{name}");
        AssertResourceHints(counter[ObservabilityComponentDefinition.Resources.ContextFactory],
            ResourceDesignMetadataAttributeValues.ContextFactory,
            "context-factory:{name}");
        AssertResourceHints(counter[ObservabilityComponentDefinition.Resources.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");

        var logger = ResourcesByName(metadata[ObservabilityComponentDefinition.Types.Logger]);
        AssertResourceHints(logger[ObservabilityComponentDefinition.Resources.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");
        AssertResourceHints(
            logger[ObservabilityComponentDefinition.Resources.AttributeSelector("{name}")],
            ResourceDesignMetadataAttributeValues.Selector,
            ObservabilityComponentDefinition.Resources.AttributeSelector("{name}"));

        var metrics = ResourcesByName(metadata[ObservabilityComponentDefinition.Types.Metrics]);
        AssertResourceHints(metrics[ObservabilityComponentDefinition.Resources.SizeSelector],
            ResourceDesignMetadataAttributeValues.Selector,
            "selector:{name}");
        AssertResourceHints(metrics[ObservabilityComponentDefinition.Resources.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentCatalogTestHost.CreateDesignMetadataCatalog(
            static services => services.AddFluxFlowComponents().AddObservability());

        catalog.All.Count.ShouldBe(3);
        catalog.TryGet(new ComponentType(ObservabilityComponentDefinition.Types.Counter), out _)
            .ShouldBeTrue();
        catalog.TryGet(new ComponentType(ObservabilityComponentDefinition.Types.Logger), out _)
            .ShouldBeTrue();
        catalog.TryGet(new ComponentType(ObservabilityComponentDefinition.Types.Metrics), out _)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Hosted_counter_without_predicate_uses_clock_without_engine()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-23T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var result = await RunNodeAsync<FlowCounterSnapshot>(
            ObservabilityComponentDefinition.Types.Counter,
            Json("item"),
            Properties(
                ("name", "accepted"),
                (ObservabilityComponentDefinition.Resources.Clock, "Resources.fixed")),
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
            ObservabilityComponentDefinition.Types.Counter,
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
                (ObservabilityComponentDefinition.Resources.Engine, "Resources.primary"),
                (ObservabilityComponentDefinition.Resources.ContextFactory, "Resources.context")),
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
    public async Task Obsolete_counter_expression_option_is_rejected_with_canonical_guidance()
    {
        await using var host = await StartHostAsync(
            ObservabilityComponentDefinition.Types.Counter,
            Properties(("expression", "accepted")));

        AssertPreparationFailure(host, "Use 'predicate'");
    }

    [Fact]
    public async Task Missing_counter_engine_is_a_preparation_failure()
    {
        await using var host = await StartHostAsync(
            ObservabilityComponentDefinition.Types.Counter,
            Properties(("predicate", "accepted")));

        AssertPreparationFailure(host, "engine");
    }

    [Fact]
    public async Task Hosted_logger_binds_options_and_resolves_selectors()
    {
        var result = await RunNodeAsync<FlowLogEntry<JsonElement>>(
            ObservabilityComponentDefinition.Types.Logger,
            Json(new { kind = "alpha", size = 3 }),
            Properties(
                ("level", "Warning"),
                ("category", "workflow.test"),
                ("messageTemplate", "Observed {kind}:{size} #{sequence}"),
                ("attributeSelectors", new[] { "kind", "size" }),
                (ObservabilityComponentDefinition.Resources.AttributeSelector("kind"), "Resources.kind"),
                (ObservabilityComponentDefinition.Resources.AttributeSelector("size"), "Resources.size")),
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
            ObservabilityComponentDefinition.Types.Logger,
            Properties(("attributeSelectors", "kind")));

        AssertPreparationFailure(host, "attribute:kind");
    }

    [Fact]
    public async Task Hosted_metrics_resolves_selector_and_uses_clock_for_rates()
    {
        var firstTimestamp = DateTimeOffset.Parse("2026-07-23T12:00:00Z");
        var clock = new FakeTimeProvider(firstTimestamp);
        await WithNodeAsync(
            ObservabilityComponentDefinition.Types.Metrics,
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
                (ObservabilityComponentDefinition.Resources.SizeSelector, "Resources.size"),
                (ObservabilityComponentDefinition.Resources.Clock, "Resources.fixed")),
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
            ObservabilityComponentDefinition.Types.Counter,
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
                (ObservabilityComponentDefinition.Resources.Engine, "Resources.primary")),
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
            ObservabilityComponentDefinition.Types.Logger,
            Json("item"),
            Properties(
                ("attributeSelectors", "broken"),
                (ObservabilityComponentDefinition.Resources.AttributeSelector("broken"), "Resources.broken")),
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
            ObservabilityComponentDefinition.Types.Metrics,
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
            Properties((ObservabilityComponentDefinition.Resources.SizeSelector, "Resources.size")),
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
            ObservabilityComponentDefinition.Types.Logger,
            async ports =>
            {
                var eventReceive = ports.ReceiveAsync<ComponentEvent>(Events, Timeout);
                var input = FlowMessage.Create(Json("item"));
                (await ports.SendAsync(Input, input)).IsAccepted.ShouldBeTrue();

                var eventMessage = (await eventReceive).Message.ShouldNotBeNull();
                eventMessage.CorrelationId.ShouldBe(input.CorrelationId);
                eventMessage.Value.Name.ShouldBe(ObservabilityDiagnosticNames.LoggerEmitted);
            });
    }

    [Theory]
    [InlineData(ObservabilityComponentDefinition.Types.Counter, "boundedCapacity", 0)]
    [InlineData(ObservabilityComponentDefinition.Types.Logger, "boundedCapacity", 0)]
    [InlineData(ObservabilityComponentDefinition.Types.Logger, "level", "unsupported")]
    [InlineData(ObservabilityComponentDefinition.Types.Metrics, "boundedCapacity", 0)]
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

    private static void AddObservability(IServiceCollection services)
        => services.AddFluxFlowComponents().AddObservability();

    private static void AssertMetadata<TOutput>(
        ComponentCatalog registry,
        string nodeType)
    {
        var registration = registry.Components[nodeType];
        registration.Inputs[ObservabilityComponentDefinition.Ports.Input].MessageType
            .ShouldBe(typeof(JsonElement));
        registration.Outputs[ObservabilityComponentDefinition.Ports.Output].MessageType
            .ShouldBe(typeof(TOutput));
    }

    private static Dictionary<string, ComponentDesignMetadata> MetadataByType()
        => ComponentCatalogTestHost.CreateDesignMetadataCatalog(
                services => services.AddFluxFlowComponents().AddObservability()).All
            .ToDictionary(metadata => metadata.Type.Value, StringComparer.Ordinal);

    private static Dictionary<string, OptionDesignMetadata> OptionsByName(
        ComponentDesignMetadata metadata)
        => metadata.Options.ToDictionary(option => option.Name.Value, StringComparer.Ordinal);

    private static Dictionary<string, ResourceDesignMetadata> ResourcesByName(
        ComponentDesignMetadata metadata)
        => metadata.Resources.ToDictionary(resource => resource.Name.Value, StringComparer.Ordinal);

    private static void AssertPorts(ComponentDesignMetadata metadata, string outputType)
    {
        metadata.Ports.Count.ShouldBe(3);
        metadata.Ports[^1].Name.Value.ShouldBe("Events");
        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(ObservabilityComponentDefinition.Ports.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType?.Value.ShouldBe(nameof(JsonElement));
        input.IsPrimary.ShouldBeTrue();
        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(ObservabilityComponentDefinition.Ports.Output);
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
        Action<ApplicationResourceRegistrationContext>? configureRuntime = null)
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
        Func<ApplicationPorts, Task> run,
        IReadOnlyDictionary<string, object?>? properties = null,
        IReadOnlyList<string>? resources = null,
        Action<ApplicationResourceRegistrationContext>? configureRuntime = null)
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
        Action<ApplicationResourceRegistrationContext>? configureRuntime = null)
        => CanonicalApplicationTestHost.StartAsync(
            SingleComponent(nodeType, properties, resources, componentName: "node"),
            AddObservability,
            registerResources: configureRuntime);

    private static void AssertPreparationFailure(
        CanonicalApplicationTestHost host,
        string expectedMessage)
    {
        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        host.StartResult.Update.Diagnostics.ShouldContain(failure =>
            failure.Stage == ApplicationUpdateStage.ComponentPreparation &&
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!.Contains(
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
