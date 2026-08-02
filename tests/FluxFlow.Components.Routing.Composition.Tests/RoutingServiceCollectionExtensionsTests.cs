using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Routing.Composition;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using static FluxFlow.Testing.ComponentDesignMetadataAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Routing.Composition.Tests;

public sealed class RoutingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRouting_registers_canonical_json_metadata()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddFluxFlowComponents().AddRouting());

        registry.Components[RoutingComponentDefinition.Types.Window]
            .Outputs[RoutingComponentDefinition.Ports.Output].MessageType.ShouldBe(
                typeof(FlowWindow<JsonElement>));
        registry.Components[RoutingComponentDefinition.Types.Correlation]
            .Outputs[RoutingComponentDefinition.Ports.Output].MessageType.ShouldBe(
                typeof(FlowCorrelationOutcome<JsonElement>));
        registry.Components[RoutingComponentDefinition.Types.Correlation]
            .Outputs.Keys.ShouldBe([
                RoutingComponentDefinition.Ports.Output,
                ComponentEvents.PortName
            ], ignoreOrder: false);
        registry.Components[RoutingComponentDefinition.Types.Join]
            .Inputs.Values.Select(input => input.MessageType).ShouldBe([
                typeof(JsonElement),
                typeof(JsonElement)
            ]);
        registry.Components[RoutingComponentDefinition.Types.Join]
            .Outputs[RoutingComponentDefinition.Ports.Output].MessageType.ShouldBe(
                typeof(FlowJoinOutcome<JsonElement, JsonElement>));
    }

    [Fact]
    public void AddRouting_is_idempotent()
    {
        var catalog = ComponentCatalogTestHost.Create(services =>
        {
            services.AddFluxFlowComponents().AddRouting();
            services.AddFluxFlowComponents().AddRouting();
        });

        catalog.Components.Keys.ShouldBe([
            RoutingComponentDefinition.Types.Correlation,
            RoutingComponentDefinition.Types.Join,
            RoutingComponentDefinition.Types.Window
        ]);
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_routing_metadata()
    {
        var metadata = DesignMetadata();

        metadata.Select(item => item.Type.Value).ShouldBe([
            RoutingComponentDefinition.Types.Window,
            RoutingComponentDefinition.Types.Correlation,
            RoutingComponentDefinition.Types.Join
        ]);
        metadata.SelectMany(ComponentDesignMetadataValidator.Validate).ShouldBeEmpty();

        var optionNames = metadata.SelectMany(item => item.Options)
            .Select(option => option.Name.Value)
            .ToArray();
        optionNames.ShouldNotContain(RoutingComponentDefinition.Resources.Clock);
        optionNames.ShouldNotContain(RoutingComponentDefinition.Resources.KeySelector);
        optionNames.ShouldNotContain(RoutingComponentDefinition.Resources.SideSelector);
        optionNames.ShouldNotContain(RoutingComponentDefinition.Resources.LeftKeySelector);
        optionNames.ShouldNotContain(RoutingComponentDefinition.Resources.RightKeySelector);

        var byType = metadata.ToDictionary(item => item.Type.Value, StringComparer.Ordinal);
        AssertResources(
            byType[RoutingComponentDefinition.Types.Window],
            [
                (RoutingComponentDefinition.Resources.Clock, 0, false, nameof(TimeProvider)),
                ("processing", int.MaxValue, false, "CompositionProcessingProfile")
            ]);
        AssertResources(
            byType[RoutingComponentDefinition.Types.Correlation],
            [
                (RoutingComponentDefinition.Resources.KeySelector, 0, true, "Func<JsonElement,string?>"),
                (RoutingComponentDefinition.Resources.SideSelector, 1, true, "Func<JsonElement,string?>"),
                (RoutingComponentDefinition.Resources.Clock, 2, false, nameof(TimeProvider)),
                ("processing", int.MaxValue, false, "CompositionProcessingProfile")
            ]);
        AssertResources(
            byType[RoutingComponentDefinition.Types.Join],
            [
                (RoutingComponentDefinition.Resources.LeftKeySelector, 0, true, "Func<JsonElement,string?>"),
                (RoutingComponentDefinition.Resources.RightKeySelector, 1, true, "Func<JsonElement,string?>"),
                (RoutingComponentDefinition.Resources.Clock, 2, false, nameof(TimeProvider)),
                ("processing", int.MaxValue, false, "CompositionProcessingProfile")
            ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_static_routing_ports()
    {
        var metadata = MetadataByType();

        AssertPorts(
            metadata[RoutingComponentDefinition.Types.Window],
            [
                (RoutingComponentDefinition.Ports.Input, PortDirection.Input, 0, true, nameof(JsonElement)),
                (RoutingComponentDefinition.Ports.Output, PortDirection.Output, 1, true, "FlowWindow<JsonElement>"),
                ("Events", PortDirection.Output, int.MaxValue, false, nameof(ComponentEvent))
            ]);
        AssertPorts(
            metadata[RoutingComponentDefinition.Types.Correlation],
            [
                (RoutingComponentDefinition.Ports.Input, PortDirection.Input, 0, true, nameof(JsonElement)),
                (RoutingComponentDefinition.Ports.Output, PortDirection.Output, 1, true, "FlowCorrelationOutcome<JsonElement>"),
                ("Events", PortDirection.Output, int.MaxValue, false, nameof(ComponentEvent))
            ]);
        AssertPorts(
            metadata[RoutingComponentDefinition.Types.Join],
            [
                (RoutingComponentDefinition.Ports.Left, PortDirection.Input, 0, true, nameof(JsonElement)),
                (RoutingComponentDefinition.Ports.Right, PortDirection.Input, 1, false, nameof(JsonElement)),
                (RoutingComponentDefinition.Ports.Output, PortDirection.Output, 2, true, "FlowJoinOutcome<JsonElement,JsonElement>"),
                ("Events", PortDirection.Output, int.MaxValue, false, nameof(ComponentEvent))
            ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_routing_options()
    {
        var metadata = MetadataByType();

        AssertOptionNames(
            metadata[RoutingComponentDefinition.Types.Window],
            ["inputType", "maxItems", "timeMilliseconds", "emitPartialOnCompletion", "boundedCapacity", "processing"]);
        AssertOption(
            metadata[RoutingComponentDefinition.Types.Window],
            "maxItems",
            OptionValueKind.Number,
            0,
            0);
        AssertOption(
            metadata[RoutingComponentDefinition.Types.Window],
            "emitPartialOnCompletion",
            OptionValueKind.Boolean,
            true);

        AssertOptionNames(
            metadata[RoutingComponentDefinition.Types.Correlation],
            [
                "engine", "keyExpression", "sideExpression", "expressionId",
                "expressionName", "inputType", "requestSide", "responseSide",
                "caseSensitive", "timeoutMilliseconds", "maxPending",
                "boundedCapacity", "processing"
            ]);
        AssertOption(
            metadata[RoutingComponentDefinition.Types.Correlation],
            "keyExpression",
            OptionValueKind.Expression);
        AssertOption(
            metadata[RoutingComponentDefinition.Types.Correlation],
            "timeoutMilliseconds",
            OptionValueKind.Number,
            30_000,
            1);
        AssertOption(
            metadata[RoutingComponentDefinition.Types.Correlation],
            "maxPending",
            OptionValueKind.Number,
            1_024,
            1);

        AssertOptionNames(
            metadata[RoutingComponentDefinition.Types.Join],
            [
                "engine", "leftKeyExpression", "rightKeyExpression",
                "expressionId", "expressionName", "leftInputType",
                "rightInputType", "caseSensitive", "timeoutMilliseconds",
                "maxPending", "boundedCapacity", "processing"
            ]);
        AssertOption(
            metadata[RoutingComponentDefinition.Types.Join],
            "leftInputType",
            OptionValueKind.Text,
            "object");

    }

    [Fact]
    public void Design_metadata_provider_describes_routing_option_hints()
    {
        var metadata = MetadataByType();

        var windowOptions = OptionsByName(metadata[RoutingComponentDefinition.Types.Window]);
        AssertOptionHints(windowOptions["inputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(windowOptions["maxItems"], "Windowing", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(windowOptions["timeMilliseconds"], "Windowing", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(windowOptions["emitPartialOnCompletion"], "Windowing", OptionDesignMetadataAttributeValues.Advanced);

        var correlationOptions = OptionsByName(metadata[RoutingComponentDefinition.Types.Correlation]);
        AssertOptionHints(correlationOptions["engine"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            correlationOptions["keyExpression"],
            "Selection",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: RoutingComponentDefinition.Resources.KeySelector);
        AssertOptionHints(
            correlationOptions["sideExpression"],
            "Selection",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: RoutingComponentDefinition.Resources.SideSelector);
        AssertOptionHints(correlationOptions["expressionId"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(correlationOptions["expressionName"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(correlationOptions["inputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(correlationOptions["requestSide"], "Matching", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(correlationOptions["responseSide"], "Matching", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(correlationOptions["caseSensitive"], "Matching", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(correlationOptions["timeoutMilliseconds"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(correlationOptions["maxPending"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);

        var joinOptions = OptionsByName(metadata[RoutingComponentDefinition.Types.Join]);
        AssertOptionHints(joinOptions["engine"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            joinOptions["leftKeyExpression"],
            "Selection",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: RoutingComponentDefinition.Resources.LeftKeySelector);
        AssertOptionHints(
            joinOptions["rightKeyExpression"],
            "Selection",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: RoutingComponentDefinition.Resources.RightKeySelector);
        AssertOptionHints(joinOptions["expressionId"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(joinOptions["expressionName"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(joinOptions["leftInputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(joinOptions["rightInputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(joinOptions["caseSensitive"], "Matching", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(joinOptions["timeoutMilliseconds"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(joinOptions["maxPending"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public void Design_metadata_provider_describes_routing_resource_picker_hints()
    {
        var metadata = MetadataByType();

        AssertResourceHints(
            ResourcesByName(metadata[RoutingComponentDefinition.Types.Window])[RoutingComponentDefinition.Resources.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");

        AttributeValue(metadata[RoutingComponentDefinition.Types.Correlation].Attributes, "requiredResources")
            .ShouldBe($"{RoutingComponentDefinition.Resources.KeySelector},{RoutingComponentDefinition.Resources.SideSelector}");
        var correlationResources = ResourcesByName(metadata[RoutingComponentDefinition.Types.Correlation]);
        correlationResources[RoutingComponentDefinition.Resources.KeySelector].IsRequired.ShouldBeTrue();
        correlationResources[RoutingComponentDefinition.Resources.SideSelector].IsRequired.ShouldBeTrue();
        AssertResourceHints(
            correlationResources[RoutingComponentDefinition.Resources.KeySelector],
            ResourceDesignMetadataAttributeValues.Delegate,
            "delegate:{name}");
        AssertResourceHints(
            correlationResources[RoutingComponentDefinition.Resources.SideSelector],
            ResourceDesignMetadataAttributeValues.Delegate,
            "delegate:{name}");
        AssertResourceHints(
            correlationResources[RoutingComponentDefinition.Resources.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");

        AttributeValue(metadata[RoutingComponentDefinition.Types.Join].Attributes, "requiredResources")
            .ShouldBe($"{RoutingComponentDefinition.Resources.LeftKeySelector},{RoutingComponentDefinition.Resources.RightKeySelector}");
        var joinResources = ResourcesByName(metadata[RoutingComponentDefinition.Types.Join]);
        joinResources[RoutingComponentDefinition.Resources.LeftKeySelector].IsRequired.ShouldBeTrue();
        joinResources[RoutingComponentDefinition.Resources.RightKeySelector].IsRequired.ShouldBeTrue();
        AssertResourceHints(
            joinResources[RoutingComponentDefinition.Resources.LeftKeySelector],
            ResourceDesignMetadataAttributeValues.Delegate,
            "delegate:{name}");
        AssertResourceHints(
            joinResources[RoutingComponentDefinition.Resources.RightKeySelector],
            ResourceDesignMetadataAttributeValues.Delegate,
            "delegate:{name}");
        AssertResourceHints(
            joinResources[RoutingComponentDefinition.Resources.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentCatalogTestHost.CreateDesignMetadataCatalog(
            static services => services.AddFluxFlowComponents().AddRouting());

        catalog.All.Count.ShouldBe(3);
        catalog.TryGet(
            new ComponentType(RoutingComponentDefinition.Types.Join),
            out var join).ShouldBeTrue();
        join.ShouldNotBeNull();
        join.Type.ShouldBe(new ComponentType(RoutingComponentDefinition.Types.Join));
    }

    [Fact]
    public async Task Hosted_window_binds_options_and_uses_keyed_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T12:30:00Z");
        await using var host = await StartNodeAsync(
            RoutingComponentDefinition.Types.Window,
            Properties(
                (RoutingComponentDefinition.Resources.Clock, "Resources.fixed"),
                ("maxItems", 2),
                ("boundedCapacity", 8)),
            ["fixed"],
            registry => registry.AddFluxFlowComponents().AddRouting(),
            services => services.AddExternalFluxFlowResource<TimeProvider>(
                ApplicationAddress.Resource("fixed"),
                new FakeTimeProvider(timestamp)));
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        var outputResult = ports.ReceiveAsync<FlowWindow<JsonElement>>(
            Port(RoutingComponentDefinition.Ports.Output),
            Timeout);
        var first = FlowMessage.Create(
            JsonSerializer.SerializeToElement(10),
            new CorrelationId("window"));

        (await ports.SendAsync(Port(RoutingComponentDefinition.Ports.Input), first))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(
            Port(RoutingComponentDefinition.Ports.Input),
            FlowMessage.Create(JsonSerializer.SerializeToElement(20))))
            .IsAccepted.ShouldBeTrue();

        var window = (await outputResult).Message.ShouldNotBeNull();

        window.CorrelationId.ShouldBe(first.CorrelationId);
        window.IsError.ShouldBeFalse();
        window.Value.Items.Select(item => item.GetInt32()).ShouldBe([10, 20]);
        window.Value.StartedAt.ShouldBe(timestamp);
        window.Value.EmittedAt.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Hosted_canonical_correlation_resolves_json_selectors()
    {
        await using var host = await StartNodeAsync(
            RoutingComponentDefinition.Types.Correlation,
            Properties(
                (RoutingComponentDefinition.Resources.KeySelector, "Resources.key"),
                (RoutingComponentDefinition.Resources.SideSelector, "Resources.side"),
                ("requestSide", "request"),
                ("responseSide", "response")),
            ["key", "side"],
            registry => registry.AddFluxFlowComponents().AddRouting(),
            services =>
            {
                services.AddExternalFluxFlowResource<Func<JsonElement, string?>>(
                    ApplicationAddress.Resource("key"),
                    value => value.GetProperty("key").GetString());
                services.AddExternalFluxFlowResource<Func<JsonElement, string?>>(
                    ApplicationAddress.Resource("side"),
                    value => value.GetProperty("side").GetString());
            });
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        ports.Metadata
            .Where(port =>
                port.Direction == ApplicationPortDirection.Output &&
                port.Address.Kind == ApplicationAddressKind.WorkflowPort &&
                port.Address.Segments[0] == "main" &&
                port.Address.Segments[1] == "node")
            .Select(port => port.Address.Segments[^1])
            .ShouldBe([
                ComponentEvents.PortName,
                RoutingComponentDefinition.Ports.Output
            ], ignoreOrder: false);
        var outputResult = ports.ReceiveAsync<FlowCorrelationOutcome<JsonElement>>(
            Port(RoutingComponentDefinition.Ports.Output),
            Timeout);
        var request = FlowMessage.Create(
            RoutingItem("A-350", "request", "left"),
            new CorrelationId("request"));

        (await ports.SendAsync(Port(RoutingComponentDefinition.Ports.Input), request))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(Port(RoutingComponentDefinition.Ports.Input), FlowMessage.Create(
                RoutingItem("A-350", "response", "right"),
                new CorrelationId("response"))))
            .IsAccepted.ShouldBeTrue();

        var result = (await outputResult).Message.ShouldNotBeNull();
        result.CorrelationId.ShouldBe(request.CorrelationId);
        result.Value
            .ShouldBeOfType<FlowCorrelationMatchedOutcome<JsonElement>>()
            .Match.Key.ShouldBe("A-350");
    }

    [Fact]
    public async Task Hosted_join_resolves_selectors_and_routes_matches()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T13:30:00Z");
        await using var host = await StartNodeAsync(
            RoutingComponentDefinition.Types.Join,
            Properties(
                (RoutingComponentDefinition.Resources.LeftKeySelector, "Resources.left"),
                (RoutingComponentDefinition.Resources.RightKeySelector, "Resources.right"),
                (RoutingComponentDefinition.Resources.Clock, "Resources.fixed"),
                ("boundedCapacity", 8),
                ("timeoutMilliseconds", 5000)),
            ["left", "right", "fixed"],
            registry => registry.AddFluxFlowComponents().AddRouting(),
            services =>
            {
                services.AddExternalFluxFlowResource<Func<JsonElement, string?>>(
                    ApplicationAddress.Resource("left"),
                    input => input.GetProperty("key").GetString());
                services.AddExternalFluxFlowResource<Func<JsonElement, string?>>(
                    ApplicationAddress.Resource("right"),
                    input => input.GetProperty("key").GetString());
                services.AddExternalFluxFlowResource<TimeProvider>(
                    ApplicationAddress.Resource("fixed"),
                    new FakeTimeProvider(timestamp));
            });
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        var outputResult = ports.ReceiveAsync<FlowJoinOutcome<JsonElement, JsonElement>>(
            Port(RoutingComponentDefinition.Ports.Output), Timeout);
        var leftMessage = FlowMessage.Create(
            RoutingItem("A-400", "left", "left"),
            new CorrelationId("left"));

        (await ports.SendAsync(Port(RoutingComponentDefinition.Ports.Left), leftMessage))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(Port(RoutingComponentDefinition.Ports.Right), FlowMessage.Create(
                RoutingItem("A-400", "right", "right"),
                new CorrelationId("right"))))
            .IsAccepted.ShouldBeTrue();

        var result = (await outputResult).Message.ShouldNotBeNull();

        result.CorrelationId.ShouldBe(leftMessage.CorrelationId);
        result.IsError.ShouldBeFalse();
        var match = result.Value
            .ShouldBeOfType<FlowJoinMatchedOutcome<JsonElement, JsonElement>>()
            .Match;
        match.Key.ShouldBe("A-400");
        match.Left.GetProperty("value").GetString().ShouldBe("left");
        match.Right.GetProperty("value").GetString().ShouldBe("right");
        match.JoinedAt.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Missing_required_selector_surfaces_factory_diagnostic()
    {
        await using var host = await StartNodeAsync(
            RoutingComponentDefinition.Types.Correlation,
            Properties((RoutingComponentDefinition.Resources.SideSelector, "Resources.side")),
            ["side"],
            registry => registry.AddFluxFlowComponents().AddRouting(),
            services => services.AddExternalFluxFlowResource<Func<JsonElement, string?>>(
                ApplicationAddress.Resource("side"),
                input => input.GetProperty("side").GetString()));

        AssertPreparationFailure(host, RoutingComponentDefinition.Resources.KeySelector);
    }

    [Fact]
    public async Task Invalid_routing_options_surface_factory_diagnostic()
    {
        await AssertFactoryDiagnosticAsync(
            RoutingComponentDefinition.Types.Window,
            Properties(("maxItems", -1)),
            null,
            null,
            registry => registry.AddFluxFlowComponents().AddRouting(),
            "MaxItems");

        await AssertFactoryDiagnosticAsync(
            RoutingComponentDefinition.Types.Correlation,
            Properties(
                (RoutingComponentDefinition.Resources.KeySelector, "Resources.key"),
                (RoutingComponentDefinition.Resources.SideSelector, "Resources.side"),
                ("timeoutMilliseconds", 0)),
            ["key", "side"],
            services =>
            {
                services.AddExternalFluxFlowResource<Func<JsonElement, string?>>(
                    ApplicationAddress.Resource("key"),
                    input => input.GetProperty("key").GetString());
                services.AddExternalFluxFlowResource<Func<JsonElement, string?>>(
                    ApplicationAddress.Resource("side"),
                    input => input.GetProperty("side").GetString());
            },
            registry => registry.AddFluxFlowComponents().AddRouting(),
            "TimeoutMilliseconds");
    }

    private static Dictionary<string, ComponentDesignMetadata> MetadataByType()
        => DesignMetadata()
            .ToDictionary(item => item.Type.Value, StringComparer.Ordinal);

    private static IReadOnlyList<ComponentDesignMetadata> DesignMetadata()
        => ComponentCatalogTestHost.CreateDesignMetadataCatalog(
            services => services.AddFluxFlowComponents().AddRouting()).All;

    private static void AssertOptionNames(
        ComponentDesignMetadata metadata,
        IReadOnlyList<string> expected)
    {
        metadata.Options.Select(option => option.Name.Value).ShouldBe(expected);
    }

    private static async Task AssertFactoryDiagnosticAsync(
        string componentType,
        IReadOnlyDictionary<string, object?> properties,
        IReadOnlyList<string>? resources,
        Action<IServiceCollection>? configureRuntimeServices,
        Action<IServiceCollection> addComponents,
        string expectedMessage)
    {
        await using var host = await StartNodeAsync(
            componentType,
            properties,
            resources,
            addComponents,
            configureRuntimeServices);
        AssertPreparationFailure(host, expectedMessage);
    }

    private static ValueTask<CanonicalApplicationTestHost> StartNodeAsync(
        string componentType,
        IReadOnlyDictionary<string, object?> properties,
        IReadOnlyList<string>? resources,
        Action<IServiceCollection> addComponents,
        Action<IServiceCollection>? configureRuntimeServices = null)
        => CanonicalApplicationTestHost.StartAsync(
            SingleComponent(componentType, properties, resources),
            addComponents,
            registerResources: configureRuntimeServices is null
                ? null
                : context => configureRuntimeServices(context.Services));

    private static ApplicationAddress Port(string name)
        => ApplicationAddress.WorkflowPort("main", "node", name);

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

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static JsonElement RoutingItem(string key, string side, string value)
        => JsonSerializer.SerializeToElement(new { key, side, value });

}
