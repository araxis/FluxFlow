using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Mapping.Composition;
using FluxFlow.Components.Mapping.Contracts;
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
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Mapping.Composition.Tests;

public sealed class MappingCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "node", MappingCompositionPortNames.Input);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", MappingCompositionPortNames.Output);
    private static readonly ApplicationAddress Events =
        ApplicationAddress.WorkflowPort("main", "node", CompositionComponentEvents.PortName);

    [Fact]
    public void RegisterMapper_registers_only_the_canonical_json_contract()
    {
        var registry = new CompositionNodeRegistry().RegisterMapper();

        var mapper = registry.Registrations[MappingCompositionNodeTypes.Mapper];
        mapper.Inputs.Keys.ShouldBe([MappingCompositionPortNames.Input]);
        mapper.Outputs.Keys.ShouldBe([
            MappingCompositionPortNames.Output,
            CompositionComponentEvents.PortName
        ], ignoreOrder: false);
        mapper.Inputs[MappingCompositionPortNames.Input].MessageType.ShouldBe(typeof(JsonElement));
        mapper.Outputs[MappingCompositionPortNames.Output].MessageType.ShouldBe(typeof(JsonElement));
    }

    [Fact]
    public void RegisterMapper_supports_explicit_canonical_component_types()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterMapper("data.map.primary")
            .RegisterMapper("data.map.secondary");

        registry.Registrations.Keys.ShouldBe([
            "data.map.primary",
            "data.map.secondary"
        ], ignoreOrder: false);
        registry.Registrations.Values.ShouldAllBe(registration =>
            registration.Inputs[MappingCompositionPortNames.Input].MessageType == typeof(JsonElement) &&
            registration.Outputs[MappingCompositionPortNames.Output].MessageType == typeof(JsonElement));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_canonical_mapper_metadata()
    {
        var metadata = DesignMetadata();

        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Type.ShouldBe(new ComponentType(MappingCompositionNodeTypes.Mapper));
        metadata.DisplayName?.Value.ShouldBe("Mapper");
        metadata.Category.ShouldBe(new ComponentCategory("Mapping"));
        metadata.PreferredNodeName.ShouldBe(new ComponentPreferredNodeName("map"));
        metadata.SuggestedEditorWidth.ShouldBe(420);
        metadata.Options.Select(option => (option.Name.Value, option.Kind)).ShouldBe([
            ("expression", OptionValueKind.Expression),
            ("expressionId", OptionValueKind.Text),
            ("expressionName", OptionValueKind.Text),
            ("inputType", OptionValueKind.Text),
            ("outputType", OptionValueKind.Text),
            ("boundedCapacity", OptionValueKind.Number)
        ]);
        metadata.Options.Single(option => option.Name.Value == "expression")
            .IsRequired.ShouldBeTrue();
        metadata.Options.Single(option => option.Name.Value == "boundedCapacity")
            .Min.ShouldBe(1);
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == MappingCompositionResourceNames.Engine ||
            option.Name.Value == "targetType");
        metadata.Resources.Select(resource => (
            resource.Name.Value,
            resource.Order,
            resource.IsRequired,
            resource.ValueType?.Value)).ShouldBe([
            (MappingCompositionResourceNames.Engine, 0, true, nameof(IFlowExpressionEngine)),
            (MappingCompositionResourceNames.ContextFactory, 1, false, nameof(IMappingContextFactory)),
            (MappingCompositionResourceNames.Clock, 2, false, nameof(TimeProvider))
        ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_mapper_option_hints()
    {
        var options = DesignMetadata().Options.ToDictionary(
            option => option.Name.Value,
            StringComparer.Ordinal);

        AssertOptionHints(
            options["expression"],
            "Mapping",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: MappingCompositionResourceNames.Engine);
        AssertOptionHints(options["expressionId"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["expressionName"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["inputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["outputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public void Design_metadata_provider_describes_mapper_resource_picker_hints()
    {
        var resources = DesignMetadata().Resources.ToDictionary(
            resource => resource.Name.Value,
            StringComparer.Ordinal);

        AssertResourceHints(
            resources[MappingCompositionResourceNames.Engine],
            ResourceDesignMetadataAttributeValues.ExpressionEngine,
            "Resources.{name}");
        AssertResourceHints(
            resources[MappingCompositionResourceNames.ContextFactory],
            ResourceDesignMetadataAttributeValues.ContextFactory,
            "Resources.{name}");
        AssertResourceHints(
            resources[MappingCompositionResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "Resources.{name}");
    }

    [Fact]
    public void Design_metadata_provider_describes_only_canonical_mapper_ports()
    {
        var metadata = DesignMetadata();

        metadata.Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.IsPrimary,
            port.ValueType?.Value)).ShouldBe([
            (MappingCompositionPortNames.Input, PortDirection.Input, 0, true, nameof(JsonElement)),
            (MappingCompositionPortNames.Output, PortDirection.Output, 1, true, nameof(JsonElement))
        ]);
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentDesignMetadataCatalog.FromProviders([
            new MappingComponentDesignMetadataProvider()
        ]);

        catalog.TryGet(
            new ComponentType(MappingCompositionNodeTypes.Mapper),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull().Type.ShouldBe(
            new ComponentType(MappingCompositionNodeTypes.Mapper));
    }

    [Fact]
    public async Task Canonical_host_resolves_engine_and_maps_json_values()
    {
        JsonElement? observedInput = null;
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, resultType) =>
            {
                resultType.ShouldBe(typeof(JsonElement));
                observedInput = context.Variables["input"].ShouldBeOfType<JsonElement>();
                return JsonSerializer.SerializeToElement(new
                {
                    mapped = observedInput.Value.GetProperty("value").GetString()
                });
            });
        var value = JsonSerializer.SerializeToElement(new { value = "input" });
        var request = FlowMessage.Create(value);

        await WithNodeAsync(
            engine,
            async (ports, _) =>
            {
                var resultReceive = ports.ReceiveAsync<JsonElement>(Output, Timeout);
                var eventReceive = ports.ReceiveAsync<CompositionComponentEvent>(Events, Timeout);

                (await ports.SendAsync(Input, request)).IsAccepted.ShouldBeTrue();

                var response = (await resultReceive).Message.ShouldNotBeNull();
                response.IsError.ShouldBeFalse();
                response.Value.GetProperty("mapped").GetString().ShouldBe("input");
                response.CorrelationId.ShouldBe(request.CorrelationId);
                response.TraceId.ShouldBe(request.TraceId);
                response.CausationId.ShouldBe(request.MessageId);
                observedInput.ShouldBe(value);

                var @event = (await eventReceive).Message.ShouldNotBeNull();
                @event.CorrelationId.ShouldBe(request.CorrelationId);
                @event.Value.Name.ShouldBe("flow.mapper.succeeded");
            },
            Properties(
                ("expression", "map"),
                ("boundedCapacity", 8)));
    }

    [Fact]
    public async Task Canonical_host_uses_optional_context_factory_and_option_metadata()
    {
        var contextFactory = new RecordingContextFactory();
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, _) => context.Variables["mapped"]);

        await WithNodeAsync(
            engine,
            async (ports, _) =>
            {
                var receive = ports.ReceiveAsync<JsonElement>(Output, Timeout);
                var input = JsonSerializer.SerializeToElement("value");

                (await ports.SendAsync(Input, FlowMessage.Create(input))).IsAccepted.ShouldBeTrue();

                var result = (await receive).Message.ShouldNotBeNull();
                result.Value.GetString().ShouldBe("custom:value");
                contextFactory.Input.ShouldBe(input);
                contextFactory.Context.ShouldNotBeNull().Options.ExpressionName
                    .ShouldBe("custom-map");
                contextFactory.Context.InputType.ShouldBe(typeof(JsonElement));
                contextFactory.Context.OutputType.ShouldBe(typeof(JsonElement));
            },
            Properties(
                ("expression", "map"),
                ("expressionName", "custom-map"),
                ("inputType", "app.input"),
                ("outputType", "app.output")),
            contextFactory);
    }

    [Fact]
    public async Task Canonical_host_emits_failures_as_normal_results_and_continues()
    {
        var calls = 0;
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    throw new InvalidOperationException("invalid value");
                return context.Variables["input"];
            });

        await WithNodeAsync(
            engine,
            async (ports, _) =>
            {
                var firstReceive = ports.ReceiveAsync<JsonElement>(Output, Timeout);
                var invalid = JsonSerializer.SerializeToElement("invalid");
                (await ports.SendAsync(Input, FlowMessage.Create(invalid))).IsAccepted.ShouldBeTrue();
                var failure = (await firstReceive).Message.ShouldNotBeNull();
                failure.IsError.ShouldBeTrue();
                failure.Error.ShouldNotBeNull().Code.ShouldBe("mapping.mapper_failed");

                var secondReceive = ports.ReceiveAsync<JsonElement>(Output, Timeout);
                var valid = JsonSerializer.SerializeToElement("valid");
                (await ports.SendAsync(Input, FlowMessage.Create(valid))).IsAccepted.ShouldBeTrue();
                var success = (await secondReceive).Message.ShouldNotBeNull();
                success.IsError.ShouldBeFalse();
                success.Value.ShouldBe(valid);
            },
            Properties(("expression", "map")));
    }

    [Fact]
    public async Task Missing_engine_resource_reference_surfaces_preparation_failure()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                MappingCompositionNodeTypes.Mapper,
                Properties(("expression", "map"))),
            registry => registry.RegisterMapper());

        AssertPreparationFailure(host, MappingCompositionResourceNames.Engine);
    }

    [Fact]
    public async Task Invalid_mapper_configuration_surfaces_preparation_failure()
    {
        await using var host = await StartHostAsync(
            new RecordingExpressionEngine(),
            Properties(
                ("expression", "map"),
                ("boundedCapacity", 0)));

        AssertPreparationFailure(host, "Mapper capacity must be positive");
    }

    private static ComponentDesignMetadata DesignMetadata()
        => new MappingComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

    private static async Task WithNodeAsync(
        IFlowExpressionEngine engine,
        Func<ApplicationPortRuntime, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?> properties,
        IMappingContextFactory? contextFactory = null,
        TimeProvider? clock = null)
    {
        await using var host = await StartHostAsync(engine, properties, contextFactory, clock);
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
    }

    private static ValueTask<CanonicalApplicationTestHost> StartHostAsync(
        IFlowExpressionEngine engine,
        IReadOnlyDictionary<string, object?> properties,
        IMappingContextFactory? contextFactory = null,
        TimeProvider? clock = null)
    {
        var componentProperties = properties.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal);
        componentProperties[MappingCompositionResourceNames.Engine] = "Resources.engine";
        var resources = new List<string> { "engine" };
        if (contextFactory is not null)
        {
            componentProperties[MappingCompositionResourceNames.ContextFactory] =
                "Resources.contextFactory";
            resources.Add("contextFactory");
        }
        if (clock is not null)
        {
            componentProperties[MappingCompositionResourceNames.Clock] = "Resources.clock";
            resources.Add("clock");
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                MappingCompositionNodeTypes.Mapper,
                componentProperties,
                resources),
            registry => registry.RegisterMapper(),
            configureRuntimeServices: context =>
            {
                context.Services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                    ApplicationAddress.Resource("engine"),
                    engine);
                if (contextFactory is not null)
                {
                    context.Services.AddExternalFluxFlowResource<IMappingContextFactory>(
                        ApplicationAddress.Resource("contextFactory"),
                        contextFactory);
                }
                if (clock is not null)
                {
                    context.Services.AddExternalFluxFlowResource<TimeProvider>(
                        ApplicationAddress.Resource("clock"),
                        clock);
                }
            });
    }

    private static void AssertOptionHints(
        OptionDesignMetadata option,
        string section,
        string importance,
        string editor,
        string? syntax = null,
        string? relatedResource = null)
    {
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Section)
            .ShouldBe(section);
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Importance)
            .ShouldBe(importance);
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Editor)
            .ShouldBe(editor);

        if (syntax is null)
        {
            option.Attributes.ContainsKey(
                new ComponentAttributeName(OptionDesignMetadataAttributeNames.Syntax))
                .ShouldBeFalse();
        }
        else
        {
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Syntax)
                .ShouldBe(syntax);
        }

        if (relatedResource is null)
        {
            option.Attributes.ContainsKey(
                new ComponentAttributeName(OptionDesignMetadataAttributeNames.RelatedResource))
                .ShouldBeFalse();
        }
        else
        {
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.RelatedResource)
                .ShouldBe(relatedResource);
        }
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

    private sealed class RecordingContextFactory : IMappingContextFactory
    {
        public JsonElement? Input { get; private set; }

        public MappingNodeContext? Context { get; private set; }

        public FlowMapContext Create(object? input, MappingNodeContext context)
        {
            Input = input.ShouldBeOfType<JsonElement>();
            Context = context;
            return new FlowMapContext
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = Input,
                    ["value"] = Input,
                    ["mapped"] = JsonSerializer.SerializeToElement($"custom:{Input.Value.GetString()}")
                }
            };
        }
    }

    private sealed class RecordingExpressionEngine(
        string name = "test",
        Func<string, FlowMapContext, Type, object?>? evaluate = null)
        : IFlowExpressionEngine
    {
        public string Name { get; } = name;

        public object? Evaluate(
            string expression,
            FlowMapContext context,
            Type resultType)
            => evaluate?.Invoke(expression, context, resultType)
                ?? context.Variables["input"];
    }
}
