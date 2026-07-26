using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Routing.Composition;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting;
using FluxFlow.Composition.Hosting.DependencyInjection;
using FluxFlow.Composition.Hosting.Revisions;
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
    public void AddRoutingComponents_registers_canonical_json_metadata()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddRoutingComponents());

        registry.Components[RoutingComponentTypes.Window]
            .Outputs[RoutingComponentPortNames.Output].MessageType.ShouldBe(
                typeof(FlowWindow<JsonElement>));
        registry.Components[RoutingComponentTypes.Correlation]
            .Outputs[RoutingComponentPortNames.Output].MessageType.ShouldBe(
                typeof(FlowCorrelationOutcome<JsonElement>));
        registry.Components[RoutingComponentTypes.Correlation]
            .Outputs.Keys.ShouldBe([
                RoutingComponentPortNames.Output,
                ComponentEvents.PortName
            ], ignoreOrder: false);
        registry.Components[RoutingComponentTypes.Join]
            .Inputs.Values.Select(input => input.MessageType).ShouldBe([
                typeof(JsonElement),
                typeof(JsonElement)
            ]);
        registry.Components[RoutingComponentTypes.Join]
            .Outputs[RoutingComponentPortNames.Output].MessageType.ShouldBe(
                typeof(FlowJoinOutcome<JsonElement, JsonElement>));
    }

    [Fact]
    public void AddRoutingComponents_is_idempotent()
    {
        var catalog = ComponentCatalogTestHost.Create(services =>
        {
            services.AddRoutingComponents();
            services.AddRoutingComponents();
        });

        catalog.Components.Keys.ShouldBe([
            RoutingComponentTypes.Correlation,
            RoutingComponentTypes.Join,
            RoutingComponentTypes.Window
        ]);
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_routing_metadata()
    {
        var metadata = new RoutingComponentDesignMetadataProvider().GetMetadata();

        metadata.Select(item => item.Type.Value).ShouldBe([
            RoutingComponentTypes.Window,
            RoutingComponentTypes.Correlation,
            RoutingComponentTypes.Join
        ]);
        metadata.SelectMany(ComponentDesignMetadataValidator.Validate).ShouldBeEmpty();

        var optionNames = metadata.SelectMany(item => item.Options)
            .Select(option => option.Name.Value)
            .ToArray();
        optionNames.ShouldNotContain(RoutingComponentResourceNames.Clock);
        optionNames.ShouldNotContain(RoutingComponentResourceNames.KeySelector);
        optionNames.ShouldNotContain(RoutingComponentResourceNames.SideSelector);
        optionNames.ShouldNotContain(RoutingComponentResourceNames.LeftKeySelector);
        optionNames.ShouldNotContain(RoutingComponentResourceNames.RightKeySelector);

        var byType = metadata.ToDictionary(item => item.Type.Value, StringComparer.Ordinal);
        AssertResources(
            byType[RoutingComponentTypes.Window],
            [(RoutingComponentResourceNames.Clock, 0, false, nameof(TimeProvider))]);
        AssertResources(
            byType[RoutingComponentTypes.Correlation],
            [
                (RoutingComponentResourceNames.KeySelector, 0, true, "Func<JsonElement,string?>"),
                (RoutingComponentResourceNames.SideSelector, 1, true, "Func<JsonElement,string?>"),
                (RoutingComponentResourceNames.Clock, 2, false, nameof(TimeProvider))
            ]);
        AssertResources(
            byType[RoutingComponentTypes.Join],
            [
                (RoutingComponentResourceNames.LeftKeySelector, 0, true, "Func<JsonElement,string?>"),
                (RoutingComponentResourceNames.RightKeySelector, 1, true, "Func<JsonElement,string?>"),
                (RoutingComponentResourceNames.Clock, 2, false, nameof(TimeProvider))
            ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_static_routing_ports()
    {
        var metadata = MetadataByType();

        AssertPorts(
            metadata[RoutingComponentTypes.Window],
            [
                (RoutingComponentPortNames.Input, PortDirection.Input, 0, true, nameof(JsonElement)),
                (RoutingComponentPortNames.Output, PortDirection.Output, 1, true, "FlowWindow<JsonElement>")
            ]);
        AssertPorts(
            metadata[RoutingComponentTypes.Correlation],
            [
                (RoutingComponentPortNames.Input, PortDirection.Input, 0, true, nameof(JsonElement)),
                (RoutingComponentPortNames.Output, PortDirection.Output, 1, true, "FlowCorrelationOutcome<JsonElement>")
            ]);
        AssertPorts(
            metadata[RoutingComponentTypes.Join],
            [
                (RoutingComponentPortNames.Left, PortDirection.Input, 0, true, nameof(JsonElement)),
                (RoutingComponentPortNames.Right, PortDirection.Input, 1, false, nameof(JsonElement)),
                (RoutingComponentPortNames.Output, PortDirection.Output, 2, true, "FlowJoinOutcome<JsonElement,JsonElement>")
            ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_routing_options()
    {
        var metadata = MetadataByType();

        AssertOptionNames(
            metadata[RoutingComponentTypes.Window],
            ["inputType", "maxItems", "timeMilliseconds", "emitPartialOnCompletion", "boundedCapacity"]);
        AssertOption(
            metadata[RoutingComponentTypes.Window],
            "maxItems",
            OptionValueKind.Number,
            0,
            0);
        AssertOption(
            metadata[RoutingComponentTypes.Window],
            "emitPartialOnCompletion",
            OptionValueKind.Boolean,
            true);

        AssertOptionNames(
            metadata[RoutingComponentTypes.Correlation],
            [
                "engine", "keyExpression", "sideExpression", "expressionId",
                "expressionName", "inputType", "requestSide", "responseSide",
                "caseSensitive", "timeoutMilliseconds", "maxPending",
                "boundedCapacity"
            ]);
        AssertOption(
            metadata[RoutingComponentTypes.Correlation],
            "keyExpression",
            OptionValueKind.Expression);
        AssertOption(
            metadata[RoutingComponentTypes.Correlation],
            "timeoutMilliseconds",
            OptionValueKind.Number,
            30_000,
            1);
        AssertOption(
            metadata[RoutingComponentTypes.Correlation],
            "maxPending",
            OptionValueKind.Number,
            1_024,
            1);

        AssertOptionNames(
            metadata[RoutingComponentTypes.Join],
            [
                "engine", "leftKeyExpression", "rightKeyExpression",
                "expressionId", "expressionName", "leftInputType",
                "rightInputType", "caseSensitive", "timeoutMilliseconds",
                "maxPending", "boundedCapacity"
            ]);
        AssertOption(
            metadata[RoutingComponentTypes.Join],
            "leftInputType",
            OptionValueKind.Text,
            "object");

        foreach (var item in metadata.Values)
        {
            AssertOption(item, "boundedCapacity", OptionValueKind.Number, 128, 1);
        }
    }

    [Fact]
    public void Design_metadata_provider_describes_routing_option_hints()
    {
        var metadata = MetadataByType();

        var windowOptions = OptionsByName(metadata[RoutingComponentTypes.Window]);
        AssertOptionHints(windowOptions["inputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(windowOptions["maxItems"], "Windowing", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(windowOptions["timeMilliseconds"], "Windowing", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(windowOptions["emitPartialOnCompletion"], "Windowing", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(windowOptions["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);

        var correlationOptions = OptionsByName(metadata[RoutingComponentTypes.Correlation]);
        AssertOptionHints(correlationOptions["engine"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            correlationOptions["keyExpression"],
            "Selection",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: RoutingComponentResourceNames.KeySelector);
        AssertOptionHints(
            correlationOptions["sideExpression"],
            "Selection",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: RoutingComponentResourceNames.SideSelector);
        AssertOptionHints(correlationOptions["expressionId"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(correlationOptions["expressionName"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(correlationOptions["inputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(correlationOptions["requestSide"], "Matching", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(correlationOptions["responseSide"], "Matching", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(correlationOptions["caseSensitive"], "Matching", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(correlationOptions["timeoutMilliseconds"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(correlationOptions["maxPending"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(correlationOptions["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);

        var joinOptions = OptionsByName(metadata[RoutingComponentTypes.Join]);
        AssertOptionHints(joinOptions["engine"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            joinOptions["leftKeyExpression"],
            "Selection",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: RoutingComponentResourceNames.LeftKeySelector);
        AssertOptionHints(
            joinOptions["rightKeyExpression"],
            "Selection",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: RoutingComponentResourceNames.RightKeySelector);
        AssertOptionHints(joinOptions["expressionId"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(joinOptions["expressionName"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(joinOptions["leftInputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(joinOptions["rightInputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(joinOptions["caseSensitive"], "Matching", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(joinOptions["timeoutMilliseconds"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(joinOptions["maxPending"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(joinOptions["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public void Design_metadata_provider_describes_routing_resource_picker_hints()
    {
        var metadata = MetadataByType();

        AssertResourceHints(
            ResourcesByName(metadata[RoutingComponentTypes.Window])[RoutingComponentResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");

        AttributeValue(metadata[RoutingComponentTypes.Correlation].Attributes, "requiredResources")
            .ShouldBe($"{RoutingComponentResourceNames.KeySelector},{RoutingComponentResourceNames.SideSelector}");
        var correlationResources = ResourcesByName(metadata[RoutingComponentTypes.Correlation]);
        correlationResources[RoutingComponentResourceNames.KeySelector].IsRequired.ShouldBeTrue();
        correlationResources[RoutingComponentResourceNames.SideSelector].IsRequired.ShouldBeTrue();
        AssertResourceHints(
            correlationResources[RoutingComponentResourceNames.KeySelector],
            ResourceDesignMetadataAttributeValues.Delegate,
            "delegate:{name}");
        AssertResourceHints(
            correlationResources[RoutingComponentResourceNames.SideSelector],
            ResourceDesignMetadataAttributeValues.Delegate,
            "delegate:{name}");
        AssertResourceHints(
            correlationResources[RoutingComponentResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");

        AttributeValue(metadata[RoutingComponentTypes.Join].Attributes, "requiredResources")
            .ShouldBe($"{RoutingComponentResourceNames.LeftKeySelector},{RoutingComponentResourceNames.RightKeySelector}");
        var joinResources = ResourcesByName(metadata[RoutingComponentTypes.Join]);
        joinResources[RoutingComponentResourceNames.LeftKeySelector].IsRequired.ShouldBeTrue();
        joinResources[RoutingComponentResourceNames.RightKeySelector].IsRequired.ShouldBeTrue();
        AssertResourceHints(
            joinResources[RoutingComponentResourceNames.LeftKeySelector],
            ResourceDesignMetadataAttributeValues.Delegate,
            "delegate:{name}");
        AssertResourceHints(
            joinResources[RoutingComponentResourceNames.RightKeySelector],
            ResourceDesignMetadataAttributeValues.Delegate,
            "delegate:{name}");
        AssertResourceHints(
            joinResources[RoutingComponentResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentCatalogTestHost.CreateDesignMetadataCatalog(
            static services => services.AddRoutingComponents());

        catalog.All.Count.ShouldBe(3);
        catalog.TryGet(
            new ComponentType(RoutingComponentTypes.Join),
            out var join).ShouldBeTrue();
        join.ShouldNotBeNull();
        join.Type.ShouldBe(new ComponentType(RoutingComponentTypes.Join));
    }

    [Fact]
    public async Task Hosted_window_binds_options_and_uses_keyed_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T12:30:00Z");
        await using var host = await StartNodeAsync(
            RoutingComponentTypes.Window,
            Properties(
                (RoutingComponentResourceNames.Clock, "Resources.fixed"),
                ("maxItems", 2),
                ("boundedCapacity", 8)),
            ["fixed"],
            registry => registry.AddRoutingComponents(),
            services => services.AddExternalFluxFlowResource<TimeProvider>(
                ApplicationAddress.Resource("fixed"),
                new FakeTimeProvider(timestamp)));
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        var outputResult = ports.ReceiveAsync<FlowWindow<JsonElement>>(
            Port(RoutingComponentPortNames.Output),
            Timeout);
        var first = FlowMessage.Create(
            JsonSerializer.SerializeToElement(10),
            new CorrelationId("window"));

        (await ports.SendAsync(Port(RoutingComponentPortNames.Input), first))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(
            Port(RoutingComponentPortNames.Input),
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
            RoutingComponentTypes.Correlation,
            Properties(
                (RoutingComponentResourceNames.KeySelector, "Resources.key"),
                (RoutingComponentResourceNames.SideSelector, "Resources.side"),
                ("requestSide", "request"),
                ("responseSide", "response")),
            ["key", "side"],
            registry => registry.AddRoutingComponents(),
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
        ports.Ports
            .Where(port =>
                port.Direction == ApplicationPortDirection.Output &&
                port.Address.Kind == ApplicationAddressKind.WorkflowPort &&
                port.Address.Segments[0] == "main" &&
                port.Address.Segments[1] == "node")
            .Select(port => port.Address.Segments[^1])
            .ShouldBe([
                ComponentEvents.PortName,
                RoutingComponentPortNames.Output
            ], ignoreOrder: false);
        var outputResult = ports.ReceiveAsync<FlowCorrelationOutcome<JsonElement>>(
            Port(RoutingComponentPortNames.Output),
            Timeout);
        var request = FlowMessage.Create(
            RoutingItem("A-350", "request", "left"),
            new CorrelationId("request"));

        (await ports.SendAsync(Port(RoutingComponentPortNames.Input), request))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(Port(RoutingComponentPortNames.Input), FlowMessage.Create(
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
            RoutingComponentTypes.Join,
            Properties(
                (RoutingComponentResourceNames.LeftKeySelector, "Resources.left"),
                (RoutingComponentResourceNames.RightKeySelector, "Resources.right"),
                (RoutingComponentResourceNames.Clock, "Resources.fixed"),
                ("boundedCapacity", 8),
                ("timeoutMilliseconds", 5000)),
            ["left", "right", "fixed"],
            registry => registry.AddRoutingComponents(),
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
            Port(RoutingComponentPortNames.Output), Timeout);
        var leftMessage = FlowMessage.Create(
            RoutingItem("A-400", "left", "left"),
            new CorrelationId("left"));

        (await ports.SendAsync(Port(RoutingComponentPortNames.Left), leftMessage))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(Port(RoutingComponentPortNames.Right), FlowMessage.Create(
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
            RoutingComponentTypes.Correlation,
            Properties((RoutingComponentResourceNames.SideSelector, "Resources.side")),
            ["side"],
            registry => registry.AddRoutingComponents(),
            services => services.AddExternalFluxFlowResource<Func<JsonElement, string?>>(
                ApplicationAddress.Resource("side"),
                input => input.GetProperty("side").GetString()));

        AssertPreparationFailure(host, RoutingComponentResourceNames.KeySelector);
    }

    [Fact]
    public async Task Invalid_routing_options_surface_factory_diagnostic()
    {
        await AssertFactoryDiagnosticAsync(
            RoutingComponentTypes.Window,
            Properties(("maxItems", -1)),
            null,
            null,
            registry => registry.AddRoutingComponents(),
            "MaxItems");

        await AssertFactoryDiagnosticAsync(
            RoutingComponentTypes.Correlation,
            Properties(
                (RoutingComponentResourceNames.KeySelector, "Resources.key"),
                (RoutingComponentResourceNames.SideSelector, "Resources.side"),
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
            registry => registry.AddRoutingComponents(),
            "TimeoutMilliseconds");
    }

    private static Dictionary<string, ComponentDesignMetadata> MetadataByType()
        => new RoutingComponentDesignMetadataProvider()
            .GetMetadata()
            .ToDictionary(item => item.Type.Value, StringComparer.Ordinal);

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
        host.StartResult.Update!.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        host.StartResult.Update.Failures.ShouldContain(failure =>
            failure.Stage == ApplicationRevisionFailureStage.Preparation &&
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!.Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static JsonElement RoutingItem(string key, string side, string value)
        => JsonSerializer.SerializeToElement(new { key, side, value });

}
