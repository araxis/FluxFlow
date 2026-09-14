using FluxFlow.Json.Model;
using System.Text.Json;
using FluxFlow.Components.Mapping.Composition;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Definition;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Mapping.Composition.Tests;

public sealed class MappingServiceCollectionExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "node", MappingComponentDefinition.Ports.Input);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", MappingComponentDefinition.Ports.Output);
    private static readonly ApplicationAddress Events =
        ApplicationAddress.WorkflowPort("main", "node", "Events");

    [Fact]
    public async Task AddMapping_registers_only_the_canonical_json_factory()
    {
        var services = new ServiceCollection().AddMapping();
        services.AddExternalFluxFlowResource<IFlowExpressionEngine>(ApplicationAddress.Resource("engine"), new RecordingExpressionEngine());
        await using var provider = services.BuildServiceProvider();
        var type = MappingComponentDefinition.Types.Mapper;
        var instance = await FluxFlowRegistrationExtensions.CreateFactoryMap(provider)[type](
            new ComponentActivationContext(provider, "main", "node", new ComponentDefinition(type, new[] { new KeyValuePair<string, JsonElement>("expression", JsonSerializer.SerializeToElement("input")), new KeyValuePair<string, JsonElement>("engine", JsonSerializer.SerializeToElement("Resources.engine")) })));
        try
        {
            instance.Inputs.Keys.ShouldBe(new[] { "Input" });
            instance.Outputs.Keys.ShouldBe(new[] { "Output", "Events" });
            instance.InputMetadata.Values.ShouldAllBe(p => p.MessageType == typeof(FlowValue) && p.Kind == ComponentPortKind.Message);
            instance.OutputMetadata.Values.ShouldAllBe(p => p.MessageType == typeof(FlowValue));
        }
        finally { await instance.DisposeAsync(); }
    }

    [Fact]
    public void AddMapping_is_idempotent()
    {
        using var provider = new ServiceCollection().AddMapping().AddMapping().BuildServiceProvider();
        var catalog = FluxFlowRegistrationExtensions.CreateFactoryMap(provider);
        catalog.Keys.ShouldBe([MappingComponentDefinition.Types.Mapper]);
    }

    [Fact]
    public async Task Canonical_host_resolves_engine_and_maps_json_values()
    {
        JsonElement? observedInput = null;
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, resultType) =>
            {
                resultType.ShouldBe(typeof(JsonElement));
                observedInput = context.Variables["input"].ShouldBeOfType<JsonElement>();
                return JsonSerializer.SerializeToElement(new
                {
                    mapped = observedInput.Value.GetProperty("value").GetString()
                });
            });
        var value = JsonSerializer.SerializeToElement(new { value = "input" });
        var request = FlowMessage.Create(value);

        await WithNodeAsync(
            engine,
            async (ports, _) =>
            {
                var resultReceive = ports.ReceiveAsync<JsonElement>(Output, Timeout);
                var eventReceive = ports.ReceiveAsync<global::FluxFlow.Data.FlowValue>(Events, Timeout);

                (await ports.SendAsync(Input, request)).IsAccepted.ShouldBeTrue();

                var response = (await resultReceive).Message.ShouldNotBeNull();
                response.IsError.ShouldBeFalse();
                response.Value.GetProperty("mapped").GetString().ShouldBe("input");
                response.CorrelationId.ShouldBe(request.CorrelationId);
                response.TraceId.ShouldBe(request.TraceId);
                response.CausationId.ShouldBe(request.MessageId);
                observedInput.ShouldBe(value);

                var @event = (await eventReceive).Message.ShouldNotBeNull();
                @event.CorrelationId.ShouldBe(request.CorrelationId);
                @event.ToFlowEvent().Name.ShouldBe("flow.mapper.succeeded");
            },
            Properties(
                ("expression", "map"),
                ("boundedCapacity", 8)));
    }

    [Fact]
    public async Task Canonical_host_uses_optional_context_factory_and_option_metadata()
    {
        var contextFactory = new RecordingContextFactory();
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, _) => context.Variables["mapped"]);

        await WithNodeAsync(
            engine,
            async (ports, _) =>
            {
                var receive = ports.ReceiveAsync<JsonElement>(Output, Timeout);
                var input = JsonSerializer.SerializeToElement("value");

                (await ports.SendAsync(Input, FlowMessage.Create(input))).IsAccepted.ShouldBeTrue();

                var result = (await receive).Message.ShouldNotBeNull();
                result.Value.GetString().ShouldBe("custom:value");
                contextFactory.Input.ShouldBe(input);
                contextFactory.Context.ShouldNotBeNull().Options.ExpressionName
                    .ShouldBe("custom-map");
                contextFactory.Context.InputType.ShouldBe(typeof(JsonElement));
                contextFactory.Context.OutputType.ShouldBe(typeof(JsonElement));
            },
            Properties(
                ("expression", "map"),
                ("expressionName", "custom-map"),
                ("inputType", "app.input"),
                ("outputType", "app.output")),
            contextFactory);
    }

    [Fact]
    public async Task Typed_mapper_definition_writes_canonical_capacity_and_binds_runtime_options()
    {
        var builder = new ApplicationDefinitionBuilder();
        var engineResource = builder.AddResource<IFlowExpressionEngine>(
            "engine",
            "host.expression");
        var contextResource = builder.AddResource<IMappingContextFactory>(
            "contextFactory",
            "host.mapping-context");
        var mapperHandle = builder.AddWorkflow("main").AddMapper("node", mapper =>
        {
            mapper.Expression = "map";
            mapper.BoundedCapacity = 3;
            mapper.Engine = engineResource;
            mapper.ContextFactory = contextResource;
        });
        mapperHandle.Output.DiscardUnmatched();
        mapperHandle.Events.DiscardUnmatched();
        var definition = builder.Build();
        var component = definition.Definition.Workflows["main"].Components["node"];
        var contextFactory = new RecordingContextFactory();
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, _) => context.Variables["mapped"]);

        component.Type.ShouldBe(MappingComponentDefinition.Types.Mapper);
        component.Properties[MappingComponentDefinition.Options.BoundedCapacity]
            .GetInt32().ShouldBe(3);
        component.Properties.ContainsKey("BoundedCapacity").ShouldBeFalse();

        await using var host = await CanonicalApplicationTestHost.StartAsync(
            definition,
            services => services.AddMapping(),
            registerResources: context =>
            {
                context.Services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                    engineResource.Address,
                    engine);
                context.Services.AddExternalFluxFlowResource<IMappingContextFactory>(
                    contextResource.Address,
                    contextFactory);
            });
        host.StartResult.Succeeded.ShouldBeTrue();
        var ports = host.GetRequiredPorts();
        var receive = ports.ReceiveAsync<JsonElement>(Output, Timeout);

        (await ports.SendAsync(
                Input,
                FlowMessage.Create(JsonSerializer.SerializeToElement("value"))))
            .IsAccepted.ShouldBeTrue();

        (await receive).Message.ShouldNotBeNull().Value.GetString()
            .ShouldBe("custom:value");
        contextFactory.Context.ShouldNotBeNull().Options.BoundedCapacity.ShouldBe(3);
    }

    [Fact]
    public async Task Canonical_host_emits_failures_as_normal_results_and_continues()
    {
        var calls = 0;
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    throw new InvalidOperationException("invalid value");
                return context.Variables["input"];
            });

        await WithNodeAsync(
            engine,
            async (ports, _) =>
            {
                var firstReceive = ports.ReceiveAsync<JsonElement>(Output, Timeout);
                var invalid = JsonSerializer.SerializeToElement("invalid");
                (await ports.SendAsync(Input, FlowMessage.Create(invalid))).IsAccepted.ShouldBeTrue();
                var failure = (await firstReceive).Message.ShouldNotBeNull();
                failure.IsError.ShouldBeTrue();
                failure.Error.ShouldNotBeNull().Code.ShouldBe("mapping.mapper_failed");

                var secondReceive = ports.ReceiveAsync<JsonElement>(Output, Timeout);
                var valid = JsonSerializer.SerializeToElement("valid");
                (await ports.SendAsync(Input, FlowMessage.Create(valid))).IsAccepted.ShouldBeTrue();
                var success = (await secondReceive).Message.ShouldNotBeNull();
                success.IsError.ShouldBeFalse();
                success.Value.ShouldBe(valid);
            },
            Properties(("expression", "map")));
    }

    [Fact]
    public async Task Missing_engine_resource_reference_surfaces_preparation_failure()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                MappingComponentDefinition.Types.Mapper,
                Properties(("expression", "map"))),
            registry => registry.AddMapping());

        AssertPreparationFailure(host, MappingComponentDefinition.Resources.Engine);
    }

    [Fact]
    public async Task Invalid_mapper_configuration_surfaces_preparation_failure()
    {
        await using var host = await StartHostAsync(
            new RecordingExpressionEngine(),
            Properties(
                ("expression", "map"),
                ("boundedCapacity", 0)));

        AssertPreparationFailure(host, "Mapper capacity must be positive");
    }

    private static async Task WithNodeAsync(
        IFlowExpressionEngine engine,
        Func<ApplicationPorts, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?> properties,
        IMappingContextFactory? contextFactory = null,
        TimeProvider? clock = null)
    {
        await using var host = await StartHostAsync(engine, properties, contextFactory, clock);
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
    }

    private static ValueTask<CanonicalApplicationTestHost> StartHostAsync(
        IFlowExpressionEngine engine,
        IReadOnlyDictionary<string, object?> properties,
        IMappingContextFactory? contextFactory = null,
        TimeProvider? clock = null)
    {
        var componentProperties = properties.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal);
        componentProperties[MappingComponentDefinition.Resources.Engine] = "Resources.engine";
        var resources = new List<string> { "engine" };
        if (contextFactory is not null)
        {
            componentProperties[MappingComponentDefinition.Resources.ContextFactory] =
                "Resources.contextFactory";
            resources.Add("contextFactory");
        }
        if (clock is not null)
        {
            componentProperties[MappingComponentDefinition.Resources.Clock] = "Resources.clock";
            resources.Add("clock");
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                MappingComponentDefinition.Types.Mapper,
                componentProperties,
                resources, discardUnmatched: ["Output", "Events"]),
            registry => registry.AddMapping(),
            registerResources: context =>
            {
                context.Services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                    ApplicationAddress.Resource("engine"),
                    engine);
                if (contextFactory is not null)
                {
                    context.Services.AddExternalFluxFlowResource<IMappingContextFactory>(
                        ApplicationAddress.Resource("contextFactory"),
                        contextFactory);
                }
                if (clock is not null)
                {
                    context.Services.AddExternalFluxFlowResource<TimeProvider>(
                        ApplicationAddress.Resource("clock"),
                        clock);
                }
            });
    }

    private static void AssertPreparationFailure(
        CanonicalApplicationTestHost host,
        string expectedMessage)
    {
        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        host.StartResult.Update.Diagnostics.ShouldContain(failure =>
            failure.Stage == ApplicationUpdateStage.ComponentPreparation &&
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!.Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private sealed class RecordingContextFactory : IMappingContextFactory
    {
        public JsonElement? Input { get; private set; }

        public MappingNodeContext? Context { get; private set; }

        public FlowMapContext Create(object? input, MappingNodeContext context)
        {
            Input = input.ShouldBeOfType<JsonElement>();
            Context = context;
            return new FlowMapContext
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = Input,
                    ["value"] = Input,
                    ["mapped"] = JsonSerializer.SerializeToElement($"custom:{Input.Value.GetString()}")
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
