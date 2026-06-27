using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Control.Composition;
using FluxFlow.Components.Control.Diagnostics;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Control.Composition.Tests;

public sealed class ControlCompositionNodeRegistryExtensionsTests
{
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
            item.Category.ShouldBe("Control");
            item.SuggestedEditorWidth.ShouldBe(420);
            item.Options.Select(option => (option.Name, option.Kind)).ShouldBe([
                ("expression", OptionValueKind.Expression),
                ("expressionId", OptionValueKind.Text),
                ("expressionName", OptionValueKind.Text),
                ("engine", OptionValueKind.Text),
                ("inputType", OptionValueKind.Text),
                ("boundedCapacity", OptionValueKind.Number)
            ]);
            item.Options.Single(option => option.Name == "expression")
                .IsRequired.ShouldBeTrue();
            item.Options.Single(option => option.Name == "boundedCapacity")
                .Min.ShouldBe(1);
            item.Options.Select(option => option.Name)
                .ShouldNotContain(ControlCompositionResourceNames.ContextFactory);
            item.Options.Select(option => option.Name)
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
            port.ValueType)).ShouldBe([
            (ControlCompositionPortNames.Input, PortDirection.Input, 0, true, "TInput"),
            (ControlCompositionPortNames.Output, PortDirection.Output, 1, true, "TInput")
        ]);
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
            port.ValueType)).ShouldBe([
            (ControlCompositionPortNames.Input, PortDirection.Input, 0, true, "TInput"),
            (ControlCompositionPortNames.Output, PortDirection.Output, 1, true, "TInput"),
            (ControlCompositionPortNames.WhenTrue, PortDirection.Output, 2, false, "TInput"),
            (ControlCompositionPortNames.WhenFalse, PortDirection.Output, 3, false, "TInput")
        ]);
        metadata.Ports.Single(port => port.Name.Value == ControlCompositionPortNames.Output)
            .Attributes["aliasOf"].ShouldBe(ControlCompositionPortNames.WhenTrue);
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
            resource.ValueType.ShouldBe(expected[index].ValueType);
        }
    }

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
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>("primary", engine);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "filter",
                    ControlCompositionNodeTypes.Filter,
                    node => node
                        .Resource(ControlCompositionResourceNames.Engine, "primary")
                        .Configure("expression", "input.Value >= 10")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterFilter<InputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var runtime = host.Runtime.ShouldNotBeNull();
        var filterNode = runtime.Nodes.ShouldHaveSingleItem();
        var input = filterNode.Descriptor.Inputs[ControlCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var output = filterNode.Descriptor.Outputs[ControlCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var results = new BufferBlock<FlowMessage<InputMessage>>();
        output.Source.LinkTo(
            results,
            new DataflowLinkOptions { PropagateCompletion = true });

        var rejected = FlowMessage.Create(
            new InputMessage(3),
            new CorrelationId("filter-rejected"));
        var accepted = FlowMessage.Create(
            new InputMessage(12),
            new CorrelationId("filter-accepted"));

        (await input.Target.SendAsync(rejected)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        (await input.Target.SendAsync(accepted)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        input.Target.Complete();

        var response = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        response.CorrelationId.ShouldBe(new CorrelationId("filter-accepted"));
        response.Payload.Value.ShouldBe(12);
        results.TryReceive(out _).ShouldBeFalse();
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
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>("primary", engine);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "when",
                    ControlCompositionNodeTypes.When,
                    node => node
                        .Resource(ControlCompositionResourceNames.Engine, "primary")
                        .Configure("expression", "input.Value >= 10")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterWhen<InputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var whenNode = host.Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem();
        var input = whenNode.Descriptor.Inputs[ControlCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var whenTrue = whenNode.Descriptor.Outputs[ControlCompositionPortNames.WhenTrue]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var whenFalse = whenNode.Descriptor.Outputs[ControlCompositionPortNames.WhenFalse]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var output = whenNode.Descriptor.Outputs[ControlCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var trueResults = new BufferBlock<FlowMessage<InputMessage>>();
        var falseResults = new BufferBlock<FlowMessage<InputMessage>>();
        var outputResults = new BufferBlock<FlowMessage<InputMessage>>();
        whenTrue.Source.LinkTo(trueResults);
        whenFalse.Source.LinkTo(falseResults);
        output.Source.LinkTo(outputResults);

        var rejected = FlowMessage.Create(
            new InputMessage(3),
            new CorrelationId("when-false"));
        var accepted = FlowMessage.Create(
            new InputMessage(12),
            new CorrelationId("when-true"));

        (await input.Target.SendAsync(rejected)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        (await input.Target.SendAsync(accepted)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var falseResponse = await falseResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        var trueResponse = await trueResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        var outputResponse = await outputResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

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
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>("primary", engine);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "filter",
                    ControlCompositionNodeTypes.Filter,
                    node => node
                        .Resource(ControlCompositionResourceNames.Engine, "primary")
                        .Configure("expression", "pass")
                        .Configure("expressionName", "configured-filter")
                        .Configure("inputType", "app.input")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterFilter<object>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var filterNode = host.Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem();
        var input = filterNode.Descriptor.Inputs[ControlCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<object>>();
        var events = filterNode.Descriptor.Events.ShouldNotBeNull();
        var eventSink = new BufferBlock<FlowEvent>();
        events.LinkTo(eventSink);

        (await input.Target.SendAsync(FlowMessage.Create<object>("value"))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var @event = await eventSink.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        @event.Attributes["inputType"].ShouldBe("app.input");
        @event.Attributes["expressionName"].ShouldBe("configured-filter");
    }

    [Fact]
    public async Task Hosted_when_uses_optional_keyed_context_factory()
    {
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, _) => context.Variables["matches"]);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>("primary", engine);
        services.AddKeyedSingleton<IFlowMapContextFactory<InputMessage>>(
            "custom",
            new CustomContextFactory(matches: true));
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "when",
                    ControlCompositionNodeTypes.When,
                    node => node
                        .Resource(ControlCompositionResourceNames.Engine, "primary")
                        .Resource(ControlCompositionResourceNames.ContextFactory, "custom")
                        .Configure("expression", "matches")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterWhen<InputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var whenNode = host.Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem();
        var input = whenNode.Descriptor.Inputs[ControlCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var whenTrue = whenNode.Descriptor.Outputs[ControlCompositionPortNames.WhenTrue]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var results = new BufferBlock<FlowMessage<InputMessage>>();
        whenTrue.Source.LinkTo(results);

        var message = FlowMessage.Create(
            new InputMessage(1),
            new CorrelationId("custom-context"));

        (await input.Target.SendAsync(message)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var result = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        result.CorrelationId.ShouldBe(new CorrelationId("custom-context"));
        result.Payload.Value.ShouldBe(1);
    }

    [Fact]
    public async Task Hosted_filter_uses_optional_keyed_clock_for_diagnostics()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T13:00:00Z");
        var engine = new RecordingExpressionEngine(evaluate: (_, _, _) => true);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>("primary", engine);
        services.AddKeyedSingleton<TimeProvider>(
            "fixed",
            new FakeTimeProvider(timestamp));
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "filter",
                    ControlCompositionNodeTypes.Filter,
                    node => node
                        .Resource(ControlCompositionResourceNames.Engine, "primary")
                        .Resource(ControlCompositionResourceNames.Clock, "fixed")
                        .Configure("expression", "pass")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterFilter<InputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var filterNode = host.Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem();
        var input = filterNode.Descriptor.Inputs[ControlCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var events = filterNode.Descriptor.Events.ShouldNotBeNull();
        var eventSink = new BufferBlock<FlowEvent>();
        events.LinkTo(eventSink);

        (await input.Target.SendAsync(FlowMessage.Create(new InputMessage(1)))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var @event = await eventSink.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        @event.Timestamp.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Missing_engine_resource_reference_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "filter",
                    ControlCompositionNodeTypes.Filter,
                    node => node.Configure("expression", "pass")))
                .Build())
            .RegisterNodes(registry => registry.RegisterFilter<object>())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(
                ControlCompositionResourceNames.Engine,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_filter_options_surface_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>(
            "primary",
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "filter",
                    ControlCompositionNodeTypes.Filter,
                    node => node
                        .Resource(ControlCompositionResourceNames.Engine, "primary")
                        .Configure("expression", "pass")
                        .Configure("boundedCapacity", 0)))
                .Build())
            .RegisterNodes(registry => registry.RegisterFilter<object>())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains("boundedCapacity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_filter_expression_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>(
            "primary",
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "filter",
                    ControlCompositionNodeTypes.Filter,
                    node => node.Resource(ControlCompositionResourceNames.Engine, "primary")))
                .Build())
            .RegisterNodes(registry => registry.RegisterFilter<object>())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains("expression", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_when_options_surface_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>(
            "primary",
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "when",
                    ControlCompositionNodeTypes.When,
                    node => node
                        .Resource(ControlCompositionResourceNames.Engine, "primary")
                        .Configure("expression", "route")
                        .Configure("inputType", " ")))
                .Build())
            .RegisterNodes(registry => registry.RegisterWhen<object>())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains("inputType", StringComparison.Ordinal));
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
