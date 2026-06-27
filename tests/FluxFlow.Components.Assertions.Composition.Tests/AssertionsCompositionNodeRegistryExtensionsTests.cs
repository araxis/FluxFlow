using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Assertions.Composition;
using FluxFlow.Components.Assertions.Contracts;
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

namespace FluxFlow.Components.Assertions.Composition.Tests;

public sealed class AssertionsCompositionNodeRegistryExtensionsTests
{
    [Fact]
    public void RegisterAssertion_registers_closed_assertion_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterAssertion<InputMessage>();

        var assertion = registry.Registrations[AssertionsCompositionNodeTypes.Assert];
        assertion.Inputs[AssertionsCompositionPortNames.Input].MessageType.ShouldBe(
            typeof(InputMessage));
        assertion.Outputs[AssertionsCompositionPortNames.Output].MessageType.ShouldBe(
            typeof(FlowAssertionResult));
        assertion.Outputs[AssertionsCompositionPortNames.Passed].MessageType.ShouldBe(
            typeof(InputMessage));
        assertion.Outputs[AssertionsCompositionPortNames.Failed].MessageType.ShouldBe(
            typeof(InputMessage));
    }

    [Fact]
    public void RegisterAssertion_supports_multiple_custom_node_types()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterAssertion<InputMessage>("flow.assert.input")
            .RegisterAssertion<string>("flow.assert.string");

        registry.Registrations["flow.assert.input"]
            .Inputs[AssertionsCompositionPortNames.Input].MessageType.ShouldBe(
                typeof(InputMessage));
        registry.Registrations["flow.assert.string"]
            .Inputs[AssertionsCompositionPortNames.Input].MessageType.ShouldBe(
                typeof(string));
        registry.Registrations["flow.assert.input"]
            .Outputs[AssertionsCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(FlowAssertionResult));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_assertion_metadata()
    {
        var metadata = new AssertionsComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Type.ShouldBe(new ComponentType(AssertionsCompositionNodeTypes.Assert));
        metadata.DisplayName.ShouldBe("Assertion");
        metadata.Category.ShouldBe(new ComponentCategory("Assertions"));
        metadata.PreferredNodeName.ShouldBe(new ComponentPreferredNodeName("assert"));
        metadata.SuggestedEditorWidth.ShouldBe(420);
        metadata.Options.Select(option => (option.Name.Value, option.Kind)).ShouldBe([
            ("expression", OptionValueKind.Expression),
            ("expressionId", OptionValueKind.Text),
            ("expressionName", OptionValueKind.Text),
            ("engine", OptionValueKind.Text),
            ("inputType", OptionValueKind.Text),
            ("boundedCapacity", OptionValueKind.Number),
            ("description", OptionValueKind.Text),
            ("failureMessage", OptionValueKind.Text),
            ("emitPassedInput", OptionValueKind.Boolean),
            ("emitFailedInput", OptionValueKind.Boolean)
        ]);
        metadata.Options.Single(option => option.Name.Value == "expression")
            .IsRequired.ShouldBeTrue();
        metadata.Options.Single(option => option.Name.Value == "boundedCapacity")
            .Min.ShouldBe(1);
        metadata.Options.Single(option => option.Name.Value == "description")
            .DefaultValue.ShouldBe("Flow assertion");
        metadata.Options.Single(option => option.Name.Value == "failureMessage")
            .DefaultValue.ShouldBe("Assertion failed.");
        metadata.Options.Select(option => option.Name.Value)
            .ShouldNotContain(AssertionsCompositionResourceNames.ContextFactory);
        metadata.Options.Select(option => option.Name.Value)
            .ShouldNotContain(AssertionsCompositionResourceNames.Clock);
        AssertResources(
            metadata,
            (AssertionsCompositionResourceNames.Engine, 0, true, nameof(IFlowExpressionEngine)),
            (AssertionsCompositionResourceNames.ContextFactory, 1, false, "IFlowMapContextFactory<TInput>"),
            (AssertionsCompositionResourceNames.Clock, 2, false, nameof(TimeProvider))
        );
    }

    [Fact]
    public void Design_metadata_provider_describes_assertion_ports()
    {
        var metadata = new AssertionsComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

        metadata.Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.IsPrimary,
            port.ValueType?.Value)).ShouldBe([
            (AssertionsCompositionPortNames.Input, PortDirection.Input, 0, true, "TInput"),
            (AssertionsCompositionPortNames.Output, PortDirection.Output, 1, true, nameof(FlowAssertionResult)),
            (AssertionsCompositionPortNames.Passed, PortDirection.Output, 2, false, "TInput"),
            (AssertionsCompositionPortNames.Failed, PortDirection.Output, 3, false, "TInput")
        ]);
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var provider = new AssertionsComponentDesignMetadataProvider();

        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.TryGet(
            new ComponentType(AssertionsCompositionNodeTypes.Assert),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull();
        metadata.Type.ShouldBe(new ComponentType(AssertionsCompositionNodeTypes.Assert));
    }

    private static void AssertResources(
        ComponentDesignMetadata metadata,
        params (string Name, int Order, bool IsRequired, string ValueType)[] expected)
    {
        metadata.Resources.Count.ShouldBe(expected.Length);

        for (var index = 0; index < expected.Length; index++)
        {
            var resource = metadata.Resources[index];
            resource.Name.Value.ShouldBe(expected[index].Name);
            resource.Order.ShouldBe(expected[index].Order);
            resource.IsRequired.ShouldBe(expected[index].IsRequired);
            resource.ValueType?.Value.ShouldBe(expected[index].ValueType);
        }
    }

    [Fact]
    public async Task Hosted_assertion_resolves_keyed_engine_and_routes_inputs()
    {
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, resultType) =>
            {
                resultType.ShouldBe(typeof(bool));
                var input = (InputMessage)context.Variables["input"]!;
                return input.Score >= 10;
            });
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>("primary", engine);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "assert",
                    AssertionsCompositionNodeTypes.Assert,
                    node => node
                        .Resource(AssertionsCompositionResourceNames.Engine, "primary")
                        .Configure("expression", "input.Score >= 10")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterAssertion<InputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var assertionNode = host.Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem();
        var input = assertionNode.Descriptor.Inputs[AssertionsCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var output = assertionNode.Descriptor.Outputs[AssertionsCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<FlowAssertionResult>>();
        var passed = assertionNode.Descriptor.Outputs[AssertionsCompositionPortNames.Passed]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var failed = assertionNode.Descriptor.Outputs[AssertionsCompositionPortNames.Failed]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var results = new BufferBlock<FlowMessage<FlowAssertionResult>>();
        var passedResults = new BufferBlock<FlowMessage<InputMessage>>();
        var failedResults = new BufferBlock<FlowMessage<InputMessage>>();
        output.Source.LinkTo(results);
        passed.Source.LinkTo(passedResults);
        failed.Source.LinkTo(failedResults);

        var high = FlowMessage.Create(
            new InputMessage(12),
            new CorrelationId("assert-passed"));
        var low = FlowMessage.Create(
            new InputMessage(3),
            new CorrelationId("assert-failed"));

        (await input.Target.SendAsync(high)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        (await input.Target.SendAsync(low)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var passedResult = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        var failedResult = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        var passedInput = await passedResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        var failedInput = await failedResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        passedResult.CorrelationId.ShouldBe(new CorrelationId("assert-passed"));
        passedResult.Payload.Passed.ShouldBeTrue();
        failedResult.CorrelationId.ShouldBe(new CorrelationId("assert-failed"));
        failedResult.Payload.Passed.ShouldBeFalse();
        passedInput.CorrelationId.ShouldBe(new CorrelationId("assert-passed"));
        passedInput.Payload.Score.ShouldBe(12);
        failedInput.CorrelationId.ShouldBe(new CorrelationId("assert-failed"));
        failedInput.Payload.Score.ShouldBe(3);
    }

    [Fact]
    public async Task Hosted_assertion_binds_options_from_configuration()
    {
        var engine = new RecordingExpressionEngine(evaluate: (_, _, _) => false);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>("primary", engine);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "assert",
                    AssertionsCompositionNodeTypes.Assert,
                    node => node
                        .Resource(AssertionsCompositionResourceNames.Engine, "primary")
                        .Configure("expression", "score >= 10")
                        .Configure("description", "configured assertion")
                        .Configure("failureMessage", "Score too low.")
                        .Configure("inputType", "app.input")
                        .Configure("emitPassedInput", false)
                        .Configure("emitFailedInput", false)
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterAssertion<InputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var assertionNode = host.Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem();
        var input = assertionNode.Descriptor.Inputs[AssertionsCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var output = assertionNode.Descriptor.Outputs[AssertionsCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<FlowAssertionResult>>();
        var passed = assertionNode.Descriptor.Outputs[AssertionsCompositionPortNames.Passed]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var failed = assertionNode.Descriptor.Outputs[AssertionsCompositionPortNames.Failed]
            .ShouldBeOfType<CompositionOutputPort<InputMessage>>();
        var results = new BufferBlock<FlowMessage<FlowAssertionResult>>();
        var passedResults = new BufferBlock<FlowMessage<InputMessage>>();
        var failedResults = new BufferBlock<FlowMessage<InputMessage>>();
        output.Source.LinkTo(
            results,
            new DataflowLinkOptions { PropagateCompletion = true });
        passed.Source.LinkTo(passedResults);
        failed.Source.LinkTo(failedResults);

        (await input.Target.SendAsync(FlowMessage.Create(new InputMessage(3)))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        input.Target.Complete();

        var result = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Payload.Passed.ShouldBeFalse();
        result.Payload.Description.ShouldBe("configured assertion");
        result.Payload.Message.ShouldBe("Score too low.");
        result.Payload.InputType.ShouldBe("app.input");
        passedResults.TryReceive(out _).ShouldBeFalse();
        failedResults.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Hosted_assertion_uses_optional_keyed_context_factory()
    {
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, _) => context.Variables["passed"]);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>("primary", engine);
        services.AddKeyedSingleton<IFlowMapContextFactory<InputMessage>>(
            "custom",
            new CustomContextFactory(passed: true));
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "assert",
                    AssertionsCompositionNodeTypes.Assert,
                    node => node
                        .Resource(AssertionsCompositionResourceNames.Engine, "primary")
                        .Resource(AssertionsCompositionResourceNames.ContextFactory, "custom")
                        .Configure("expression", "passed")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterAssertion<InputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var assertionNode = host.Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem();
        var input = assertionNode.Descriptor.Inputs[AssertionsCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var output = assertionNode.Descriptor.Outputs[AssertionsCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<FlowAssertionResult>>();
        var results = new BufferBlock<FlowMessage<FlowAssertionResult>>();
        output.Source.LinkTo(results);

        (await input.Target.SendAsync(FlowMessage.Create(new InputMessage(1)))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var result = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        result.Payload.Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task Hosted_assertion_uses_optional_keyed_clock_for_results()
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
                    "assert",
                    AssertionsCompositionNodeTypes.Assert,
                    node => node
                        .Resource(AssertionsCompositionResourceNames.Engine, "primary")
                        .Resource(AssertionsCompositionResourceNames.Clock, "fixed")
                        .Configure("expression", "pass")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterAssertion<InputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var assertionNode = host.Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem();
        var input = assertionNode.Descriptor.Inputs[AssertionsCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var output = assertionNode.Descriptor.Outputs[AssertionsCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<FlowAssertionResult>>();
        var results = new BufferBlock<FlowMessage<FlowAssertionResult>>();
        output.Source.LinkTo(results);

        (await input.Target.SendAsync(FlowMessage.Create(new InputMessage(1)))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var result = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        result.Payload.EvaluatedAt.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Missing_engine_resource_reference_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "assert",
                    AssertionsCompositionNodeTypes.Assert,
                    node => node.Configure("expression", "pass")))
                .Build())
            .RegisterNodes(registry => registry.RegisterAssertion<object>())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(
                AssertionsCompositionResourceNames.Engine,
                StringComparison.Ordinal));
    }

    private sealed record InputMessage(int Score);

    private sealed class CustomContextFactory(bool passed) :
        IFlowMapContextFactory<InputMessage>
    {
        public FlowMapContext Create(InputMessage input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["passed"] = passed
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
