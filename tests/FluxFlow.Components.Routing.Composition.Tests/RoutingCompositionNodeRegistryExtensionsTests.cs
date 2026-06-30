using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Routing.Composition;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Routing.Composition.Tests;

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
        flowSwitch.Outputs.ShouldBeEmpty();

        var fork = registry.Registrations[RoutingCompositionNodeTypes.Fork];
        fork.Inputs[RoutingCompositionPortNames.Input].MessageType.ShouldBe(
            typeof(InputMessage));
        fork.Outputs.ShouldBeEmpty();

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
                (RoutingCompositionResourceNames.KeySelector, 0, true, "Func<TInput,string?>"),
                (RoutingCompositionResourceNames.SideSelector, 1, true, "Func<TInput,string?>"),
                (RoutingCompositionResourceNames.Clock, 2, false, nameof(TimeProvider))
            ]);
        AssertResources(
            byType[RoutingCompositionNodeTypes.Join],
            [
                (RoutingCompositionResourceNames.LeftKeySelector, 0, true, "Func<TLeft,string?>"),
                (RoutingCompositionResourceNames.RightKeySelector, 1, true, "Func<TRight,string?>"),
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

        AssertPorts(
            metadata[RoutingCompositionNodeTypes.Fork],
            [
                (RoutingCompositionPortNames.Input, PortDirection.Input, 0, true, "TInput"),
                (RoutingCompositionPortNames.Output, PortDirection.Output, 1, true, "TInput")
            ]);
        metadata[RoutingCompositionNodeTypes.Fork].Attributes[new ComponentAttributeName("dynamicOutputsOption")]
            .Value.ShouldBe("outputs");

        AssertPorts(
            metadata[RoutingCompositionNodeTypes.Merge],
            [
                (RoutingCompositionPortNames.Input, PortDirection.Input, 0, true, "TInput"),
                (RoutingCompositionPortNames.Output, PortDirection.Output, 1, true, "TInput")
            ]);
        AssertPorts(
            metadata[RoutingCompositionNodeTypes.Window],
            [
                (RoutingCompositionPortNames.Input, PortDirection.Input, 0, true, "TInput"),
                (RoutingCompositionPortNames.Output, PortDirection.Output, 1, true, "FlowWindow<TInput>")
            ]);
        AssertPorts(
            metadata[RoutingCompositionNodeTypes.Correlation],
            [
                (RoutingCompositionPortNames.Input, PortDirection.Input, 0, true, "TInput"),
                (RoutingCompositionPortNames.Output, PortDirection.Output, 1, true, "FlowCorrelationMatch<TInput>"),
                (RoutingCompositionPortNames.Matched, PortDirection.Output, 2, false, "FlowCorrelationMatch<TInput>"),
                (RoutingCompositionPortNames.Timeouts, PortDirection.Output, 3, false, "FlowCorrelationTimeout<TInput>")
            ]);
        AssertPorts(
            metadata[RoutingCompositionNodeTypes.Join],
            [
                (RoutingCompositionPortNames.Left, PortDirection.Input, 0, true, "TLeft"),
                (RoutingCompositionPortNames.Right, PortDirection.Input, 1, false, "TRight"),
                (RoutingCompositionPortNames.Output, PortDirection.Output, 2, true, "FlowJoinResult<TLeft,TRight>"),
                (RoutingCompositionPortNames.Timeouts, PortDirection.Output, 3, false, "FlowJoinTimeout<TLeft,TRight>")
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
    public async Task Hosted_switch_resolves_selector_and_exposes_configured_ports()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<Func<InputMessage, string?>>(
            "route",
            input => input.Route);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "switch",
                    RoutingCompositionNodeTypes.Switch,
                    node => node
                        .Resource(RoutingCompositionResourceNames.RouteKeySelector, "route")
                        .Configure("routes", new[] { "priority", "standard" })
                        .Configure(
                            "routeOutputs",
                            new Dictionary<string, string>
                            {
                                ["priority"] = "Priority"
                            })
                        .Configure("emitRouteEnvelope", true)
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterSwitch<InputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var node = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem();
        var input = node.Descriptor.Inputs[RoutingCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var output = node.Descriptor.Outputs[RoutingCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var matched = node.Descriptor.Outputs[RoutingCompositionPortNames.Matched]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var defaults = node.Descriptor.Outputs[RoutingCompositionPortNames.Default]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var routed = node.Descriptor.Outputs[RoutingCompositionPortNames.Routed]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var priority = node.Descriptor.Outputs["Priority"]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var outputResults = Link(output.Source);
        var matchedResults = Link(matched.Source);
        var defaultResults = Link(defaults.Source);
        var routedResults = Link(routed.Source);
        var priorityResults = Link(priority.Source);

        var first = FlowMessage.Create(
            new InputMessage("priority", "A-100"),
            new CorrelationId("matched"));
        var second = FlowMessage.Create(
            new InputMessage("unknown", "A-101"),
            new CorrelationId("default"));

        (await input.Target.SendAsync(first)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        (await input.Target.SendAsync(second)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        (await outputResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5))).CorrelationId.ShouldBe(first.CorrelationId);
        (await matchedResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5))).CorrelationId.ShouldBe(first.CorrelationId);
        (await priorityResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5))).Payload.Id.ShouldBe("A-100");
        (await defaultResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5))).CorrelationId.ShouldBe(second.CorrelationId);
        var routedMessages = new[]
        {
            await routedResults.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)),
            await routedResults.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))
        };
        routedMessages.Select(message => message.CorrelationId).ShouldBe(
            [first.CorrelationId, second.CorrelationId],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Hosted_fork_emits_to_configured_ports_and_output_alias()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "fork",
                    RoutingCompositionNodeTypes.Fork,
                    node => node
                        .Configure("outputs", new[] { "Audit", "Work" })
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterFork<InputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var node = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem();
        var input = node.Descriptor.Inputs[RoutingCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var output = node.Descriptor.Outputs[RoutingCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var audit = node.Descriptor.Outputs["Audit"]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var work = node.Descriptor.Outputs["Work"]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var outputResults = Link(output.Source);
        var auditResults = Link(audit.Source);
        var workResults = Link(work.Source);

        var message = FlowMessage.Create(
            new InputMessage("work", "A-200"),
            new CorrelationId("forked"));

        (await input.Target.SendAsync(message)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        (await outputResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5))).CorrelationId.ShouldBe(message.CorrelationId);
        (await auditResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5))).Payload.Id.ShouldBe("A-200");
        (await workResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5))).CorrelationId.ShouldBe(message.CorrelationId);
    }

    [Fact]
    public async Task Hosted_merge_forwards_inputs_and_uses_keyed_clock_for_events()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T12:00:00Z");
        var services = new ServiceCollection();
        services.AddKeyedSingleton<TimeProvider>(
            "fixed",
            new FakeTimeProvider(timestamp));
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "merge",
                    RoutingCompositionNodeTypes.Merge,
                    node => node
                        .Resource(RoutingCompositionResourceNames.Clock, "fixed")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterMerge<string>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var node = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem();
        var input = node.Descriptor.Inputs[RoutingCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<string>>();
        var output = node.Descriptor.Outputs[RoutingCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<string>>();
        var results = Link(output.Source);
        var events = Link(node.Descriptor.Events.ShouldNotBeNull());
        var message = FlowMessage.Create("value", new CorrelationId("merge"));

        (await input.Target.SendAsync(message)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        (await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5))).CorrelationId.ShouldBe(message.CorrelationId);
        (await events.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5))).Timestamp.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Hosted_window_binds_options_and_uses_keyed_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T12:30:00Z");
        var services = new ServiceCollection();
        services.AddKeyedSingleton<TimeProvider>(
            "fixed",
            new FakeTimeProvider(timestamp));
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "window",
                    RoutingCompositionNodeTypes.Window,
                    node => node
                        .Resource(RoutingCompositionResourceNames.Clock, "fixed")
                        .Configure("maxItems", 2)
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterWindow<int>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var node = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem();
        var input = node.Descriptor.Inputs[RoutingCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<int>>();
        var output = node.Descriptor.Outputs[RoutingCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<FlowWindow<int>>>();
        var results = Link(output.Source);
        var first = FlowMessage.Create(10, new CorrelationId("window"));

        (await input.Target.SendAsync(first)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        (await input.Target.SendAsync(FlowMessage.Create(20))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var window = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        window.CorrelationId.ShouldBe(first.CorrelationId);
        window.Payload.Items.ShouldBe([10, 20]);
        window.Payload.StartedAt.ShouldBe(timestamp);
        window.Payload.EmittedAt.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Hosted_correlation_resolves_selectors_and_routes_matches()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T13:00:00Z");
        var services = new ServiceCollection();
        services.AddKeyedSingleton<Func<InputMessage, string?>>(
            "key",
            input => input.Id);
        services.AddKeyedSingleton<Func<InputMessage, string?>>(
            "side",
            input => input.Route);
        services.AddKeyedSingleton<TimeProvider>(
            "fixed",
            new FakeTimeProvider(timestamp));
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "correlate",
                    RoutingCompositionNodeTypes.Correlation,
                    node => node
                        .Resource(RoutingCompositionResourceNames.KeySelector, "key")
                        .Resource(RoutingCompositionResourceNames.SideSelector, "side")
                        .Resource(RoutingCompositionResourceNames.Clock, "fixed")
                        .Configure("requestSide", "request")
                        .Configure("responseSide", "response")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterCorrelation<InputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var node = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem();
        var input = node.Descriptor.Inputs[RoutingCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var output = node.Descriptor.Outputs[RoutingCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<FlowCorrelationMatch<InputMessage>>>();
        var matched = node.Descriptor.Outputs[RoutingCompositionPortNames.Matched]
            .ShouldBeOfType<CompositionOutputPort<FlowCorrelationMatch<InputMessage>>>();
        var timeouts = node.Descriptor.Outputs[RoutingCompositionPortNames.Timeouts]
            .ShouldBeOfType<CompositionOutputPort<FlowCorrelationTimeout<InputMessage>>>();
        var outputResults = Link(output.Source);
        var matchedResults = Link(matched.Source);
        var timeoutResults = Link(timeouts.Source);
        var request = FlowMessage.Create(
            new InputMessage("request", "A-300"),
            new CorrelationId("request"));

        (await input.Target.SendAsync(request)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        (await input.Target.SendAsync(FlowMessage.Create(
                new InputMessage("response", "A-300"),
                new CorrelationId("response")))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var result = await outputResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        var aliasResult = await matchedResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        result.CorrelationId.ShouldBe(request.CorrelationId);
        result.Payload.Key.ShouldBe("A-300");
        result.Payload.Request.Route.ShouldBe("request");
        result.Payload.Response.Route.ShouldBe("response");
        result.Payload.MatchedAt.ShouldBe(timestamp);
        aliasResult.Payload.Key.ShouldBe("A-300");
        timeoutResults.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Hosted_join_resolves_selectors_and_routes_matches()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T13:30:00Z");
        var services = new ServiceCollection();
        services.AddKeyedSingleton<Func<LeftMessage, string?>>(
            "left",
            input => input.Key);
        services.AddKeyedSingleton<Func<RightMessage, string?>>(
            "right",
            input => input.Key);
        services.AddKeyedSingleton<TimeProvider>(
            "fixed",
            new FakeTimeProvider(timestamp));
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "join",
                    RoutingCompositionNodeTypes.Join,
                    node => node
                        .Resource(RoutingCompositionResourceNames.LeftKeySelector, "left")
                        .Resource(RoutingCompositionResourceNames.RightKeySelector, "right")
                        .Resource(RoutingCompositionResourceNames.Clock, "fixed")
                        .Configure("boundedCapacity", 8)
                        .Configure("timeoutMilliseconds", 5000)))
                .Build())
            .RegisterNodes(registry => registry.RegisterJoin<LeftMessage, RightMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var node = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem();
        var left = node.Descriptor.Inputs[RoutingCompositionPortNames.Left]
            .ShouldBeOfType<CompositionInputPort<LeftMessage>>();
        var right = node.Descriptor.Inputs[RoutingCompositionPortNames.Right]
            .ShouldBeOfType<CompositionInputPort<RightMessage>>();
        var output = node.Descriptor.Outputs[RoutingCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<FlowJoinResult<LeftMessage, RightMessage>>>();
        var timeouts = node.Descriptor.Outputs[RoutingCompositionPortNames.Timeouts]
            .ShouldBeOfType<CompositionOutputPort<FlowJoinTimeout<LeftMessage, RightMessage>>>();
        var results = Link(output.Source);
        var timeoutResults = Link(timeouts.Source);
        var leftMessage = FlowMessage.Create(
            new LeftMessage("A-400", "left"),
            new CorrelationId("left"));

        (await left.Target.SendAsync(leftMessage)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        (await right.Target.SendAsync(FlowMessage.Create(
                new RightMessage("A-400", "right"),
                new CorrelationId("right")))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var result = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        result.CorrelationId.ShouldBe(leftMessage.CorrelationId);
        result.Payload.Key.ShouldBe("A-400");
        result.Payload.Left.Payload.ShouldBe("left");
        result.Payload.Right.Payload.ShouldBe("right");
        result.Payload.JoinedAt.ShouldBe(timestamp);
        timeoutResults.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Missing_required_selector_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "switch",
                    RoutingCompositionNodeTypes.Switch,
                    node => node.Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterSwitch<InputMessage>())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(
                RoutingCompositionResourceNames.RouteKeySelector,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_dynamic_output_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "fork",
                    RoutingCompositionNodeTypes.Fork,
                    node => node.Configure("outputs", new[] { "Output" })))
                .Build())
            .RegisterNodes(registry => registry.RegisterFork<InputMessage>())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains("built-in", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Invalid_routing_options_surface_factory_diagnostic()
    {
        await AssertFactoryDiagnosticAsync(
            CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "switch",
                    RoutingCompositionNodeTypes.Switch,
                    node => node
                        .Resource(RoutingCompositionResourceNames.RouteKeySelector, "route")
                        .Configure("boundedCapacity", 0)))
                .Build(),
            services => services.AddKeyedSingleton<Func<InputMessage, string?>>(
                "route",
                input => input.Route),
            registry => registry.RegisterSwitch<InputMessage>(),
            "boundedCapacity");

        await AssertFactoryDiagnosticAsync(
            CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "fork",
                    RoutingCompositionNodeTypes.Fork,
                    node => node
                        .Configure("outputs", new[] { "Audit" })
                        .Configure("inputType", " ")))
                .Build(),
            null,
            registry => registry.RegisterFork<InputMessage>(),
            "inputType");

        await AssertFactoryDiagnosticAsync(
            CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "merge",
                    RoutingCompositionNodeTypes.Merge,
                    node => node.Configure("boundedCapacity", 0)))
                .Build(),
            null,
            registry => registry.RegisterMerge<InputMessage>(),
            "boundedCapacity");

        await AssertFactoryDiagnosticAsync(
            CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "window",
                    RoutingCompositionNodeTypes.Window,
                    node => node.Configure("maxItems", -1)))
                .Build(),
            null,
            registry => registry.RegisterWindow<InputMessage>(),
            "maxItems");

        await AssertFactoryDiagnosticAsync(
            CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "correlate",
                    RoutingCompositionNodeTypes.Correlation,
                    node => node
                        .Resource(RoutingCompositionResourceNames.KeySelector, "key")
                        .Resource(RoutingCompositionResourceNames.SideSelector, "side")
                        .Configure("timeoutMilliseconds", 0)))
                .Build(),
            services =>
            {
                services.AddKeyedSingleton<Func<InputMessage, string?>>(
                    "key",
                    input => input.Id);
                services.AddKeyedSingleton<Func<InputMessage, string?>>(
                    "side",
                    input => input.Route);
            },
            registry => registry.RegisterCorrelation<InputMessage>(),
            "timeoutMilliseconds");
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

    private static async Task BuildCompositionAsync(IServiceProvider provider)
    {
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();
        await hostedService.StartAsync(CancellationToken.None);
    }

    private static async Task AssertFactoryDiagnosticAsync(
        CompositionDefinition definition,
        Action<IServiceCollection>? configureServices,
        Action<CompositionNodeRegistry> registerNodes,
        string expectedMessage)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        services
            .AddFluxFlowComposition(definition)
            .RegisterNodes(registerNodes)
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase));
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }

    private sealed record InputMessage(string Route, string Id);

    private sealed record LeftMessage(string Key, string Payload);

    private sealed record RightMessage(string Key, string Payload);
}
