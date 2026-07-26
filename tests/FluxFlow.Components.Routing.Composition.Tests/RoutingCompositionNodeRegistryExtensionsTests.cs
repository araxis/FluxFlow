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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Routing.Composition.Tests;

public sealed class RoutingCompositionNodeRegistryExtensionsTests
{
    [Fact]
    public void RegisterRoutingNodes_registers_canonical_json_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterWindow()
            .RegisterCorrelation()
            .RegisterJoin();

        registry.Registrations[RoutingCompositionNodeTypes.Window]
            .Outputs[RoutingCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(FlowWindow<JsonElement>));
        registry.Registrations[RoutingCompositionNodeTypes.Correlation]
            .Outputs[RoutingCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(FlowCorrelationOutcome<JsonElement>));
        registry.Registrations[RoutingCompositionNodeTypes.Correlation]
            .Outputs.Keys.ShouldBe([
                RoutingCompositionPortNames.Output,
                CompositionComponentEvents.PortName
            ], ignoreOrder: false);
        registry.Registrations[RoutingCompositionNodeTypes.Join]
            .Inputs.Values.Select(input => input.MessageType).ShouldBe([
                typeof(JsonElement),
                typeof(JsonElement)
            ]);
        registry.Registrations[RoutingCompositionNodeTypes.Join]
            .Outputs[RoutingCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(FlowJoinOutcome<JsonElement, JsonElement>));
    }

    [Fact]
    public void RegisterRoutingNodes_supports_multiple_custom_node_types()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterJoin("flow.join.messages")
            .RegisterJoin("flow.join.values");
        registry.Registrations["flow.join.messages"]
            .Outputs[RoutingCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(FlowJoinOutcome<JsonElement, JsonElement>));
        registry.Registrations["flow.join.values"]
            .Inputs[RoutingCompositionPortNames.Left].MessageType.ShouldBe(
                typeof(JsonElement));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_routing_metadata()
    {
        var metadata = new RoutingComponentDesignMetadataProvider().GetMetadata();

        metadata.Select(item => item.Type.Value).ShouldBe([
            RoutingCompositionNodeTypes.Window,
            RoutingCompositionNodeTypes.Correlation,
            RoutingCompositionNodeTypes.Join
        ]);
        metadata.SelectMany(ComponentDesignMetadataValidator.Validate).ShouldBeEmpty();

        var optionNames = metadata.SelectMany(item => item.Options)
            .Select(option => option.Name.Value)
            .ToArray();
        optionNames.ShouldNotContain(RoutingCompositionResourceNames.Clock);
        optionNames.ShouldNotContain(RoutingCompositionResourceNames.KeySelector);
        optionNames.ShouldNotContain(RoutingCompositionResourceNames.SideSelector);
        optionNames.ShouldNotContain(RoutingCompositionResourceNames.LeftKeySelector);
        optionNames.ShouldNotContain(RoutingCompositionResourceNames.RightKeySelector);

        var byType = metadata.ToDictionary(item => item.Type.Value, StringComparer.Ordinal);
        AssertResources(
            byType[RoutingCompositionNodeTypes.Window],
            [(RoutingCompositionResourceNames.Clock, 0, false, nameof(TimeProvider))]);
        AssertResources(
            byType[RoutingCompositionNodeTypes.Correlation],
            [
                (RoutingCompositionResourceNames.KeySelector, 0, true, "Func<JsonElement,string?>"),
                (RoutingCompositionResourceNames.SideSelector, 1, true, "Func<JsonElement,string?>"),
                (RoutingCompositionResourceNames.Clock, 2, false, nameof(TimeProvider))
            ]);
        AssertResources(
            byType[RoutingCompositionNodeTypes.Join],
            [
                (RoutingCompositionResourceNames.LeftKeySelector, 0, true, "Func<JsonElement,string?>"),
                (RoutingCompositionResourceNames.RightKeySelector, 1, true, "Func<JsonElement,string?>"),
                (RoutingCompositionResourceNames.Clock, 2, false, nameof(TimeProvider))
            ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_static_routing_ports()
    {
        var metadata = MetadataByType();

        AssertPorts(
            metadata[RoutingCompositionNodeTypes.Window],
            [
                (RoutingCompositionPortNames.Input, PortDirection.Input, 0, true, nameof(JsonElement)),
                (RoutingCompositionPortNames.Output, PortDirection.Output, 1, true, "FlowWindow<JsonElement>")
            ]);
        AssertPorts(
            metadata[RoutingCompositionNodeTypes.Correlation],
            [
                (RoutingCompositionPortNames.Input, PortDirection.Input, 0, true, nameof(JsonElement)),
                (RoutingCompositionPortNames.Output, PortDirection.Output, 1, true, "FlowCorrelationOutcome<JsonElement>")
            ]);
        AssertPorts(
            metadata[RoutingCompositionNodeTypes.Join],
            [
                (RoutingCompositionPortNames.Left, PortDirection.Input, 0, true, nameof(JsonElement)),
                (RoutingCompositionPortNames.Right, PortDirection.Input, 1, false, nameof(JsonElement)),
                (RoutingCompositionPortNames.Output, PortDirection.Output, 2, true, "FlowJoinOutcome<JsonElement,JsonElement>")
            ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_routing_options()
    {
        var metadata = MetadataByType();

        AssertOptionNames(
            metadata[RoutingCompositionNodeTypes.Window],
            ["inputType", "maxItems", "timeMilliseconds", "emitPartialOnCompletion", "boundedCapacity"]);
        AssertOption(
            metadata[RoutingCompositionNodeTypes.Window],
            "maxItems",
            OptionValueKind.Number,
            0,
            0);
        AssertOption(
            metadata[RoutingCompositionNodeTypes.Window],
            "emitPartialOnCompletion",
            OptionValueKind.Boolean,
            true);

        AssertOptionNames(
            metadata[RoutingCompositionNodeTypes.Correlation],
            [
                "engine", "keyExpression", "sideExpression", "expressionId",
                "expressionName", "inputType", "requestSide", "responseSide",
                "caseSensitive", "timeoutMilliseconds", "maxPending",
                "boundedCapacity"
            ]);
        AssertOption(
            metadata[RoutingCompositionNodeTypes.Correlation],
            "keyExpression",
            OptionValueKind.Expression);
        AssertOption(
            metadata[RoutingCompositionNodeTypes.Correlation],
            "timeoutMilliseconds",
            OptionValueKind.Number,
            30_000,
            1);
        AssertOption(
            metadata[RoutingCompositionNodeTypes.Correlation],
            "maxPending",
            OptionValueKind.Number,
            1_024,
            1);

        AssertOptionNames(
            metadata[RoutingCompositionNodeTypes.Join],
            [
                "engine", "leftKeyExpression", "rightKeyExpression",
                "expressionId", "expressionName", "leftInputType",
                "rightInputType", "caseSensitive", "timeoutMilliseconds",
                "maxPending", "boundedCapacity"
            ]);
        AssertOption(
            metadata[RoutingCompositionNodeTypes.Join],
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

        var windowOptions = OptionsByName(metadata[RoutingCompositionNodeTypes.Window]);
        AssertOptionHints(windowOptions["inputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(windowOptions["maxItems"], "Windowing", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(windowOptions["timeMilliseconds"], "Windowing", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(windowOptions["emitPartialOnCompletion"], "Windowing", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(windowOptions["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);

        var correlationOptions = OptionsByName(metadata[RoutingCompositionNodeTypes.Correlation]);
        AssertOptionHints(correlationOptions["engine"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            correlationOptions["keyExpression"],
            "Selection",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: RoutingCompositionResourceNames.KeySelector);
        AssertOptionHints(
            correlationOptions["sideExpression"],
            "Selection",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: RoutingCompositionResourceNames.SideSelector);
        AssertOptionHints(correlationOptions["expressionId"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(correlationOptions["expressionName"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(correlationOptions["inputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(correlationOptions["requestSide"], "Matching", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(correlationOptions["responseSide"], "Matching", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(correlationOptions["caseSensitive"], "Matching", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(correlationOptions["timeoutMilliseconds"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(correlationOptions["maxPending"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(correlationOptions["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);

        var joinOptions = OptionsByName(metadata[RoutingCompositionNodeTypes.Join]);
        AssertOptionHints(joinOptions["engine"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            joinOptions["leftKeyExpression"],
            "Selection",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: RoutingCompositionResourceNames.LeftKeySelector);
        AssertOptionHints(
            joinOptions["rightKeyExpression"],
            "Selection",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: RoutingCompositionResourceNames.RightKeySelector);
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
            ResourcesByName(metadata[RoutingCompositionNodeTypes.Window])[RoutingCompositionResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");

        AttributeValue(metadata[RoutingCompositionNodeTypes.Correlation].Attributes, "requiredResources")
            .ShouldBe($"{RoutingCompositionResourceNames.KeySelector},{RoutingCompositionResourceNames.SideSelector}");
        var correlationResources = ResourcesByName(metadata[RoutingCompositionNodeTypes.Correlation]);
        correlationResources[RoutingCompositionResourceNames.KeySelector].IsRequired.ShouldBeTrue();
        correlationResources[RoutingCompositionResourceNames.SideSelector].IsRequired.ShouldBeTrue();
        AssertResourceHints(
            correlationResources[RoutingCompositionResourceNames.KeySelector],
            ResourceDesignMetadataAttributeValues.Delegate,
            "delegate:{name}");
        AssertResourceHints(
            correlationResources[RoutingCompositionResourceNames.SideSelector],
            ResourceDesignMetadataAttributeValues.Delegate,
            "delegate:{name}");
        AssertResourceHints(
            correlationResources[RoutingCompositionResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");

        AttributeValue(metadata[RoutingCompositionNodeTypes.Join].Attributes, "requiredResources")
            .ShouldBe($"{RoutingCompositionResourceNames.LeftKeySelector},{RoutingCompositionResourceNames.RightKeySelector}");
        var joinResources = ResourcesByName(metadata[RoutingCompositionNodeTypes.Join]);
        joinResources[RoutingCompositionResourceNames.LeftKeySelector].IsRequired.ShouldBeTrue();
        joinResources[RoutingCompositionResourceNames.RightKeySelector].IsRequired.ShouldBeTrue();
        AssertResourceHints(
            joinResources[RoutingCompositionResourceNames.LeftKeySelector],
            ResourceDesignMetadataAttributeValues.Delegate,
            "delegate:{name}");
        AssertResourceHints(
            joinResources[RoutingCompositionResourceNames.RightKeySelector],
            ResourceDesignMetadataAttributeValues.Delegate,
            "delegate:{name}");
        AssertResourceHints(
            joinResources[RoutingCompositionResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var provider = new RoutingComponentDesignMetadataProvider();

        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.All.Count.ShouldBe(3);
        catalog.TryGet(
            new ComponentType(RoutingCompositionNodeTypes.Join),
            out var join).ShouldBeTrue();
        join.ShouldNotBeNull();
        join.Type.ShouldBe(new ComponentType(RoutingCompositionNodeTypes.Join));
    }

    [Fact]
    public async Task Hosted_window_binds_options_and_uses_keyed_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T12:30:00Z");
        await using var host = await StartNodeAsync(
            RoutingCompositionNodeTypes.Window,
            Properties(
                (RoutingCompositionResourceNames.Clock, "Resources.fixed"),
                ("maxItems", 2),
                ("boundedCapacity", 8)),
            ["fixed"],
            registry => registry.RegisterWindow(),
            services => services.AddExternalFluxFlowResource<TimeProvider>(
                ApplicationAddress.Resource("fixed"),
                new FakeTimeProvider(timestamp)));
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        var outputResult = ports.ReceiveAsync<FlowWindow<JsonElement>>(
            Port(RoutingCompositionPortNames.Output),
            Timeout);
        var first = FlowMessage.Create(
            JsonSerializer.SerializeToElement(10),
            new CorrelationId("window"));

        (await ports.SendAsync(Port(RoutingCompositionPortNames.Input), first))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(
            Port(RoutingCompositionPortNames.Input),
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
            RoutingCompositionNodeTypes.Correlation,
            Properties(
                (RoutingCompositionResourceNames.KeySelector, "Resources.key"),
                (RoutingCompositionResourceNames.SideSelector, "Resources.side"),
                ("requestSide", "request"),
                ("responseSide", "response")),
            ["key", "side"],
            registry => registry.RegisterCorrelation(),
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
                CompositionComponentEvents.PortName,
                RoutingCompositionPortNames.Output
            ], ignoreOrder: false);
        var outputResult = ports.ReceiveAsync<FlowCorrelationOutcome<JsonElement>>(
            Port(RoutingCompositionPortNames.Output),
            Timeout);
        var request = FlowMessage.Create(
            RoutingItem("A-350", "request", "left"),
            new CorrelationId("request"));

        (await ports.SendAsync(Port(RoutingCompositionPortNames.Input), request))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(Port(RoutingCompositionPortNames.Input), FlowMessage.Create(
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
            RoutingCompositionNodeTypes.Join,
            Properties(
                (RoutingCompositionResourceNames.LeftKeySelector, "Resources.left"),
                (RoutingCompositionResourceNames.RightKeySelector, "Resources.right"),
                (RoutingCompositionResourceNames.Clock, "Resources.fixed"),
                ("boundedCapacity", 8),
                ("timeoutMilliseconds", 5000)),
            ["left", "right", "fixed"],
            registry => registry.RegisterJoin(),
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
            Port(RoutingCompositionPortNames.Output), Timeout);
        var leftMessage = FlowMessage.Create(
            RoutingItem("A-400", "left", "left"),
            new CorrelationId("left"));

        (await ports.SendAsync(Port(RoutingCompositionPortNames.Left), leftMessage))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(Port(RoutingCompositionPortNames.Right), FlowMessage.Create(
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
            RoutingCompositionNodeTypes.Correlation,
            Properties((RoutingCompositionResourceNames.SideSelector, "Resources.side")),
            ["side"],
            registry => registry.RegisterCorrelation(),
            services => services.AddExternalFluxFlowResource<Func<JsonElement, string?>>(
                ApplicationAddress.Resource("side"),
                input => input.GetProperty("side").GetString()));

        AssertPreparationFailure(host, RoutingCompositionResourceNames.KeySelector);
    }

    [Fact]
    public async Task Invalid_routing_options_surface_factory_diagnostic()
    {
        await AssertFactoryDiagnosticAsync(
            RoutingCompositionNodeTypes.Window,
            Properties(("maxItems", -1)),
            null,
            null,
            registry => registry.RegisterWindow(),
            "MaxItems");

        await AssertFactoryDiagnosticAsync(
            RoutingCompositionNodeTypes.Correlation,
            Properties(
                (RoutingCompositionResourceNames.KeySelector, "Resources.key"),
                (RoutingCompositionResourceNames.SideSelector, "Resources.side"),
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
            registry => registry.RegisterCorrelation(),
            "TimeoutMilliseconds");
    }

    private static Dictionary<string, ComponentDesignMetadata> MetadataByType()
        => new RoutingComponentDesignMetadataProvider()
            .GetMetadata()
            .ToDictionary(item => item.Type.Value, StringComparer.Ordinal);

    private static void AssertPorts(
        ComponentDesignMetadata metadata,
        IReadOnlyList<(string Name, PortDirection Direction, int Order, bool IsPrimary, string ValueType)> expected)
    {
        metadata.Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.IsPrimary,
            port.ValueType?.Value!)).ShouldBe(expected);
    }

    private static void AssertOptionNames(
        ComponentDesignMetadata metadata,
        IReadOnlyList<string> expected)
    {
        metadata.Options.Select(option => option.Name.Value).ShouldBe(expected);
    }

    private static Dictionary<string, OptionDesignMetadata> OptionsByName(
        ComponentDesignMetadata metadata)
        => metadata.Options.ToDictionary(
            option => option.Name.Value,
            StringComparer.Ordinal);

    private static Dictionary<string, ResourceDesignMetadata> ResourcesByName(
        ComponentDesignMetadata metadata)
        => metadata.Resources.ToDictionary(
            resource => resource.Name.Value,
            StringComparer.Ordinal);

    private static void AssertOption(
        ComponentDesignMetadata metadata,
        string optionName,
        OptionValueKind kind,
        object? defaultValue = null,
        double? min = null,
        bool? isRequired = null)
    {
        var option = metadata.Options.Single(option => option.Name.Value == optionName);
        option.Kind.ShouldBe(kind);

        if (defaultValue is not null)
        {
            option.DefaultValue.ShouldBe(defaultValue);
        }

        if (min.HasValue)
        {
            option.Min.ShouldBe(min);
        }

        if (isRequired.HasValue)
        {
            option.IsRequired.ShouldBe(isRequired.Value);
        }
    }

    private static void AssertResources(
        ComponentDesignMetadata metadata,
        IReadOnlyList<(string Name, int Order, bool IsRequired, string ValueType)> expected)
    {
        metadata.Resources.Select(resource => (
            resource.Name.Value,
            resource.Order,
            resource.IsRequired,
            resource.ValueType?.Value!)).ShouldBe(expected);
    }

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

    private static async Task AssertFactoryDiagnosticAsync(
        string componentType,
        IReadOnlyDictionary<string, object?> properties,
        IReadOnlyList<string>? resources,
        Action<IServiceCollection>? configureRuntimeServices,
        Action<CompositionNodeRegistry> registerNodes,
        string expectedMessage)
    {
        await using var host = await StartNodeAsync(
            componentType,
            properties,
            resources,
            registerNodes,
            configureRuntimeServices);
        AssertPreparationFailure(host, expectedMessage);
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
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString().Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static JsonElement RoutingItem(string key, string side, string value)
        => JsonSerializer.SerializeToElement(new { key, side, value });

}
