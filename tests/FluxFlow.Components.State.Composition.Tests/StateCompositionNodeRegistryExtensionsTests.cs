using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.State;
using FluxFlow.Components.State.Composition;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Diagnostics;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
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
            .ShouldBe(typeof(StateReducerInput));
        reducer.Outputs[StateCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(StateReducerResult));
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
                    new StateReducerInput { Key = "a", Input = "first" },
                    new CorrelationId("first"));
                var second = FlowMessage.Create(
                    new StateReducerInput { Key = "a", Input = "second" },
                    new CorrelationId("second"));

                (await input.Target.SendAsync(first).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(second).WaitAsync(Timeout)).ShouldBeTrue();

                var firstResult = await results.ReceiveAsync().WaitAsync(Timeout);
                var secondResult = await results.ReceiveAsync().WaitAsync(Timeout);

                firstResult.CorrelationId.ShouldBe(first.CorrelationId);
                firstResult.Payload.Key.ShouldBe("a");
                firstResult.Payload.NewState.ShouldBe(11L);
                firstResult.Payload.Version.ShouldBe(1);
                firstResult.Payload.UpdatedAt.ShouldBe(timestamp);

                secondResult.CorrelationId.ShouldBe(second.CorrelationId);
                secondResult.Payload.PreviousState.ShouldBe(11L);
                secondResult.Payload.NewState.ShouldBe(12L);
                secondResult.Payload.Version.ShouldBe(2);
                secondResult.Payload.UpdatedAt.ShouldBe(timestamp);

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
                var message = FlowMessage.Create(new StateReducerInput
                {
                    Key = "ignored",
                    Input = "payload",
                    Variables = new Dictionary<string, object?>
                    {
                        ["topic"] = "orders/created"
                    }
                });

                (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();

                var result = await results.ReceiveAsync().WaitAsync(Timeout);
                result.Payload.Key.ShouldBe("orders/created");
                result.Payload.NewState.ShouldBe("payload");

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

                (await input.Target.SendAsync(FlowMessage.Create(new StateReducerInput { Key = "a" }))
                    .WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(FlowMessage.Create(new StateReducerInput
                    {
                        Key = "a",
                        InitialState = 100,
                        Operation = StateReducerOperation.Reset
                    }))
                    .WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(FlowMessage.Create(new StateReducerInput
                    {
                        Key = "a",
                        Operation = StateReducerOperation.Clear
                    }))
                    .WaitAsync(Timeout)).ShouldBeTrue();

                await results.ReceiveAsync().WaitAsync(Timeout);
                var reset = await results.ReceiveAsync().WaitAsync(Timeout);
                var clear = await results.ReceiveAsync().WaitAsync(Timeout);

                reset.Payload.NewState.ShouldBe(100);
                reset.Payload.Version.ShouldBe(2);
                clear.Payload.NewState.ShouldBeNull();
                clear.Payload.Version.ShouldBe(3);
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
    public async Task Hosted_reducer_emits_errors_and_continues_after_reducer_failure()
    {
        await WithNodeAsync(
            async (input, output, descriptor) =>
            {
                var results = Link(output.Source);
                var errors = Link(descriptor.Errors.ShouldNotBeNull());
                var bad = FlowMessage.Create(
                    new StateReducerInput { Key = "a", Input = "bad" },
                    new CorrelationId("bad"));
                var good = FlowMessage.Create(
                    new StateReducerInput { Key = "a", Input = "good" },
                    new CorrelationId("good"));

                (await input.Target.SendAsync(bad).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(good).WaitAsync(Timeout)).ShouldBeTrue();

                var error = await errors.ReceiveAsync().WaitAsync(Timeout);
                var result = await results.ReceiveAsync().WaitAsync(Timeout);

                error.Code.ShouldBe(StateErrorCodes.ReducerFailed);
                error.CorrelationId.ShouldBe(bad.CorrelationId);
                result.CorrelationId.ShouldBe(good.CorrelationId);
                result.Payload.NewState.ShouldBe("good");
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
            CompositionInputPort<StateReducerInput>,
            CompositionOutputPort<StateReducerResult>,
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
            .ShouldBeOfType<CompositionInputPort<StateReducerInput>>();
        var output = descriptor.Outputs[StateCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<StateReducerResult>>();

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

    private sealed class SampleExpressionEngine : IFlowExpressionEngine
    {
        public string Name => "sample";

        public object? Evaluate(
            string expression,
            FlowMapContext context,
            Type resultType)
            => expression switch
            {
                "count" => CoerceNumber(context.Variables["state"]) + 1,
                "last-input" => context.Variables["input"],
                "topic-key" => context.Variables["topic"],
                "fail-on-bad" when Equals(context.Variables["input"], "bad") =>
                    throw new InvalidOperationException("bad input"),
                "fail-on-bad" => context.Variables["input"],
                _ => throw new InvalidOperationException($"Unknown expression '{expression}'.")
            };

        private static long CoerceNumber(object? value)
            => value switch
            {
                null => 0,
                long number => number,
                int number => number,
                JsonElement json when json.ValueKind == JsonValueKind.Number &&
                                      json.TryGetInt64(out var number) => number,
                _ => throw new InvalidOperationException(
                    $"Cannot coerce '{value.GetType().Name}' to a number.")
            };
    }
}
