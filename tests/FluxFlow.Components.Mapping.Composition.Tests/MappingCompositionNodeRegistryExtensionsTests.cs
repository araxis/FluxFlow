using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mapping.Composition;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mapping.Composition.Tests;

public sealed class MappingCompositionNodeRegistryExtensionsTests
{
    [Fact]
    public void RegisterMapper_registers_closed_mapper_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterMapper<InputMessage, OutputMessage>();

        var mapper = registry.Registrations[MappingCompositionNodeTypes.Mapper];
        mapper.Inputs[MappingCompositionPortNames.Input].MessageType.ShouldBe(
            typeof(InputMessage));
        mapper.Outputs[MappingCompositionPortNames.Output].MessageType.ShouldBe(
            typeof(OutputMessage));
        mapper.Outputs[MappingCompositionPortNames.Failed].MessageType.ShouldBe(
            typeof(InputMessage));
    }

    [Fact]
    public void RegisterMapper_supports_multiple_custom_node_types()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterMapper<InputMessage, OutputMessage>("flow.mapper.input-output")
            .RegisterMapper<string, int>("flow.mapper.string-int");

        registry.Registrations["flow.mapper.input-output"]
            .Outputs[MappingCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(OutputMessage));
        registry.Registrations["flow.mapper.string-int"]
            .Outputs[MappingCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(int));
    }

    [Fact]
    public async Task Hosted_mapper_resolves_keyed_engine_and_maps_message()
    {
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, resultType) =>
            {
                resultType.ShouldBe(typeof(OutputMessage));
                var input = (InputMessage)context.Variables["input"]!;
                return new OutputMessage($"{input.Value}-mapped");
            });
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>("primary", engine);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "map",
                    MappingCompositionNodeTypes.Mapper,
                    node => node
                        .Resource(MappingCompositionResourceNames.Engine, "primary")
                        .Configure("expression", "map")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterMapper<InputMessage, OutputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var runtime = host.Runtime.ShouldNotBeNull();
        var mapperNode = runtime.Nodes.ShouldHaveSingleItem();
        var input = mapperNode.Descriptor.Inputs[MappingCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var output = mapperNode.Descriptor.Outputs[MappingCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<OutputMessage>>();
        var results = new BufferBlock<FlowMessage<OutputMessage>>();
        output.Source.LinkTo(
            results,
            new DataflowLinkOptions { PropagateCompletion = true });

        var request = FlowMessage.Create(
            new InputMessage("value"),
            new CorrelationId("map-correlation"));

        (await input.Target.SendAsync(request)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        input.Target.Complete();

        var response = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        response.CorrelationId.ShouldBe(new CorrelationId("map-correlation"));
        response.Payload.Value.ShouldBe("value-mapped");
    }

    [Fact]
    public async Task Hosted_mapper_binds_options_from_configuration()
    {
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, _) => new OutputMessage($"{context.Variables["input"]}-mapped"));
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>("primary", engine);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "map",
                    MappingCompositionNodeTypes.Mapper,
                    node => node
                        .Resource(MappingCompositionResourceNames.Engine, "primary")
                        .Configure("expression", "map")
                        .Configure("expressionName", "test-map")
                        .Configure("inputType", "app.input")
                        .Configure("outputType", "app.output")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterMapper<object, OutputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var mapperNode = host.Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem();
        var input = mapperNode.Descriptor.Inputs[MappingCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<object>>();
        var events = mapperNode.Descriptor.Events.ShouldNotBeNull();
        var eventSink = new BufferBlock<FlowEvent>();
        events.LinkTo(eventSink);

        (await input.Target.SendAsync(FlowMessage.Create<object>("value"))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var @event = await eventSink.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        @event.Attributes["inputType"].ShouldBe("app.input");
        @event.Attributes["outputType"].ShouldBe("app.output");
        @event.Attributes["expressionName"].ShouldBe("test-map");
    }

    [Fact]
    public async Task Hosted_mapper_uses_optional_keyed_context_factory()
    {
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, _) => new OutputMessage((string)context.Variables["mapped"]!));
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>("primary", engine);
        services.AddKeyedSingleton<IMappingContextFactory>(
            "custom",
            new CustomMappingContextFactory());
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "map",
                    MappingCompositionNodeTypes.Mapper,
                    node => node
                        .Resource(MappingCompositionResourceNames.Engine, "primary")
                        .Resource(MappingCompositionResourceNames.ContextFactory, "custom")
                        .Configure("expression", "map")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterMapper<InputMessage, OutputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var mapperNode = host.Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem();
        var input = mapperNode.Descriptor.Inputs[MappingCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var output = mapperNode.Descriptor.Outputs[MappingCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<OutputMessage>>();
        var results = new BufferBlock<FlowMessage<OutputMessage>>();
        output.Source.LinkTo(results);

        (await input.Target.SendAsync(FlowMessage.Create(new InputMessage("value")))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var result = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        result.Payload.Value.ShouldBe("custom:value");
    }

    [Fact]
    public async Task Missing_engine_resource_reference_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "map",
                    MappingCompositionNodeTypes.Mapper,
                    node => node.Configure("expression", "map")))
                .Build())
            .RegisterNodes(registry => registry.RegisterMapper<object, object>())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(
                MappingCompositionResourceNames.Engine,
                StringComparison.Ordinal));
    }

    private sealed record InputMessage(string Value);

    private sealed record OutputMessage(string Value);

    private sealed class CustomMappingContextFactory : IMappingContextFactory
    {
        public FlowMapContext Create(object? input, MappingNodeContext context)
        {
            var message = input.ShouldBeOfType<InputMessage>();
            return new FlowMapContext
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = message,
                    ["value"] = message,
                    ["mapped"] = $"custom:{message.Value}",
                    ["inputType"] = context.InputType.Name,
                    ["outputType"] = context.OutputType.Name
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
