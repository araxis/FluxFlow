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

#pragma warning disable CS0618

public sealed class RoutingCompositionNodeRegistryExtensionsTests
{
    [Fact]
    public void RegisterRoutingNodes_registers_static_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterSwitch<InputMessage>()
            .RegisterFork<InputMessage>()
            .RegisterMerge<InputMessage>()
            .RegisterWindow<InputMessage>()
            .RegisterCorrelation<InputMessage>()
            .RegisterJoin<LeftMessage, RightMessage>();

        var flowSwitch = registry.Registrations[RoutingCompositionNodeTypes.Switch];
        flowSwitch.Inputs[RoutingCompositionPortNames.Input].MessageType.ShouldBe(
            typeof(InputMessage));
        flowSwitch.Outputs.Keys.ShouldBe([CompositionComponentEvents.PortName]);

        var fork = registry.Registrations[RoutingCompositionNodeTypes.Fork];
        fork.Inputs[RoutingCompositionPortNames.Input].MessageType.ShouldBe(
            typeof(InputMessage));
        fork.Outputs.Keys.ShouldBe([CompositionComponentEvents.PortName]);

        registry.Registrations[RoutingCompositionNodeTypes.Merge]
            .Outputs[RoutingCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(InputMessage));
        registry.Registrations[RoutingCompositionNodeTypes.Window]
            .Outputs[RoutingCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(FlowWindow<InputMessage>));
        registry.Registrations[RoutingCompositionNodeTypes.Correlation]
            .Outputs[RoutingCompositionPortNames.Timeouts].MessageType.ShouldBe(
                typeof(FlowCorrelationTimeout<InputMessage>));
        registry.Registrations[RoutingCompositionNodeTypes.Join]
            .Inputs[RoutingCompositionPortNames.Left].MessageType.ShouldBe(
                typeof(LeftMessage));
        registry.Registrations[RoutingCompositionNodeTypes.Join]
            .Outputs[RoutingCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(FlowJoinResult<LeftMessage, RightMessage>));
    }

    [Fact]
    public void RegisterRoutingNodes_registers_canonical_flow_value_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterWindow()
            .RegisterCorrelation()
            .RegisterJoin();

        registry.Registrations[RoutingCompositionNodeTypes.Window]
            .Outputs[RoutingCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(FlowResult<FlowWindow<FlowValue>>));
        registry.Registrations[RoutingCompositionNodeTypes.Correlation]
            .Outputs[RoutingCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(FlowResult<FlowCorrelationOutcome<FlowValue>>));
        registry.Registrations[RoutingCompositionNodeTypes.Correlation]
            .Outputs.Keys.ShouldBe([
                RoutingCompositionPortNames.Output,
                CompositionComponentEvents.PortName
            ], ignoreOrder: false);
        registry.Registrations[RoutingCompositionNodeTypes.Join]
            .Inputs.Values.Select(input => input.MessageType).ShouldBe([
                typeof(FlowValue),
                typeof(FlowValue)
            ]);
        registry.Registrations[RoutingCompositionNodeTypes.Join]
            .Outputs[RoutingCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(FlowResult<FlowJoinOutcome<FlowValue, FlowValue>>));
    }

    [Fact]
    public void RegisterRoutingNodes_supports_multiple_custom_node_types()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterSwitch<InputMessage>("flow.switch.input")
            .RegisterSwitch<string>("flow.switch.string")
            .RegisterJoin<LeftMessage, RightMessage>("flow.join.messages")
            .RegisterJoin<string, int>("flow.join.primitives");

        registry.Registrations["flow.switch.input"]
            .Inputs[RoutingCompositionPortNames.Input].MessageType.ShouldBe(
                typeof(InputMessage));
        registry.Registrations["flow.switch.string"]
            .Inputs[RoutingCompositionPortNames.Input].MessageType.ShouldBe(
                typeof(string));
        registry.Registrations["flow.join.messages"]
            .Outputs[RoutingCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(FlowJoinResult<LeftMessage, RightMessage>));
        registry.Registrations["flow.join.primitives"]
            .Outputs[RoutingCompositionPortNames.Timeouts].MessageType.ShouldBe(
                typeof(FlowJoinTimeout<string, int>));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_routing_metadata()
    {
        var metadata = new RoutingComponentDesignMetadataProvider().GetMetadata();

        metadata.Select(item => item.Type.Value).ShouldBe([
            RoutingCompositionNodeTypes.Switch,
            RoutingCompositionNodeTypes.Fork,
            RoutingCompositionNodeTypes.Merge,
            RoutingCompositionNodeTypes.Window,
            RoutingCompositionNodeTypes.Correlation,
            RoutingCompositionNodeTypes.Join
        ]);
        metadata.SelectMany(ComponentDesignMetadataValidator.Validate).ShouldBeEmpty();

        var optionNames = metadata.SelectMany(item => item.Options)
            .Select(option => option.Name.Value)
            .ToArray();
        optionNames.ShouldNotContain(RoutingCompositionResourceNames.Clock);
        optionNames.ShouldNotContain(RoutingCompositionResourceNames.RouteKeySelector);
        optionNames.ShouldNotContain(RoutingCompositionResourceNames.KeySelector);
        optionNames.ShouldNotContain(RoutingCompositionResourceNames.SideSelector);
        optionNames.ShouldNotContain(RoutingCompositionResourceNames.LeftKeySelector);
        optionNames.ShouldNotContain(RoutingCompositionResourceNames.RightKeySelector);

        var byType = metadata.ToDictionary(item => item.Type.Value, StringComparer.Ordinal);
        AssertResources(
            byType[RoutingCompositionNodeTypes.Switch],
            [
                (RoutingCompositionResourceNames.RouteKeySelector, 0, true, "Func<TInput,string?>"),
                (RoutingCompositionResourceNames.Clock, 1, false, nameof(TimeProvider))
            ]);
        AssertResources(
            byType[RoutingCompositionNodeTypes.Fork],
            [(RoutingCompositionResourceNames.Clock, 0, false, nameof(TimeProvider))]);
        AssertResources(
            byType[RoutingCompositionNodeTypes.Merge],
            [(RoutingCompositionResourceNames.Clock, 0, false, nameof(TimeProvider))]);
        AssertResources(
            byType[RoutingCompositionNodeTypes.Window],
            [(RoutingCompositionResourceNames.Clock, 0, false, nameof(TimeProvider))]);
        AssertResources(
            byType[RoutingCompositionNodeTypes.Correlation],
            [
                (RoutingCompositionResourceNames.KeySelector, 0, true, "Func<FlowValue,string?>"),
                (RoutingCompositionResourceNames.SideSelector, 1, true, "Func<FlowValue,string?>"),
                (RoutingCompositionResourceNames.Clock, 2, false, nameof(TimeProvider))
            ]);
        AssertResources(
            byType[RoutingCompositionNodeTypes.Join],
            [
                (RoutingCompositionResourceNames.LeftKeySelector, 0, true, "Func<FlowValue,string?>"),
                (RoutingCompositionResourceNames.RightKeySelector, 1, true, "Func<FlowValue,string?>"),
                (RoutingCompositionResourceNames.Clock, 2, false, nameof(TimeProvider))
            ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_static_routing_ports()
    {
        var metadata = MetadataByType();

        AssertPorts(
            metadata[RoutingCompositionNodeTypes.Switch],
            [
                (RoutingCompositionPortNames.Input, PortDirection.Input, 0, true, "TInput"),
                (RoutingCompositionPortNames.Output, PortDirection.Output, 1, true, "TInput"),
                (RoutingCompositionPortNames.Matched, PortDirection.Output, 2, false, "TInput"),
                (RoutingCompositionPortNames.Default, PortDirection.Output, 3, false, "TInput"),
                (RoutingCompositionPortNames.Routed, PortDirection.Output, 4, false, "TInput")
            ]);
        metadata[RoutingCompositionNodeTypes.Switch].Ports
            .Select(port => port.Name.Value)
            .ShouldNotContain("Priority");
        metadata[RoutingCompositionNodeTypes.Switch].Attributes[new ComponentAttributeName("dynamicOutputsOption")]
            .Value.ShouldBe("routeOutputs");
        metadata[RoutingCompositionNodeTypes.Switch].Attributes[new ComponentAttributeName("deprecated")]
            .Value.ShouldBe("true");

        AssertPorts(
            metadata[RoutingCompositionNodeTypes.Fork],
            [
                (RoutingCompositionPortNames.Input, PortDirection.Input, 0, true, "TInput"),
                (RoutingCompositionPortNames.Output, PortDirection.Output, 1, true, "TInput")
            ]);
        metadata[RoutingCompositionNodeTypes.Fork].Attributes[new ComponentAttributeName("dynamicOutputsOption")]
            .Value.ShouldBe("outputs");
        metadata[RoutingCompositionNodeTypes.Fork].Attributes[new ComponentAttributeName("deprecated")]
            .Value.ShouldBe("true");

        AssertPorts(
            metadata[RoutingCompositionNodeTypes.Merge],
            [
                (RoutingCompositionPortNames.Input, PortDirection.Input, 0, true, "TInput"),
                (RoutingCompositionPortNames.Output, PortDirection.Output, 1, true, "TInput")
            ]);
        metadata[RoutingCompositionNodeTypes.Merge].Attributes[new ComponentAttributeName("deprecated")]
            .Value.ShouldBe("true");
        AssertPorts(
            metadata[RoutingCompositionNodeTypes.Window],
            [
                (RoutingCompositionPortNames.Input, PortDirection.Input, 0, true, nameof(FlowValue)),
                (RoutingCompositionPortNames.Output, PortDirection.Output, 1, true, "FlowResult<FlowWindow<FlowValue>>")
            ]);
        AssertPorts(
            metadata[RoutingCompositionNodeTypes.Correlation],
            [
                (RoutingCompositionPortNames.Input, PortDirection.Input, 0, true, nameof(FlowValue)),
                (RoutingCompositionPortNames.Output, PortDirection.Output, 1, true, "FlowResult<FlowCorrelationOutcome<FlowValue>>")
            ]);
        AssertPorts(
            metadata[RoutingCompositionNodeTypes.Join],
            [
                (RoutingCompositionPortNames.Left, PortDirection.Input, 0, true, nameof(FlowValue)),
                (RoutingCompositionPortNames.Right, PortDirection.Input, 1, false, nameof(FlowValue)),
                (RoutingCompositionPortNames.Output, PortDirection.Output, 2, true, "FlowResult<FlowJoinOutcome<FlowValue,FlowValue>>")
            ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_routing_options()
    {
        var metadata = MetadataByType();

        AssertOptionNames(
            metadata[RoutingCompositionNodeTypes.Switch],
            [
                "engine", "expression", "expressionId", "expressionName", "inputType",
                "routes", "routeOutputs", "defaultRoute", "caseSensitive",
                "emitMatchedInput", "emitDefaultInput", "emitRouteEnvelope",
                "boundedCapacity"
            ]);
        AssertOption(
            metadata[RoutingCompositionNodeTypes.Switch],
            "expression",
            OptionValueKind.Expression);
        AssertOption(
            metadata[RoutingCompositionNodeTypes.Switch],
            "routes",
            OptionValueKind.Json);
        AssertOption(
            metadata[RoutingCompositionNodeTypes.Switch],
            "routeOutputs",
            OptionValueKind.Json);
        AssertOption(
            metadata[RoutingCompositionNodeTypes.Switch],
            "caseSensitive",
            OptionValueKind.Boolean,
            true);

        AssertOptionNames(
            metadata[RoutingCompositionNodeTypes.Fork],
            ["inputType", "outputs", "boundedCapacity"]);
        AssertOption(
            metadata[RoutingCompositionNodeTypes.Fork],
            "outputs",
            OptionValueKind.Json,
            isRequired: true);

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

        var switchOptions = OptionsByName(metadata[RoutingCompositionNodeTypes.Switch]);
        AssertOptionHints(switchOptions["engine"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            switchOptions["expression"],
            "Selection",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: RoutingCompositionResourceNames.RouteKeySelector);
        AssertOptionHints(switchOptions["expressionId"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(switchOptions["expressionName"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(switchOptions["inputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(switchOptions["routes"], "Routing", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Json);
        AssertOptionHints(switchOptions["routeOutputs"], "Routing", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Json);
        AssertOptionHints(switchOptions["defaultRoute"], "Routing", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(switchOptions["caseSensitive"], "Matching", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(switchOptions["emitMatchedInput"], "Branches", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(switchOptions["emitDefaultInput"], "Branches", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(switchOptions["emitRouteEnvelope"], "Branches", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(switchOptions["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);

        var forkOptions = OptionsByName(metadata[RoutingCompositionNodeTypes.Fork]);
        AssertOptionHints(forkOptions["inputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(forkOptions["outputs"], "Routing", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Json);
        AssertOptionHints(forkOptions["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);

        var mergeOptions = OptionsByName(metadata[RoutingCompositionNodeTypes.Merge]);
        AssertOptionHints(mergeOptions["inputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(mergeOptions["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);

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

        AttributeValue(metadata[RoutingCompositionNodeTypes.Switch].Attributes, "dynamicOutputsOption")
            .ShouldBe("routeOutputs");
        AttributeValue(metadata[RoutingCompositionNodeTypes.Switch].Attributes, "requiredResource")
            .ShouldBe(RoutingCompositionResourceNames.RouteKeySelector);
        var switchResources = ResourcesByName(metadata[RoutingCompositionNodeTypes.Switch]);
        switchResources[RoutingCompositionResourceNames.RouteKeySelector].IsRequired.ShouldBeTrue();
        AssertResourceHints(
            switchResources[RoutingCompositionResourceNames.RouteKeySelector],
            ResourceDesignMetadataAttributeValues.Delegate,
            "delegate:{name}");
        AssertResourceHints(
            switchResources[RoutingCompositionResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");

        AttributeValue(metadata[RoutingCompositionNodeTypes.Fork].Attributes, "dynamicOutputsOption")
            .ShouldBe("outputs");
        AssertResourceHints(
            ResourcesByName(metadata[RoutingCompositionNodeTypes.Fork])[RoutingCompositionResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");
        AssertResourceHints(
            ResourcesByName(metadata[RoutingCompositionNodeTypes.Merge])[RoutingCompositionResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");
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

        catalog.All.Count.ShouldBe(6);
        catalog.TryGet(
            new ComponentType(RoutingCompositionNodeTypes.Join),
            out var join).ShouldBeTrue();
        join.ShouldNotBeNull();
        join.Type.ShouldBe(new ComponentType(RoutingCompositionNodeTypes.Join));
    }

    [Fact]
    public async Task Canonical_factory_switch_resolves_selector_and_exposes_configured_ports()
    {
        var services = new ServiceCollection();
        services.AddExternalFluxFlowResource<Func<InputMessage, string?>>(
            ApplicationAddress.Resource("route"),
            input => input.Route);
        await using var provider = services.BuildServiceProvider();
        await using var node = await CreateNodeAsync(
            provider,
            RoutingCompositionNodeTypes.Switch,
            Properties(
                (RoutingCompositionResourceNames.RouteKeySelector, "Resources.route"),
                ("routes", new[] { "priority", "standard" }),
                ("routeOutputs", new Dictionary<string, string>
                {
                    ["priority"] = "Priority"
                }),
                ("emitRouteEnvelope", true),
                ("boundedCapacity", 8)),
            registry => registry.RegisterSwitch<InputMessage>());
        var input = node.Inputs[RoutingCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var outputResults = Link(node.Outputs[RoutingCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>().Source);
        var matchedResults = Link(node.Outputs[RoutingCompositionPortNames.Matched]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>().Source);
        var defaultResults = Link(node.Outputs[RoutingCompositionPortNames.Default]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>().Source);
        var routedResults = Link(node.Outputs[RoutingCompositionPortNames.Routed]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>().Source);
        var priorityResults = Link(node.Outputs["Priority"]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>().Source);

        var first = FlowMessage.Create(
            new InputMessage("priority", "A-100"),
            new CorrelationId("matched"));
        var second = FlowMessage.Create(
            new InputMessage("unknown", "A-101"),
            new CorrelationId("default"));

        (await input.Target.SendAsync(first).WaitAsync(Timeout)).ShouldBeTrue();
        (await input.Target.SendAsync(second).WaitAsync(Timeout)).ShouldBeTrue();

        (await outputResults.ReceiveAsync().WaitAsync(Timeout)).CorrelationId.ShouldBe(first.CorrelationId);
        (await matchedResults.ReceiveAsync().WaitAsync(Timeout)).CorrelationId.ShouldBe(first.CorrelationId);
        (await priorityResults.ReceiveAsync().WaitAsync(Timeout)).Payload.Id.ShouldBe("A-100");
        (await defaultResults.ReceiveAsync().WaitAsync(Timeout)).CorrelationId.ShouldBe(second.CorrelationId);
        var routedMessages = new[]
        {
            await routedResults.ReceiveAsync().WaitAsync(Timeout),
            await routedResults.ReceiveAsync().WaitAsync(Timeout)
        };
        routedMessages.Select(message => message.CorrelationId).ShouldBe(
            [first.CorrelationId, second.CorrelationId],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Canonical_factory_fork_emits_to_configured_ports_and_output_alias()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await using var node = await CreateNodeAsync(
            provider,
            RoutingCompositionNodeTypes.Fork,
            Properties(
                ("outputs", new[] { "Audit", "Work" }),
                ("boundedCapacity", 8)),
            registry => registry.RegisterFork<InputMessage>());
        var input = node.Inputs[RoutingCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var outputResults = Link(node.Outputs[RoutingCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>().Source);
        var auditResults = Link(node.Outputs["Audit"]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>().Source);
        var workResults = Link(node.Outputs["Work"]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>().Source);

        var message = FlowMessage.Create(
            new InputMessage("work", "A-200"),
            new CorrelationId("forked"));

        (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();

        (await outputResults.ReceiveAsync().WaitAsync(Timeout)).CorrelationId.ShouldBe(message.CorrelationId);
        (await auditResults.ReceiveAsync().WaitAsync(Timeout)).Payload.Id.ShouldBe("A-200");
        (await workResults.ReceiveAsync().WaitAsync(Timeout)).CorrelationId.ShouldBe(message.CorrelationId);
    }

    [Fact]
    public async Task Hosted_merge_forwards_inputs_and_uses_keyed_clock_for_events()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T12:00:00Z");
        await using var host = await StartNodeAsync(
            RoutingCompositionNodeTypes.Merge,
            Properties(
                (RoutingCompositionResourceNames.Clock, "Resources.fixed"),
                ("boundedCapacity", 8)),
            ["fixed"],
            registry => registry.RegisterMerge<string>(),
            services => services.AddExternalFluxFlowResource<TimeProvider>(
                ApplicationAddress.Resource("fixed"),
                new FakeTimeProvider(timestamp)));
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        var outputResult = ports.ReceiveAsync<string>(Port(RoutingCompositionPortNames.Output), Timeout);
        var eventResult = ports.ReceiveAsync<CompositionComponentEvent>(Port(CompositionComponentEvents.PortName), Timeout);
        var message = FlowMessage.Create("value", new CorrelationId("merge"));

        (await ports.SendAsync(Port(RoutingCompositionPortNames.Input), message))
            .IsAccepted.ShouldBeTrue();

        (await outputResult).Message.ShouldNotBeNull().CorrelationId.ShouldBe(message.CorrelationId);
        (await eventResult).Message.ShouldNotBeNull().Payload.Timestamp.ShouldBe(timestamp);
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
            registry => registry.RegisterWindow<int>(),
            services => services.AddExternalFluxFlowResource<TimeProvider>(
                ApplicationAddress.Resource("fixed"),
                new FakeTimeProvider(timestamp)));
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        var outputResult = ports.ReceiveAsync<FlowWindow<int>>(Port(RoutingCompositionPortNames.Output), Timeout);
        var first = FlowMessage.Create(10, new CorrelationId("window"));

        (await ports.SendAsync(Port(RoutingCompositionPortNames.Input), first))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(Port(RoutingCompositionPortNames.Input), FlowMessage.Create(20)))
            .IsAccepted.ShouldBeTrue();

        var window = (await outputResult).Message.ShouldNotBeNull();

        window.CorrelationId.ShouldBe(first.CorrelationId);
        window.Payload.Items.ShouldBe([10, 20]);
        window.Payload.StartedAt.ShouldBe(timestamp);
        window.Payload.EmittedAt.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Hosted_correlation_resolves_selectors_and_routes_matches()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T13:00:00Z");
        await using var host = await StartNodeAsync(
            RoutingCompositionNodeTypes.Correlation,
            Properties(
                (RoutingCompositionResourceNames.KeySelector, "Resources.key"),
                (RoutingCompositionResourceNames.SideSelector, "Resources.side"),
                (RoutingCompositionResourceNames.Clock, "Resources.fixed"),
                ("requestSide", "request"),
                ("responseSide", "response"),
                ("boundedCapacity", 8)),
            ["key", "side", "fixed"],
            registry => registry.RegisterCorrelation<InputMessage>(),
            services =>
            {
                services.AddExternalFluxFlowResource<Func<InputMessage, string?>>(
                    ApplicationAddress.Resource("key"),
                    input => input.Id);
                services.AddExternalFluxFlowResource<Func<InputMessage, string?>>(
                    ApplicationAddress.Resource("side"),
                    input => input.Route);
                services.AddExternalFluxFlowResource<TimeProvider>(
                    ApplicationAddress.Resource("fixed"),
                    new FakeTimeProvider(timestamp));
            });
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        var outputResult = ports.ReceiveAsync<FlowCorrelationMatch<InputMessage>>(Port(RoutingCompositionPortNames.Output), Timeout);
        var matchedResult = ports.ReceiveAsync<FlowCorrelationMatch<InputMessage>>(Port(RoutingCompositionPortNames.Matched), Timeout);
        var timeoutObservation = (await ports.ObserveAsync<FlowCorrelationTimeout<InputMessage>>(Port(RoutingCompositionPortNames.Timeouts)))
            .Observation.ShouldNotBeNull();
        await using var timeoutScope = timeoutObservation;
        var request = FlowMessage.Create(
            new InputMessage("request", "A-300"),
            new CorrelationId("request"));

        (await ports.SendAsync(Port(RoutingCompositionPortNames.Input), request))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(Port(RoutingCompositionPortNames.Input), FlowMessage.Create(
                new InputMessage("response", "A-300"),
                new CorrelationId("response"))))
            .IsAccepted.ShouldBeTrue();

        var result = (await outputResult).Message.ShouldNotBeNull();
        var aliasResult = (await matchedResult).Message.ShouldNotBeNull();

        result.CorrelationId.ShouldBe(request.CorrelationId);
        result.Payload.Key.ShouldBe("A-300");
        result.Payload.Request.Route.ShouldBe("request");
        result.Payload.Response.Route.ShouldBe("response");
        result.Payload.MatchedAt.ShouldBe(timestamp);
        aliasResult.Payload.Key.ShouldBe("A-300");
        timeoutObservation.Messages
            .ShouldBeAssignableTo<IReceivableSourceBlock<FlowMessage<FlowCorrelationTimeout<InputMessage>>>>()!
            .TryReceive(out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Hosted_canonical_correlation_resolves_flow_value_selectors()
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
                services.AddExternalFluxFlowResource<Func<FlowValue, string?>>(
                    ApplicationAddress.Resource("key"),
                    value => value.GetObject()["key"].GetString());
                services.AddExternalFluxFlowResource<Func<FlowValue, string?>>(
                    ApplicationAddress.Resource("side"),
                    value => value.GetObject()["side"].GetString());
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
        var outputResult = ports.ReceiveAsync<FlowResult<FlowCorrelationOutcome<FlowValue>>>(
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
        result.Payload.Kind.ShouldBe(RoutingResultKinds.Matched);
        result.Payload.Value
            .ShouldBeOfType<FlowCorrelationMatchedOutcome<FlowValue>>()
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
            registry => registry.RegisterJoin<LeftMessage, RightMessage>(),
            services =>
            {
                services.AddExternalFluxFlowResource<Func<LeftMessage, string?>>(
                    ApplicationAddress.Resource("left"),
                    input => input.Key);
                services.AddExternalFluxFlowResource<Func<RightMessage, string?>>(
                    ApplicationAddress.Resource("right"),
                    input => input.Key);
                services.AddExternalFluxFlowResource<TimeProvider>(
                    ApplicationAddress.Resource("fixed"),
                    new FakeTimeProvider(timestamp));
            });
        host.StartResult.Succeeded.ShouldBeTrue();

        var ports = host.GetRequiredPorts();
        var outputResult = ports.ReceiveAsync<FlowJoinResult<LeftMessage, RightMessage>>(
            Port(RoutingCompositionPortNames.Output),
            Timeout);
        var timeoutObservation = (await ports.ObserveAsync<FlowJoinTimeout<LeftMessage, RightMessage>>(
                Port(RoutingCompositionPortNames.Timeouts)))
            .Observation.ShouldNotBeNull();
        await using var timeoutScope = timeoutObservation;
        var leftMessage = FlowMessage.Create(
            new LeftMessage("A-400", "left"),
            new CorrelationId("left"));

        (await ports.SendAsync(Port(RoutingCompositionPortNames.Left), leftMessage))
            .IsAccepted.ShouldBeTrue();
        (await ports.SendAsync(Port(RoutingCompositionPortNames.Right), FlowMessage.Create(
                new RightMessage("A-400", "right"),
                new CorrelationId("right"))))
            .IsAccepted.ShouldBeTrue();

        var result = (await outputResult).Message.ShouldNotBeNull();

        result.CorrelationId.ShouldBe(leftMessage.CorrelationId);
        result.Payload.Key.ShouldBe("A-400");
        result.Payload.Left.Payload.ShouldBe("left");
        result.Payload.Right.Payload.ShouldBe("right");
        result.Payload.JoinedAt.ShouldBe(timestamp);
        timeoutObservation.Messages
            .ShouldBeAssignableTo<IReceivableSourceBlock<FlowMessage<FlowJoinTimeout<LeftMessage, RightMessage>>>>()!
            .TryReceive(out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Missing_required_selector_surfaces_factory_diagnostic()
    {
        await using var host = await StartNodeAsync(
            RoutingCompositionNodeTypes.Switch,
            Properties(("boundedCapacity", 8)),
            null,
            registry => registry.RegisterSwitch<InputMessage>());

        AssertPreparationFailure(host, RoutingCompositionResourceNames.RouteKeySelector);
    }

    [Fact]
    public async Task Invalid_dynamic_output_surfaces_factory_diagnostic()
    {
        await using var host = await StartNodeAsync(
            RoutingCompositionNodeTypes.Fork,
            Properties(("outputs", new[] { "Output" })),
            null,
            registry => registry.RegisterFork<InputMessage>());

        AssertPreparationFailure(host, "built-in");
    }

    [Fact]
    public async Task Invalid_routing_options_surface_factory_diagnostic()
    {
        await AssertFactoryDiagnosticAsync(
            RoutingCompositionNodeTypes.Switch,
            Properties(
                (RoutingCompositionResourceNames.RouteKeySelector, "Resources.route"),
                ("boundedCapacity", 0)),
            ["route"],
            services => services.AddExternalFluxFlowResource<Func<InputMessage, string?>>(
                ApplicationAddress.Resource("route"),
                input => input.Route),
            registry => registry.RegisterSwitch<InputMessage>(),
            "BoundedCapacity");

        await AssertFactoryDiagnosticAsync(
            RoutingCompositionNodeTypes.Fork,
            Properties(
                ("outputs", new[] { "Audit" }),
                ("inputType", " ")),
            null,
            null,
            registry => registry.RegisterFork<InputMessage>(),
            "InputType");

        await AssertFactoryDiagnosticAsync(
            RoutingCompositionNodeTypes.Merge,
            Properties(("boundedCapacity", 0)),
            null,
            null,
            registry => registry.RegisterMerge<InputMessage>(),
            "BoundedCapacity");

        await AssertFactoryDiagnosticAsync(
            RoutingCompositionNodeTypes.Window,
            Properties(("maxItems", -1)),
            null,
            null,
            registry => registry.RegisterWindow<InputMessage>(),
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
                services.AddExternalFluxFlowResource<Func<InputMessage, string?>>(
                    ApplicationAddress.Resource("key"),
                    input => input.Id);
                services.AddExternalFluxFlowResource<Func<InputMessage, string?>>(
                    ApplicationAddress.Resource("side"),
                    input => input.Route);
            },
            registry => registry.RegisterCorrelation<InputMessage>(),
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
            failure.Error.Details.GetObject()["exceptionMessage"].GetString().Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private static async ValueTask<ComposedNode> CreateNodeAsync(
        IServiceProvider services,
        string componentType,
        IReadOnlyDictionary<string, object?> properties,
        Action<CompositionNodeRegistry> registerNodes)
    {
        var registry = new CompositionNodeRegistry();
        registerNodes(registry);
        var component = SingleComponent(componentType, properties)
            .Workflows["main"]
            .Components["node"];
        return await registry.Registrations[componentType].Factory(
            new CompositionNodeFactoryContext(
                services,
                "main",
                "node",
                component));
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static FlowValue RoutingItem(string key, string side, string value)
        => FlowValue.FromObject(new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["key"] = FlowValue.From(key),
            ["side"] = FlowValue.From(side),
            ["value"] = FlowValue.From(value)
        });

    private sealed record InputMessage(string Route, string Id);

    private sealed record LeftMessage(string Key, string Payload);

    private sealed record RightMessage(string Key, string Payload);
}
