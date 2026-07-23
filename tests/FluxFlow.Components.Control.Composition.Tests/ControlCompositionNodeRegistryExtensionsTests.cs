using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Control.Composition;
using FluxFlow.Components.Control.Diagnostics;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting;
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

namespace FluxFlow.Components.Control.Composition.Tests;

#pragma warning disable CS0618

public sealed class ControlCompositionNodeRegistryExtensionsTests
{
    [Fact]
    public void Legacy_registrations_and_metadata_point_to_canonical_link_conditions()
    {
        var methods = typeof(ControlCompositionNodeRegistryExtensions)
            .GetMethods()
            .Where(method =>
                method.Name is nameof(ControlCompositionNodeRegistryExtensions.RegisterFilter)
                    or nameof(ControlCompositionNodeRegistryExtensions.RegisterWhen))
            .ToArray();

        methods.Length.ShouldBe(2);
        foreach (var method in methods)
        {
            var attribute = method
                .GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false)
                .ShouldHaveSingleItem()
                .ShouldBeOfType<ObsoleteAttribute>();
            attribute.Message.ShouldBe("Use canonical conditional workflow links instead.");
            attribute.IsError.ShouldBeFalse();
        }

        foreach (var metadata in new ControlComponentDesignMetadataProvider().GetMetadata())
        {
            metadata.Attributes[new ComponentAttributeName("deprecated")]
                .Value.ShouldBe("true");
            metadata.Attributes[new ComponentAttributeName("deprecationReason")]
                .Value.ShouldBe("Use canonical conditional workflow links.");
        }
    }

    [Fact]
    public void RegisterFilter_registers_closed_filter_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterFilter<InputMessage>();

        var filter = registry.Registrations[ControlCompositionNodeTypes.Filter];
        filter.Inputs[ControlCompositionPortNames.Input].MessageType.ShouldBe(
            typeof(InputMessage));
        filter.Outputs[ControlCompositionPortNames.Output].MessageType.ShouldBe(
            typeof(InputMessage));
    }

    [Fact]
    public void RegisterWhen_registers_closed_when_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterWhen<InputMessage>();

        var when = registry.Registrations[ControlCompositionNodeTypes.When];
        when.Inputs[ControlCompositionPortNames.Input].MessageType.ShouldBe(
            typeof(InputMessage));
        when.Outputs[ControlCompositionPortNames.WhenTrue].MessageType.ShouldBe(
            typeof(InputMessage));
        when.Outputs[ControlCompositionPortNames.WhenFalse].MessageType.ShouldBe(
            typeof(InputMessage));
        when.Outputs[ControlCompositionPortNames.Output].MessageType.ShouldBe(
            typeof(InputMessage));
    }

    [Fact]
    public void RegisterFilterAndWhen_support_multiple_custom_node_types()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterFilter<InputMessage>("flow.filter.input")
            .RegisterFilter<string>("flow.filter.string")
            .RegisterWhen<InputMessage>("flow.when.input")
            .RegisterWhen<int>("flow.when.int");

        registry.Registrations["flow.filter.input"]
            .Outputs[ControlCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(InputMessage));
        registry.Registrations["flow.filter.string"]
            .Outputs[ControlCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(string));
        registry.Registrations["flow.when.input"]
            .Outputs[ControlCompositionPortNames.WhenFalse].MessageType.ShouldBe(
                typeof(InputMessage));
        registry.Registrations["flow.when.int"]
            .Outputs[ControlCompositionPortNames.WhenFalse].MessageType.ShouldBe(
                typeof(int));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_control_metadata()
    {
        var metadata = new ControlComponentDesignMetadataProvider()
            .GetMetadata()
            .OrderBy(item => item.Type.Value)
            .ToArray();

        metadata.Length.ShouldBe(2);
        metadata.Select(item => item.Type).ShouldBe([
            new ComponentType(ControlCompositionNodeTypes.Filter),
            new ComponentType(ControlCompositionNodeTypes.When)
        ]);
        foreach (var item in metadata)
        {
            ComponentDesignMetadataValidator.Validate(item).ShouldBeEmpty();
            item.Category.ShouldBe(new ComponentCategory("Control"));
            item.SuggestedEditorWidth.ShouldBe(420);
            item.Options.Select(option => (option.Name.Value, option.Kind)).ShouldBe([
                ("expression", OptionValueKind.Expression),
                ("expressionId", OptionValueKind.Text),
                ("expressionName", OptionValueKind.Text),
                ("engine", OptionValueKind.Text),
                ("inputType", OptionValueKind.Text),
                ("boundedCapacity", OptionValueKind.Number)
            ]);
            item.Options.Single(option => option.Name.Value == "expression")
                .IsRequired.ShouldBeTrue();
            item.Options.Single(option => option.Name.Value == "boundedCapacity")
                .Min.ShouldBe(1);
            item.Options.Select(option => option.Name.Value)
                .ShouldNotContain(ControlCompositionResourceNames.ContextFactory);
            item.Options.Select(option => option.Name.Value)
                .ShouldNotContain(ControlCompositionResourceNames.Clock);
            AssertResources(
                item,
                (ControlCompositionResourceNames.Engine, true, nameof(IFlowExpressionEngine)),
                (ControlCompositionResourceNames.ContextFactory, false, "IFlowMapContextFactory<TInput>"),
                (ControlCompositionResourceNames.Clock, false, nameof(TimeProvider)));
        }
    }

    [Fact]
    public void Design_metadata_provider_describes_filter_ports()
    {
        var metadata = new ControlComponentDesignMetadataProvider()
            .GetMetadata()
            .Single(item => item.Type == new ComponentType(ControlCompositionNodeTypes.Filter));

        metadata.Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.IsPrimary,
            port.ValueType?.Value)).ShouldBe([
            (ControlCompositionPortNames.Input, PortDirection.Input, 0, true, "TInput"),
            (ControlCompositionPortNames.Output, PortDirection.Output, 1, true, "TInput")
        ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_control_option_hints()
    {
        var metadata = new ControlComponentDesignMetadataProvider()
            .GetMetadata();

        foreach (var item in metadata)
        {
            var options = item.Options.ToDictionary(
                option => option.Name.Value,
                StringComparer.Ordinal);

            AssertOptionHints(
                options["expression"],
                "Control",
                OptionDesignMetadataAttributeValues.Primary,
                OptionDesignMetadataAttributeValues.Expression,
                syntax: OptionDesignMetadataAttributeValues.Expression,
                relatedResource: ControlCompositionResourceNames.Engine);
            AssertOptionHints(options["expressionId"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
            AssertOptionHints(options["expressionName"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
            AssertOptionHints(options["engine"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
            AssertOptionHints(options["inputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
            AssertOptionHints(options["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        }
    }

    [Fact]
    public void Design_metadata_provider_describes_control_resource_picker_hints()
    {
        var metadata = new ControlComponentDesignMetadataProvider()
            .GetMetadata();

        foreach (var item in metadata)
        {
            var resources = item.Resources.ToDictionary(
                resource => resource.Name.Value,
                StringComparer.Ordinal);

            AssertResourceHints(
                resources[ControlCompositionResourceNames.Engine],
                ResourceDesignMetadataAttributeValues.ExpressionEngine,
                "expression-engine:{name}");
            AssertResourceHints(
                resources[ControlCompositionResourceNames.ContextFactory],
                ResourceDesignMetadataAttributeValues.ContextFactory,
                "context-factory:{name}");
            AssertResourceHints(
                resources[ControlCompositionResourceNames.Clock],
                ResourceDesignMetadataAttributeValues.Clock,
                "clock:{name}");
        }
    }

    [Fact]
    public void Design_metadata_provider_describes_when_ports_and_output_alias()
    {
        var metadata = new ControlComponentDesignMetadataProvider()
            .GetMetadata()
            .Single(item => item.Type == new ComponentType(ControlCompositionNodeTypes.When));

        metadata.Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.IsPrimary,
            port.ValueType?.Value)).ShouldBe([
            (ControlCompositionPortNames.Input, PortDirection.Input, 0, true, "TInput"),
            (ControlCompositionPortNames.Output, PortDirection.Output, 1, true, "TInput"),
            (ControlCompositionPortNames.WhenTrue, PortDirection.Output, 2, false, "TInput"),
            (ControlCompositionPortNames.WhenFalse, PortDirection.Output, 3, false, "TInput")
        ]);
        metadata.Ports.Single(port => port.Name.Value == ControlCompositionPortNames.Output)
            .Attributes[new ComponentAttributeName("aliasOf")].Value.ShouldBe(ControlCompositionPortNames.WhenTrue);
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var provider = new ControlComponentDesignMetadataProvider();

        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.TryGet(
            new ComponentType(ControlCompositionNodeTypes.Filter),
            out var filter).ShouldBeTrue();
        catalog.TryGet(
            new ComponentType(ControlCompositionNodeTypes.When),
            out var when).ShouldBeTrue();
        filter.ShouldNotBeNull();
        when.ShouldNotBeNull();
    }

    private static void AssertResources(
        ComponentDesignMetadata metadata,
        params (string Name, bool IsRequired, string ValueType)[] expected)
    {
        metadata.Resources.Count.ShouldBe(expected.Length);

        for (var index = 0; index < expected.Length; index++)
        {
            var resource = metadata.Resources[index];
            resource.Name.Value.ShouldBe(expected[index].Name);
            resource.Order.ShouldBe(index);
            resource.IsRequired.ShouldBe(expected[index].IsRequired);
            resource.ValueType?.Value.ShouldBe(expected[index].ValueType);
        }
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
            option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.Syntax))
                .ShouldBeFalse();
        }
        else
        {
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Syntax)
                .ShouldBe(syntax);
        }

        if (relatedResource is null)
        {
            option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.RelatedResource))
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

    [Fact]
    public async Task Hosted_filter_resolves_keyed_engine_and_forwards_only_matches()
    {
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, resultType) =>
            {
                resultType.ShouldBe(typeof(bool));
                var input = (InputMessage)context.Variables["input"]!;
                return input.Value >= 10;
            });
        await using var host = await StartNodeAsync(
            ControlCompositionNodeTypes.Filter,
            Properties(
                (ControlCompositionResourceNames.Engine, "Resources.primary"),
                ("expression", "input.Value >= 10"),
                ("boundedCapacity", 8)),
            ["primary"],
            registry => registry.RegisterFilter<InputMessage>(),
            services => services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                ApplicationAddress.Resource("primary"),
                engine));
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        var observation = (await ports.ObserveAsync<InputMessage>(Port(ControlCompositionPortNames.Output)))
            .Observation.ShouldNotBeNull();
        await using var observationScope = observation;
        var rejected = FlowMessage.Create(
            new InputMessage(3),
            new CorrelationId("filter-rejected"));
        var accepted = FlowMessage.Create(
            new InputMessage(12),
            new CorrelationId("filter-accepted"));

        (await ports.SendAsync(Port(ControlCompositionPortNames.Input), rejected))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(Port(ControlCompositionPortNames.Input), accepted))
            .IsAccepted.ShouldBeTrue();

        var response = await observation.Messages.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        response.CorrelationId.ShouldBe(new CorrelationId("filter-accepted"));
        response.Payload.Value.ShouldBe(12);
        observation.Messages
            .ShouldBeAssignableTo<IReceivableSourceBlock<FlowMessage<InputMessage>>>()
            .TryReceive(out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Hosted_when_routes_true_and_false_branches()
    {
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, _) =>
            {
                var input = (InputMessage)context.Variables["input"]!;
                return input.Value >= 10;
            });
        await using var host = await StartNodeAsync(
            ControlCompositionNodeTypes.When,
            Properties(
                (ControlCompositionResourceNames.Engine, "Resources.primary"),
                ("expression", "input.Value >= 10"),
                ("boundedCapacity", 8)),
            ["primary"],
            registry => registry.RegisterWhen<InputMessage>(),
            services => services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                ApplicationAddress.Resource("primary"),
                engine));
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        var trueResult = ports.ReceiveAsync<InputMessage>(Port(ControlCompositionPortNames.WhenTrue), TimeSpan.FromSeconds(5));
        var falseResult = ports.ReceiveAsync<InputMessage>(Port(ControlCompositionPortNames.WhenFalse), TimeSpan.FromSeconds(5));
        var outputResult = ports.ReceiveAsync<InputMessage>(Port(ControlCompositionPortNames.Output), TimeSpan.FromSeconds(5));
        var rejected = FlowMessage.Create(
            new InputMessage(3),
            new CorrelationId("when-false"));
        var accepted = FlowMessage.Create(
            new InputMessage(12),
            new CorrelationId("when-true"));

        (await ports.SendAsync(Port(ControlCompositionPortNames.Input), rejected))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(Port(ControlCompositionPortNames.Input), accepted))
            .IsAccepted.ShouldBeTrue();

        PortReceiveResult<InputMessage> falseReceive = await falseResult;
        PortReceiveResult<InputMessage> trueReceive = await trueResult;
        PortReceiveResult<InputMessage> outputReceive = await outputResult;
        var falseResponse = falseReceive.Message;
        var trueResponse = trueReceive.Message;
        var outputResponse = outputReceive.Message;
        falseResponse.ShouldNotBeNull();
        trueResponse.ShouldNotBeNull();
        outputResponse.ShouldNotBeNull();
        falseResponse.CorrelationId.ShouldBe(new CorrelationId("when-false"));
        falseResponse.Payload.Value.ShouldBe(3);
        trueResponse.CorrelationId.ShouldBe(new CorrelationId("when-true"));
        trueResponse.Payload.Value.ShouldBe(12);
        outputResponse.CorrelationId.ShouldBe(new CorrelationId("when-true"));
        outputResponse.Payload.Value.ShouldBe(12);
    }

    [Fact]
    public async Task Hosted_filter_binds_options_from_configuration()
    {
        var engine = new RecordingExpressionEngine(evaluate: (_, _, _) => true);
        await using var host = await StartNodeAsync(
            ControlCompositionNodeTypes.Filter,
            Properties(
                (ControlCompositionResourceNames.Engine, "Resources.primary"),
                ("expression", "pass"),
                ("expressionName", "configured-filter"),
                ("inputType", "app.input"),
                ("boundedCapacity", 8)),
            ["primary"],
            registry => registry.RegisterFilter<object>(),
            services => services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                ApplicationAddress.Resource("primary"),
                engine));
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        var eventResult = ports.ReceiveAsync<CompositionComponentEvent>(
            Port(CompositionComponentEvents.PortName),
            TimeSpan.FromSeconds(5));
        (await ports.SendAsync(
            Port(ControlCompositionPortNames.Input),
            FlowMessage.Create<object>("value"))).IsAccepted.ShouldBeTrue();

        PortReceiveResult<CompositionComponentEvent> eventReceive = await eventResult;
        var eventMessage = eventReceive.Message;
        eventMessage.ShouldNotBeNull();
        var @event = eventMessage.Payload;
        @event.Attributes["inputType"].GetString().ShouldBe("app.input");
        @event.Attributes["expressionName"].GetString().ShouldBe("configured-filter");
    }

    [Fact]
    public async Task Hosted_when_uses_optional_keyed_context_factory()
    {
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, _) => context.Variables["matches"]);
        await using var host = await StartNodeAsync(
            ControlCompositionNodeTypes.When,
            Properties(
                (ControlCompositionResourceNames.Engine, "Resources.primary"),
                (ControlCompositionResourceNames.ContextFactory, "Resources.custom"),
                ("expression", "matches"),
                ("boundedCapacity", 8)),
            ["primary", "custom"],
            registry => registry.RegisterWhen<InputMessage>(),
            services =>
            {
                services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                    ApplicationAddress.Resource("primary"),
                    engine);
                services.AddExternalFluxFlowResource<IFlowMapContextFactory<InputMessage>>(
                    ApplicationAddress.Resource("custom"),
                    new CustomContextFactory(matches: true));
            });
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        var receive = ports.ReceiveAsync<InputMessage>(
            Port(ControlCompositionPortNames.WhenTrue),
            TimeSpan.FromSeconds(5));
        var message = FlowMessage.Create(
            new InputMessage(1),
            new CorrelationId("custom-context"));
        (await ports.SendAsync(Port(ControlCompositionPortNames.Input), message))
            .IsAccepted.ShouldBeTrue();

        PortReceiveResult<InputMessage> receiveResult = await receive;
        var result = receiveResult.Message;
        result.ShouldNotBeNull();
        result.CorrelationId.ShouldBe(new CorrelationId("custom-context"));
        result.Payload.Value.ShouldBe(1);
    }

    [Fact]
    public async Task Hosted_filter_uses_optional_keyed_clock_for_diagnostics()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T13:00:00Z");
        var engine = new RecordingExpressionEngine(evaluate: (_, _, _) => true);
        await using var host = await StartNodeAsync(
            ControlCompositionNodeTypes.Filter,
            Properties(
                (ControlCompositionResourceNames.Engine, "Resources.primary"),
                (ControlCompositionResourceNames.Clock, "Resources.fixed"),
                ("expression", "pass"),
                ("boundedCapacity", 8)),
            ["primary", "fixed"],
            registry => registry.RegisterFilter<InputMessage>(),
            services =>
            {
                services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                    ApplicationAddress.Resource("primary"),
                    engine);
                services.AddExternalFluxFlowResource<TimeProvider>(
                    ApplicationAddress.Resource("fixed"),
                    new FakeTimeProvider(timestamp));
            });
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        var eventResult = ports.ReceiveAsync<CompositionComponentEvent>(
            Port(CompositionComponentEvents.PortName),
            TimeSpan.FromSeconds(5));
        (await ports.SendAsync(
            Port(ControlCompositionPortNames.Input),
            FlowMessage.Create(new InputMessage(1)))).IsAccepted.ShouldBeTrue();

        PortReceiveResult<CompositionComponentEvent> eventReceive = await eventResult;
        var eventMessage = eventReceive.Message;
        eventMessage.ShouldNotBeNull();
        eventMessage.Payload.Timestamp.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Missing_engine_resource_reference_surfaces_factory_diagnostic()
    {
        await using var host = await StartNodeAsync(
            ControlCompositionNodeTypes.Filter,
            Properties(("expression", "pass")),
            null,
            registry => registry.RegisterFilter<object>());

        AssertPreparationFailure(host, ControlCompositionResourceNames.Engine);
    }

    [Fact]
    public async Task Invalid_filter_options_surface_factory_diagnostic()
    {
        var engine = new RecordingExpressionEngine(evaluate: (_, _, _) => true);
        await using var host = await StartNodeAsync(
            ControlCompositionNodeTypes.Filter,
            Properties(
                (ControlCompositionResourceNames.Engine, "Resources.primary"),
                ("expression", "pass"),
                ("boundedCapacity", 0)),
            ["primary"],
            registry => registry.RegisterFilter<object>(),
            services => services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                ApplicationAddress.Resource("primary"),
                engine));

        AssertPreparationFailure(host, "BoundedCapacity");
    }

    [Fact]
    public async Task Missing_filter_expression_surfaces_factory_diagnostic()
    {
        var engine = new RecordingExpressionEngine(evaluate: (_, _, _) => true);
        await using var host = await StartNodeAsync(
            ControlCompositionNodeTypes.Filter,
            Properties((ControlCompositionResourceNames.Engine, "Resources.primary")),
            ["primary"],
            registry => registry.RegisterFilter<object>(),
            services => services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                ApplicationAddress.Resource("primary"),
                engine));

        AssertPreparationFailure(host, "Expression");
    }

    [Fact]
    public async Task Invalid_when_options_surface_factory_diagnostic()
    {
        var engine = new RecordingExpressionEngine(evaluate: (_, _, _) => true);
        await using var host = await StartNodeAsync(
            ControlCompositionNodeTypes.When,
            Properties(
                (ControlCompositionResourceNames.Engine, "Resources.primary"),
                ("expression", "route"),
                ("inputType", " ")),
            ["primary"],
            registry => registry.RegisterWhen<object>(),
            services => services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                ApplicationAddress.Resource("primary"),
                engine));

        AssertPreparationFailure(host, "InputType");
    }

    private static ValueTask<CanonicalApplicationTestHost> StartNodeAsync(
        string componentType,
        IReadOnlyDictionary<string, object?> properties,
        IReadOnlyList<string>? resources,
        Action<CompositionNodeRegistry> registerNodes,
        Action<IServiceCollection>? configureRuntimeServices = null)
        => CanonicalApplicationTestHost.StartAsync(
            SingleComponent(componentType, properties, resources),
            registerNodes,
            configureRuntimeServices: configureRuntimeServices is null
                ? null
                : context => configureRuntimeServices(context.Services));

    private static ApplicationAddress Port(string name)
        => ApplicationAddress.WorkflowPort("main", "node", name);

    private static void AssertPreparationFailure(
        CanonicalApplicationTestHost host,
        string expectedMessage)
    {
        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        host.StartResult.Update.Failures.ShouldContain(failure =>
            failure.Stage == ApplicationRevisionFailureStage.Preparation &&
            failure.Error.Details.GetObject()["exceptionMessage"].GetString().Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private sealed record InputMessage(int Value);

    private sealed class CustomContextFactory(bool matches) :
        IFlowMapContextFactory<InputMessage>
    {
        public FlowMapContext Create(InputMessage input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["matches"] = matches
                }
            };
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
