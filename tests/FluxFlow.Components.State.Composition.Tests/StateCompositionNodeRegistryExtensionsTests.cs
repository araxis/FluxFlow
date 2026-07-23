using System.Numerics;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.State;
using FluxFlow.Components.State.Composition;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Diagnostics;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting.DependencyInjection;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.State.Composition.Tests;

public sealed class StateCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "node", StateCompositionPortNames.Input);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", StateCompositionPortNames.Output);
    private static readonly ApplicationAddress Events =
        ApplicationAddress.WorkflowPort("main", "node", CompositionComponentEvents.PortName);

    [Fact]
    public void RegisterStateReducer_registers_only_the_canonical_contract()
    {
        var registry = new CompositionNodeRegistry().RegisterStateReducer();

        var reducer = registry.Registrations[StateCompositionNodeTypes.Reducer];
        reducer.Inputs.Keys.ShouldBe([StateCompositionPortNames.Input]);
        reducer.Outputs.Keys.ShouldBe([
            StateCompositionPortNames.Output,
            CompositionComponentEvents.PortName
        ], ignoreOrder: false);
        reducer.Inputs[StateCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(FlowValueStateReducerInput));
        reducer.Outputs[StateCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FlowResult<FlowValueStateReducerResult>));
    }

    [Fact]
    public void RegisterStateReducer_supports_explicit_canonical_component_types()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterStateReducer("state.reduce.primary")
            .RegisterStateReducer("state.reduce.secondary");

        registry.Registrations.Keys.ShouldBe([
            "state.reduce.primary",
            "state.reduce.secondary"
        ], ignoreOrder: false);
        registry.Registrations.Values.ShouldAllBe(registration =>
            registration.Inputs[StateCompositionPortNames.Input].MessageType ==
                typeof(FlowValueStateReducerInput) &&
            registration.Outputs[StateCompositionPortNames.Output].MessageType ==
                typeof(FlowResult<FlowValueStateReducerResult>));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_canonical_state_metadata()
    {
        var metadata = DesignMetadata();

        metadata.Type.ShouldBe(new ComponentType(StateCompositionNodeTypes.Reducer));
        metadata.DisplayName?.Value.ShouldBe("State Reducer");
        metadata.Category.ShouldBe(new ComponentCategory("State"));
        metadata.PreferredNodeName.ShouldBe(new ComponentPreferredNodeName("stateReducer"));
        metadata.SuggestedEditorWidth.ShouldBe(460);
        metadata.Options.Select(option => (option.Name.Value, option.Kind)).ShouldBe([
            ("keyExpression", OptionValueKind.Text),
            ("reducer", OptionValueKind.Text),
            ("expressionId", OptionValueKind.Text),
            ("expressionName", OptionValueKind.Text),
            ("initialState", OptionValueKind.Json),
            ("boundedCapacity", OptionValueKind.Number),
            ("maxKeys", OptionValueKind.Number)
        ], ignoreOrder: false);
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == StateCompositionResourceNames.Engine ||
            option.Name.Value == StateCompositionResourceNames.Clock);
        AssertResources(metadata);
        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
    }

    [Fact]
    public void Design_metadata_provider_describes_canonical_state_ports()
    {
        var metadata = DesignMetadata();

        metadata.Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.ValueType?.Value,
            port.IsPrimary)).ShouldBe([
                (StateCompositionPortNames.Input, PortDirection.Input, 0,
                    nameof(FlowValueStateReducerInput), true),
                (StateCompositionPortNames.Output, PortDirection.Output, 1,
                    "FlowResult<FlowValueStateReducerResult>", true)
            ], ignoreOrder: false);
    }

    [Fact]
    public void Design_metadata_provider_describes_canonical_state_options()
    {
        var metadata = DesignMetadata();

        AssertOption(metadata, "keyExpression", OptionValueKind.Text);
        AssertOption(metadata, "reducer", OptionValueKind.Text, isRequired: true);
        AssertOption(metadata, "expressionId", OptionValueKind.Text);
        AssertOption(metadata, "expressionName", OptionValueKind.Text);
        AssertOption(metadata, "initialState", OptionValueKind.Json);
        AssertOption(metadata, "boundedCapacity", OptionValueKind.Number, 128, min: 1);
        AssertOption(metadata, "maxKeys", OptionValueKind.Number, 1024, min: 0);
    }

    [Fact]
    public void Design_metadata_provider_describes_state_option_hints()
    {
        var options = DesignMetadata().Options.ToDictionary(
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
        AssertOptionHints(options["expressionId"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["expressionName"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["initialState"], "State", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Json);
        AssertOptionHints(options["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(options["maxKeys"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public void Design_metadata_provider_uses_canonical_resource_picker_addresses()
    {
        var resources = DesignMetadata().Resources.ToDictionary(
            resource => resource.Name.Value,
            StringComparer.Ordinal);

        AssertResourceHints(
            resources[StateCompositionResourceNames.Engine],
            ResourceDesignMetadataAttributeValues.ExpressionEngine,
            "Resources.{name}");
        AssertResourceHints(
            resources[StateCompositionResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "Resources.{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentDesignMetadataCatalog.FromProviders(
            [new StateComponentDesignMetadataProvider()]);

        catalog.All.Count.ShouldBe(1);
        catalog.TryGet(
            new ComponentType(StateCompositionNodeTypes.Reducer),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull().DisplayName?.Value.ShouldBe("State Reducer");
    }

    [Fact]
    public async Task Canonical_host_updates_state_preserves_lineage_and_uses_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-19T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var engine = new SampleExpressionEngine();

        await WithNodeAsync(
            engine,
            async (ports, _) =>
            {
                var firstReceive = ports.ReceiveAsync<FlowResult<FlowValueStateReducerResult>>(
                    Output,
                    Timeout);
                var eventReceive = ports.ReceiveAsync<CompositionComponentEvent>(Events, Timeout);
                var first = FlowMessage.Create(
                    new FlowValueStateReducerInput
                    {
                        Key = "a",
                        Input = FlowValue.From("first")
                    },
                    new CorrelationId("first"));

                (await ports.SendAsync(Input, first)).IsAccepted.ShouldBeTrue();
                var firstResult = (await firstReceive).Message.ShouldNotBeNull();
                firstResult.CorrelationId.ShouldBe(first.CorrelationId);
                firstResult.TraceId.ShouldBe(first.TraceId);
                firstResult.CausationId.ShouldBe(first.MessageId);
                firstResult.Payload.Kind.ShouldBe(StateResultKinds.Updated);
                var firstValue = firstResult.Payload.Value.ShouldNotBeNull();
                firstValue.Key.ShouldBe("a");
                firstValue.NewState.GetInteger().ShouldBe(11);
                firstValue.Version.ShouldBe(1);
                firstValue.UpdatedAt.ShouldBe(timestamp);

                var secondReceive = ports.ReceiveAsync<FlowResult<FlowValueStateReducerResult>>(
                    Output,
                    Timeout);
                var second = FlowMessage.Create(
                    new FlowValueStateReducerInput
                    {
                        Key = "a",
                        Input = FlowValue.From("second")
                    },
                    new CorrelationId("second"));
                (await ports.SendAsync(Input, second)).IsAccepted.ShouldBeTrue();
                var secondResult = (await secondReceive).Message.ShouldNotBeNull();
                secondResult.Payload.Value.ShouldNotBeNull().PreviousState.GetInteger().ShouldBe(11);
                secondResult.Payload.Value.NewState.GetInteger().ShouldBe(12);
                secondResult.Payload.Value.Version.ShouldBe(2);

                var @event = (await eventReceive).Message.ShouldNotBeNull();
                @event.CorrelationId.ShouldBe(first.CorrelationId);
                @event.Payload.Name.ShouldBe(StateDiagnosticNames.ReducerUpdated);
                @event.Payload.Attributes["engine"].GetString().ShouldBe("sample");
                @event.Payload.Attributes["expressionName"].GetString().ShouldBe("counter");
            },
            Properties(
                ("reducer", "count"),
                ("initialState", 10),
                ("maxKeys", 4),
                ("boundedCapacity", 8),
                ("expressionName", "counter")),
            clock);
    }

    [Fact]
    public async Task Canonical_host_binds_key_expression_and_diagnostic_metadata()
    {
        await WithNodeAsync(
            new SampleExpressionEngine(),
            async (ports, _) =>
            {
                var resultReceive = ports.ReceiveAsync<FlowResult<FlowValueStateReducerResult>>(
                    Output,
                    Timeout);
                var eventReceive = ports.ReceiveAsync<CompositionComponentEvent>(Events, Timeout);
                var message = FlowMessage.Create(new FlowValueStateReducerInput
                {
                    Key = "ignored",
                    Input = FlowValue.From("payload"),
                    Variables = new Dictionary<string, FlowValue>
                    {
                        ["topic"] = FlowValue.From("orders/created")
                    }
                });

                (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();

                var result = (await resultReceive).Message.ShouldNotBeNull();
                result.Payload.Value.ShouldNotBeNull().Key.ShouldBe("orders/created");
                result.Payload.Value.NewState.GetString().ShouldBe("payload");
                var @event = (await eventReceive).Message.ShouldNotBeNull();
                @event.Payload.Attributes["expressionId"].GetString().ShouldBe("state-1");
                @event.Payload.Attributes["expressionName"].GetString().ShouldBe("last payload");
            },
            Properties(
                ("reducer", "last-input"),
                ("keyExpression", "topic-key"),
                ("expressionId", "state-1"),
                ("expressionName", "last payload"),
                ("maxKeys", 2)));
    }

    [Fact]
    public async Task Canonical_host_emits_reset_clear_and_normal_failure_results()
    {
        await WithNodeAsync(
            new SampleExpressionEngine(),
            async (ports, _) =>
            {
                var firstReceive = ports.ReceiveAsync<FlowResult<FlowValueStateReducerResult>>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(new FlowValueStateReducerInput
                {
                    Key = "a"
                }))).IsAccepted.ShouldBeTrue();
                await firstReceive;

                var resetReceive = ports.ReceiveAsync<FlowResult<FlowValueStateReducerResult>>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(new FlowValueStateReducerInput
                {
                    Key = "a",
                    InitialState = FlowValue.From(100),
                    Operation = StateReducerOperation.Reset
                }))).IsAccepted.ShouldBeTrue();
                var reset = (await resetReceive).Message.ShouldNotBeNull().Payload;
                reset.Kind.ShouldBe(StateResultKinds.Reset);
                reset.Value.ShouldNotBeNull().NewState.GetInteger().ShouldBe(100);
                reset.Value.Version.ShouldBe(2);

                var clearReceive = ports.ReceiveAsync<FlowResult<FlowValueStateReducerResult>>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(new FlowValueStateReducerInput
                {
                    Key = "a",
                    Operation = StateReducerOperation.Clear
                }))).IsAccepted.ShouldBeTrue();
                var clear = (await clearReceive).Message.ShouldNotBeNull().Payload;
                clear.Kind.ShouldBe(StateResultKinds.Cleared);
                clear.Value.ShouldNotBeNull().NewState.ShouldBeSameAs(FlowValue.Null);
                clear.Value.Version.ShouldBe(3);

                var failureReceive = ports.ReceiveAsync<FlowResult<FlowValueStateReducerResult>>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(new FlowValueStateReducerInput
                {
                    Key = "a",
                    Input = FlowValue.From("bad")
                }))).IsAccepted.ShouldBeTrue();
                var failure = (await failureReceive).Message.ShouldNotBeNull().Payload;
                failure.IsError.ShouldBeTrue();
                failure.Error.ShouldNotBeNull().Code.ShouldBe(StateErrorCodeNames.ReducerFailed);

                var successReceive = ports.ReceiveAsync<FlowResult<FlowValueStateReducerResult>>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(new FlowValueStateReducerInput
                {
                    Key = "a",
                    Input = FlowValue.From("good")
                }))).IsAccepted.ShouldBeTrue();
                var success = (await successReceive).Message.ShouldNotBeNull().Payload;
                success.IsError.ShouldBeFalse();
                success.Value.ShouldNotBeNull().NewState.GetString().ShouldBe("good");
            },
            Properties(
                ("reducer", "fail-on-bad"),
                ("initialState", 5)));
    }

    [Fact]
    public async Task Missing_engine_resource_reference_surfaces_preparation_failure()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                StateCompositionNodeTypes.Reducer,
                Properties(("reducer", "count"))),
            registry => registry.RegisterStateReducer());

        AssertPreparationFailure(host, StateCompositionResourceNames.Engine);
    }

    [Theory]
    [InlineData("boundedCapacity", 0, "boundedCapacity")]
    [InlineData("maxKeys", -1, "maxKeys")]
    public async Task Invalid_configuration_surfaces_preparation_failure(
        string optionName,
        object value,
        string expectedMessage)
    {
        var properties = Properties(
            ("reducer", "count"),
            (optionName, value));
        await using var host = await StartHostAsync(new SampleExpressionEngine(), properties);

        AssertPreparationFailure(host, expectedMessage);
    }

    [Fact]
    public async Task Missing_reducer_configuration_surfaces_preparation_failure()
    {
        await using var host = await StartHostAsync(
            new SampleExpressionEngine(),
            Properties());

        AssertPreparationFailure(host, "reducer");
    }

    private static ComponentDesignMetadata DesignMetadata()
        => new StateComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

    private static async Task WithNodeAsync(
        IFlowExpressionEngine engine,
        Func<ApplicationPortRuntime, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?> properties,
        TimeProvider? clock = null)
    {
        await using var host = await StartHostAsync(engine, properties, clock);
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
    }

    private static ValueTask<CanonicalApplicationTestHost> StartHostAsync(
        IFlowExpressionEngine engine,
        IReadOnlyDictionary<string, object?> properties,
        TimeProvider? clock = null)
    {
        var componentProperties = properties.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal);
        componentProperties[StateCompositionResourceNames.Engine] = "Resources.engine";
        var resources = new List<string> { "engine" };
        if (clock is not null)
        {
            componentProperties[StateCompositionResourceNames.Clock] = "Resources.clock";
            resources.Add("clock");
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                StateCompositionNodeTypes.Reducer,
                componentProperties,
                resources),
            registry => registry.RegisterStateReducer(),
            configureRuntimeServices: context =>
            {
                context.Services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                    ApplicationAddress.Resource("engine"),
                    engine);
                if (clock is not null)
                {
                    context.Services.AddExternalFluxFlowResource<TimeProvider>(
                        ApplicationAddress.Resource("clock"),
                        clock);
                }
            });
    }

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
            option.Attributes.ContainsKey(
                new ComponentAttributeName(OptionDesignMetadataAttributeNames.Syntax))
                .ShouldBeFalse();
        }
        else
        {
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Syntax)
                .ShouldBe(syntax);
        }

        if (relatedResource is null)
        {
            option.Attributes.ContainsKey(
                new ComponentAttributeName(OptionDesignMetadataAttributeNames.RelatedResource))
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
                "fail-on-bad" when input.Kind == FlowValueKind.String &&
                                       input.GetString() == "bad" =>
                    throw new InvalidOperationException("bad input"),
                "fail-on-bad" => input,
                _ => throw new InvalidOperationException($"Unknown expression '{expression}'.")
            };
        }

        private static BigInteger CoerceNumber(FlowValue value)
            => value.Kind == FlowValueKind.Null ? 0 : value.GetInteger();
    }
}
