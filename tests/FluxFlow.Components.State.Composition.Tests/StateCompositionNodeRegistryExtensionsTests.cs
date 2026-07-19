using System.Numerics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.State;
using FluxFlow.Components.State.Composition;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Diagnostics;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.State.Composition.Tests;

public sealed class StateCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void RegisterStateReducer_registers_request_result_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterStateReducer();

        var reducer = registry.Registrations[StateCompositionNodeTypes.Reducer];
        reducer.Inputs[StateCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(FlowValueStateReducerInput));
        reducer.Outputs[StateCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FlowResult<FlowValueStateReducerResult>));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_state_metadata()
    {
        var metadata = DesignMetadata();

        metadata.Type.Value.ShouldBe(StateCompositionNodeTypes.Reducer);
        metadata.DisplayName?.Value.ShouldBe("State Reducer");
        metadata.Category.ShouldBe(new ComponentCategory("State"));
        metadata.SuggestedEditorWidth.ShouldBe(460);
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == StateCompositionResourceNames.Clock);
        AssertResources(metadata);
        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
    }

    [Fact]
    public void Design_metadata_provider_describes_state_ports()
    {
        var metadata = DesignMetadata();

        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(StateCompositionPortNames.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType?.Value.ShouldBe(nameof(FlowValueStateReducerInput));
        input.IsPrimary.ShouldBeTrue();

        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(StateCompositionPortNames.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(1);
        output.ValueType?.Value.ShouldBe("FlowResult<FlowValueStateReducerResult>");
        output.IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public void Design_metadata_provider_describes_state_options()
    {
        var metadata = DesignMetadata();

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "engine",
            "keyExpression",
            "reducer",
            "expressionId",
            "expressionName",
            "initialState",
            "boundedCapacity",
            "maxKeys"
        ], ignoreOrder: false);

        AssertOption(metadata, "engine", OptionValueKind.Text);
        AssertOption(metadata, "keyExpression", OptionValueKind.Text);
        AssertOption(metadata, "reducer", OptionValueKind.Text, isRequired: true);
        AssertOption(metadata, "expressionId", OptionValueKind.Text);
        AssertOption(metadata, "expressionName", OptionValueKind.Text);
        AssertOption(metadata, "initialState", OptionValueKind.Json);
        AssertOption(
            metadata,
            "boundedCapacity",
            OptionValueKind.Number,
            defaultValue: 128,
            min: 1);
        AssertOption(
            metadata,
            "maxKeys",
            OptionValueKind.Number,
            defaultValue: 1024,
            min: 0);
    }

    [Fact]
    public void Design_metadata_provider_describes_state_option_hints()
    {
        var metadata = DesignMetadata();
        var options = metadata.Options.ToDictionary(
            option => option.Name.Value,
            StringComparer.Ordinal);

        AssertOptionHints(
            options["reducer"],
            "State",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: StateCompositionResourceNames.Engine);
        AssertOptionHints(
            options["keyExpression"],
            "State",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: StateCompositionResourceNames.Engine);
        AssertOptionHints(options["engine"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["expressionId"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["expressionName"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["initialState"], "State", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Json);
        AssertOptionHints(options["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(options["maxKeys"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public void Design_metadata_provider_describes_state_resource_picker_hints()
    {
        var metadata = DesignMetadata();
        var resources = metadata.Resources.ToDictionary(
            resource => resource.Name.Value,
            StringComparer.Ordinal);

        AssertResourceHints(
            resources[StateCompositionResourceNames.Engine],
            ResourceDesignMetadataAttributeValues.ExpressionEngine,
            "expression-engine:{name}");
        AssertResourceHints(
            resources[StateCompositionResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var provider = new StateComponentDesignMetadataProvider();
        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.All.Count.ShouldBe(1);
        catalog.TryGet(
            new ComponentType(StateCompositionNodeTypes.Reducer),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull().DisplayName?.Value.ShouldBe("State Reducer");
    }

    [Fact]
    public async Task Hosted_reducer_updates_state_preserves_correlation_id_and_uses_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-19T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var engine = new SampleExpressionEngine();

        await WithNodeAsync(
            async (input, output, descriptor) =>
            {
                var results = Link(output.Source);
                var events = Link(descriptor.Events.ShouldNotBeNull());
                var first = FlowMessage.Create(
                    new FlowValueStateReducerInput
                    {
                        Key = "a",
                        Input = FlowValue.From("first")
                    },
                    new CorrelationId("first"));
                var second = FlowMessage.Create(
                    new FlowValueStateReducerInput
                    {
                        Key = "a",
                        Input = FlowValue.From("second")
                    },
                    new CorrelationId("second"));

                (await input.Target.SendAsync(first).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(second).WaitAsync(Timeout)).ShouldBeTrue();

                var firstResult = await results.ReceiveAsync().WaitAsync(Timeout);
                var secondResult = await results.ReceiveAsync().WaitAsync(Timeout);

                firstResult.CorrelationId.ShouldBe(first.CorrelationId);
                firstResult.Payload.Kind.ShouldBe(StateResultKinds.Updated);
                var firstValue = firstResult.Payload.Value.ShouldNotBeNull();
                firstValue.Key.ShouldBe("a");
                firstValue.NewState.GetInteger().ShouldBe(11L);
                firstValue.Version.ShouldBe(1);
                firstValue.UpdatedAt.ShouldBe(timestamp);

                secondResult.CorrelationId.ShouldBe(second.CorrelationId);
                var secondValue = secondResult.Payload.Value.ShouldNotBeNull();
                secondValue.PreviousState.GetInteger().ShouldBe(11L);
                secondValue.NewState.GetInteger().ShouldBe(12L);
                secondValue.Version.ShouldBe(2);
                secondValue.UpdatedAt.ShouldBe(timestamp);
                descriptor.Errors.ShouldBeNull();

                var @event = await events.ReceiveAsync().WaitAsync(Timeout);
                @event.Name.ShouldBe(StateDiagnosticNames.ReducerUpdated);
                @event.Attributes["engine"].ShouldBe("sample");
                @event.Attributes["expressionName"].ShouldBe("counter");
            },
            node => node
                .Resource(StateCompositionResourceNames.Engine, "primary")
                .Resource(StateCompositionResourceNames.Clock, "fixed")
                .Configure("reducer", "count")
                .Configure("initialState", 10)
                .Configure("maxKeys", 4)
                .Configure("boundedCapacity", 8)
                .Configure("expressionName", "counter"),
            services =>
            {
                services.AddKeyedSingleton<IFlowExpressionEngine>("primary", engine);
                services.AddKeyedSingleton<TimeProvider>("fixed", clock);
            });
    }

    [Fact]
    public async Task Hosted_reducer_binds_key_expression_and_metadata()
    {
        await WithNodeAsync(
            async (input, output, descriptor) =>
            {
                var results = Link(output.Source);
                var events = Link(descriptor.Events.ShouldNotBeNull());
                var message = FlowMessage.Create(new FlowValueStateReducerInput
                {
                    Key = "ignored",
                    Input = FlowValue.From("payload"),
                    Variables = new Dictionary<string, FlowValue>
                    {
                        ["topic"] = FlowValue.From("orders/created")
                    }
                });

                (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();

                var result = await results.ReceiveAsync().WaitAsync(Timeout);
                result.Payload.Value.ShouldNotBeNull().Key.ShouldBe("orders/created");
                result.Payload.Value.NewState.GetString().ShouldBe("payload");

                var @event = await events.ReceiveAsync().WaitAsync(Timeout);
                @event.Attributes["expressionId"].ShouldBe("state-1");
                @event.Attributes["expressionName"].ShouldBe("last payload");
            },
            node => node
                .Resource(StateCompositionResourceNames.Engine, "primary")
                .Configure("engine", "diagnostic-only")
                .Configure("reducer", "last-input")
                .Configure("keyExpression", "topic-key")
                .Configure("expressionId", "state-1")
                .Configure("expressionName", "last payload")
                .Configure("maxKeys", 2),
            services => services.AddKeyedSingleton<IFlowExpressionEngine>(
                "primary",
                new SampleExpressionEngine()));
    }

    [Fact]
    public async Task Hosted_reducer_reset_and_clear_emit_results()
    {
        await WithNodeAsync(
            async (input, output, _) =>
            {
                var results = Link(output.Source);

                (await input.Target.SendAsync(FlowMessage.Create(new FlowValueStateReducerInput { Key = "a" }))
                    .WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(FlowMessage.Create(new FlowValueStateReducerInput
                    {
                        Key = "a",
                        InitialState = FlowValue.From(100),
                        Operation = StateReducerOperation.Reset
                    }))
                    .WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(FlowMessage.Create(new FlowValueStateReducerInput
                    {
                        Key = "a",
                        Operation = StateReducerOperation.Clear
                    }))
                    .WaitAsync(Timeout)).ShouldBeTrue();

                await results.ReceiveAsync().WaitAsync(Timeout);
                var reset = await results.ReceiveAsync().WaitAsync(Timeout);
                var clear = await results.ReceiveAsync().WaitAsync(Timeout);

                reset.Payload.Kind.ShouldBe(StateResultKinds.Reset);
                reset.Payload.Value.ShouldNotBeNull().NewState.GetInteger().ShouldBe(100);
                reset.Payload.Value.Version.ShouldBe(2);
                clear.Payload.Kind.ShouldBe(StateResultKinds.Cleared);
                clear.Payload.Value.ShouldNotBeNull().NewState.ShouldBeSameAs(FlowValue.Null);
                clear.Payload.Value.Version.ShouldBe(3);
            },
            node => node
                .Resource(StateCompositionResourceNames.Engine, "primary")
                .Configure("reducer", "count")
                .Configure("initialState", 5),
            services => services.AddKeyedSingleton<IFlowExpressionEngine>(
                "primary",
                new SampleExpressionEngine()));
    }

    [Fact]
    public async Task Hosted_reducer_emits_normal_failure_and_continues_after_reducer_failure()
    {
        await WithNodeAsync(
            async (input, output, descriptor) =>
            {
                var results = Link(output.Source);
                var bad = FlowMessage.Create(
                    new FlowValueStateReducerInput
                    {
                        Key = "a",
                        Input = FlowValue.From("bad")
                    },
                    new CorrelationId("bad"));
                var good = FlowMessage.Create(
                    new FlowValueStateReducerInput
                    {
                        Key = "a",
                        Input = FlowValue.From("good")
                    },
                    new CorrelationId("good"));

                (await input.Target.SendAsync(bad).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(good).WaitAsync(Timeout)).ShouldBeTrue();

                var failure = await results.ReceiveAsync().WaitAsync(Timeout);
                var success = await results.ReceiveAsync().WaitAsync(Timeout);

                failure.CorrelationId.ShouldBe(bad.CorrelationId);
                failure.Payload.IsError.ShouldBeTrue();
                failure.Payload.Error.ShouldNotBeNull().Code
                    .ShouldBe(StateErrorCodeNames.ReducerFailed);
                failure.Payload.Error.Details.GetObject()["legacyCode"].GetInteger()
                    .ShouldBe(StateErrorCodes.ReducerFailed);
                success.CorrelationId.ShouldBe(good.CorrelationId);
                success.Payload.Value.ShouldNotBeNull().NewState.GetString().ShouldBe("good");
                descriptor.Errors.ShouldBeNull();
            },
            node => node
                .Resource(StateCompositionResourceNames.Engine, "primary")
                .Configure("reducer", "fail-on-bad"),
            services => services.AddKeyedSingleton<IFlowExpressionEngine>(
                "primary",
                new SampleExpressionEngine()));
    }

    [Fact]
    public async Task Missing_engine_resource_reference_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "state",
                    StateCompositionNodeTypes.Reducer,
                    node => node.Configure("reducer", "count")))
                .Build())
            .RegisterNodes(registry => registry.RegisterStateReducer())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(
                StateCompositionResourceNames.Engine,
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("boundedCapacity", 0, "boundedCapacity")]
    [InlineData("maxKeys", -1, "maxKeys")]
    public async Task Invalid_configuration_surfaces_factory_diagnostic(
        string optionName,
        object value,
        string expectedMessage)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>(
            "primary",
            new SampleExpressionEngine());
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "state",
                    StateCompositionNodeTypes.Reducer,
                    node => node
                        .Resource(StateCompositionResourceNames.Engine, "primary")
                        .Configure("reducer", "count")
                        .Configure(optionName, value)))
                .Build())
            .RegisterNodes(registry => registry.RegisterStateReducer())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Missing_reducer_configuration_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFlowExpressionEngine>(
            "primary",
            new SampleExpressionEngine());
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "state",
                    StateCompositionNodeTypes.Reducer,
                    node => node.Resource(StateCompositionResourceNames.Engine, "primary")))
                .Build())
            .RegisterNodes(registry => registry.RegisterStateReducer())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains("reducer", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WithNodeAsync(
        Func<
            CompositionInputPort<FlowValueStateReducerInput>,
            CompositionOutputPort<FlowResult<FlowValueStateReducerResult>>,
            ComposedNode,
            Task> run,
        Action<NodeDefinitionBuilder> configureNode,
        Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();
        configureServices(services);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "state",
                    StateCompositionNodeTypes.Reducer,
                    configureNode))
                .Build())
            .RegisterNodes(registry => registry.RegisterStateReducer())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var descriptor = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem()
            .Descriptor;
        var input = descriptor.Inputs[StateCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<FlowValueStateReducerInput>>();
        var output = descriptor.Outputs[StateCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<FlowResult<FlowValueStateReducerResult>>>();

        await run(input, output, descriptor);
    }

    private static async Task BuildCompositionAsync(IServiceProvider provider)
    {
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();
        await hostedService.StartAsync(CancellationToken.None);
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }

    private static ComponentDesignMetadata DesignMetadata()
        => new StateComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

    private static void AssertOption(
        ComponentDesignMetadata metadata,
        string name,
        OptionValueKind kind,
        object? defaultValue = null,
        double? min = null,
        bool isRequired = false)
    {
        var option = metadata.Options.Single(option => option.Name.Value == name);
        option.Kind.ShouldBe(kind);
        option.DefaultValue.ShouldBe(defaultValue);
        option.Min.ShouldBe(min);
        option.IsRequired.ShouldBe(isRequired);
    }

    private static void AssertResources(ComponentDesignMetadata metadata)
    {
        metadata.Resources.Select(resource => (
            resource.Name.Value,
            resource.Order,
            resource.IsRequired,
            resource.ValueType?.Value)).ShouldBe([
            (StateCompositionResourceNames.Engine, 0, true, nameof(IFlowExpressionEngine)),
            (StateCompositionResourceNames.Clock, 1, false, nameof(TimeProvider))
        ]);
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

    private sealed class SampleExpressionEngine : IFlowExpressionEngine
    {
        public string Name => "sample";

        public object? Evaluate(
            string expression,
            FlowMapContext context,
            Type resultType)
        {
            var input = (FlowValue)context.Variables["input"]!;
            var state = (FlowValue)context.Variables["state"]!;
            return expression switch
            {
                "count" => FlowValue.From(CoerceNumber(state) + 1),
                "last-input" => input,
                "topic-key" => (object)((FlowValue)context.Variables["topic"]!).GetString(),
                "fail-on-bad" when input.GetString() == "bad" =>
                    throw new InvalidOperationException("bad input"),
                "fail-on-bad" => input,
                _ => throw new InvalidOperationException($"Unknown expression '{expression}'.")
            };
        }

        private static BigInteger CoerceNumber(FlowValue value)
            => value.Kind == FlowValueKind.Null ? 0 : value.GetInteger();
    }
}
