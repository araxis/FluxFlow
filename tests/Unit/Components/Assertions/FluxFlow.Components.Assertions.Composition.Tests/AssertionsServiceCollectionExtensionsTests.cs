using FluxFlow.Composition.Definition;
using FluxFlow.Json.Model;
using System.Text.Json;
using FluxFlow.Components.Assertions.Composition;
using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Assertions.Composition.Tests;

public sealed class AssertionsServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Terminal_fixture_preserves_options_and_requires_explicit_output_intent(bool terminal)
    {
        var properties = new Dictionary<string, object?> { ["expression"] = "pass", ["engine"] = "Resources.engine" };
        var outputs = new[] { "Output", "Events" };
        var definition = SingleComponent(AssertionsComponentDefinition.Types.Assertion, properties, ["engine"],
            discardUnmatched: terminal ? outputs : null);
        outputs[0] = "Changed";
        var services = new ServiceCollection().AddAssertions();
        services.AddExternalFluxFlowResource<IFlowExpressionEngine>(ApplicationAddress.Resource("engine"), new RecordingExpressionEngine());
        await using var provider = services.BuildServiceProvider();
        var instance = await FluxFlowRegistrationExtensions.CreateFactoryMap(provider)[AssertionsComponentDefinition.Types.Assertion](
            new ComponentActivationContext(provider, "main", "node", definition.Workflows["main"].Components["node"]));
        var compiled = new FluxFlow.Composition.Links.ApplicationLinkCompiler(
            new Dictionary<string, ComponentInstance> { ["main.node"] = instance }).Compile(definition);
        await instance.DisposeAsync();

        compiled.Diagnostics.ShouldBeEmpty();
        compiled.DiscardedOutputs.Select(address => address.Value).ShouldBe(terminal
            ? new[] { "main.node.Events", "main.node.Output" } : []);
        compiled.Links.ShouldBeEmpty();
        var component = definition.Workflows["main"].Components["node"];
        component.Properties["expression"].GetString().ShouldBe("pass");
        component.Properties["engine"].GetString().ShouldBe("Resources.engine");
        definition.Resources.Keys.ShouldBe(["engine"]);
        properties.Keys.ShouldBe(["expression", "engine"]);
        component.Properties.ContainsKey("DiscardUnmatched").ShouldBe(terminal);
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "node", AssertionsComponentDefinition.Ports.Input);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", AssertionsComponentDefinition.Ports.Output);

    [Fact]
    public async Task AddAssertions_declares_actual_canonical_ports()
    {
        var services = new ServiceCollection().AddAssertions();
        services.AddExternalFluxFlowResource<IFlowExpressionEngine>(ApplicationAddress.Resource("engine"), new RecordingExpressionEngine());
        await using var provider = services.BuildServiceProvider();
        var context = new ComponentActivationContext(provider, "main", "node",
            new ComponentDefinition(AssertionsComponentDefinition.Types.Assertion, new[] { new KeyValuePair<string, JsonElement>("expression", JsonSerializer.SerializeToElement("pass")), new KeyValuePair<string, JsonElement>("engine", JsonSerializer.SerializeToElement("Resources.engine")) }));
        var assertion = await FluxFlowRegistrationExtensions.CreateFactoryMap(provider)[AssertionsComponentDefinition.Types.Assertion](context);
        try
        {
            assertion.Inputs.Keys.ShouldBe([AssertionsComponentDefinition.Ports.Input]);
            assertion.Outputs.Keys.ShouldBe([
                AssertionsComponentDefinition.Ports.Output,
                "Events"
            ], ignoreOrder: false);
            assertion.InputMetadata[AssertionsComponentDefinition.Ports.Input].MessageType.ShouldBe(
                typeof(FlowValue));
            assertion.OutputMetadata[AssertionsComponentDefinition.Ports.Output].MessageType.ShouldBe(
                typeof(FlowValue));
            typeof(AssertionsServiceCollectionExtensions).GetMethods()
                .ShouldNotContain(static method => method.IsGenericMethodDefinition);
        }
        finally { await assertion.DisposeAsync(); }
    }

    [Fact]
    public void AddAssertions_is_idempotent()
    {
        using var provider = new ServiceCollection().AddAssertions().AddAssertions().BuildServiceProvider();
        var catalog = FluxFlowRegistrationExtensions.CreateFactoryMap(provider);
        catalog.Keys.ShouldBe([AssertionsComponentDefinition.Types.Assertion]);
    }

    [Fact]
    public async Task Canonical_host_emits_passed_and_failed_normal_results()
    {
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, _) =>
                ((JsonElement)context.Variables["input"]!).GetInt64() >= 10);
        await WithNodeAsync(
            engine,
            async (ports, _) =>
            {
                var passedReceive = ports.ReceiveAsync<AssertionResult<JsonElement>>(Output, Timeout);
                var passedInput = JsonSerializer.SerializeToElement(12L);
                (await ports.SendAsync(Input, FlowMessage.Create(passedInput)))
                    .IsAccepted.ShouldBeTrue();
                var passed = (await passedReceive).Message.ShouldNotBeNull();
                passed.IsError.ShouldBeFalse();
                passed.Value.Input.GetRawText().ShouldBe(passedInput.GetRawText());
                passed.Value.Description.ShouldBe("score-check");

                var failedReceive = ports.ReceiveAsync<AssertionResult<JsonElement>>(Output, Timeout);
                var failedInput = JsonSerializer.SerializeToElement(3L);
                (await ports.SendAsync(Input, FlowMessage.Create(failedInput)))
                    .IsAccepted.ShouldBeTrue();
                var failed = (await failedReceive).Message.ShouldNotBeNull();
                failed.IsError.ShouldBeFalse();
                failed.Value.Input.GetRawText().ShouldBe(failedInput.GetRawText());
                failed.Value.Message.ShouldBe("Score too low.");
            },
            Properties(
                ("expression", "score >= 10"),
                ("description", "score-check"),
                ("failureMessage", "Score too low."),
                ("boundedCapacity", 8)));
    }

    [Fact]
    public async Task Canonical_host_uses_context_factory_and_clock_resources()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-19T13:00:00Z");
        var contextFactory = new RecordingContextFactory();
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, resultType) =>
            {
                resultType.ShouldBe(typeof(bool));
                return context.Variables["passed"];
            });
        await WithNodeAsync(
            engine,
            async (ports, _) =>
            {
                var receive = ports.ReceiveAsync<AssertionResult<JsonElement>>(Output, Timeout);
                var input = JsonSerializer.SerializeToElement("value");

                (await ports.SendAsync(Input, FlowMessage.Create(input)))
                    .IsAccepted.ShouldBeTrue();

                var result = (await receive).Message.ShouldNotBeNull().Value;
                contextFactory.Input.ShouldBe(input);
                result.Input.GetRawText().ShouldBe(input.GetRawText());
                result.EvaluatedAt.ShouldBe(timestamp);
            },
            Properties(
                ("expression", "passed"),
                ("boundedCapacity", 8)),
            contextFactory,
            new FakeTimeProvider(timestamp));
    }

    [Fact]
    public async Task Canonical_host_emits_evaluation_failure_and_continues()
    {
        var calls = 0;
        var engine = new RecordingExpressionEngine(evaluate: (_, _, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("evaluation failed");
            return true;
        });
        await WithNodeAsync(
            engine,
            async (ports, _) =>
            {
                var firstReceive = ports.ReceiveAsync<AssertionResult<JsonElement>>(Output, Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(
                    JsonSerializer.SerializeToElement("first"))))
                    .IsAccepted.ShouldBeTrue();
                var failure = (await firstReceive).Message.ShouldNotBeNull();
                failure.Error.ShouldNotBeNull().Code
                    .ShouldBe(AssertionErrorCodeNames.EvaluationFailed);

                var secondReceive = ports.ReceiveAsync<AssertionResult<JsonElement>>(Output, Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(
                    JsonSerializer.SerializeToElement("second"))))
                    .IsAccepted.ShouldBeTrue();
                var success = (await secondReceive).Message.ShouldNotBeNull();
                success.IsError.ShouldBeFalse();
            },
            Properties(
                ("expression", "assert"),
                ("boundedCapacity", 8)));
    }

    [Fact]
    public async Task Missing_engine_resource_reference_surfaces_preparation_failure()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                AssertionsComponentDefinition.Types.Assertion,
                Properties(("expression", "pass"))),
            registry => registry.AddAssertions());

        AssertPreparationFailure(host, AssertionsComponentDefinition.Resources.Engine);
    }

    [Theory]
    [InlineData("expression", " ", "expression")]
    [InlineData("boundedCapacity", 0, "positive")]
    public async Task Invalid_options_surface_preparation_failure(
        string optionName,
        object optionValue,
        string expectedMessage)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["expression"] = "pass",
            [optionName] = optionValue
        };
        var engine = new RecordingExpressionEngine(evaluate: (_, _, _) => true);
        await using var host = await StartHostAsync(engine, properties);

        AssertPreparationFailure(host, expectedMessage);
    }

    private static async Task WithNodeAsync(
        IFlowExpressionEngine engine,
        Func<ApplicationPorts, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?> properties,
        IFlowMapContextFactory<JsonElement>? contextFactory = null,
        TimeProvider? clock = null)
    {
        await using var host = await StartHostAsync(
            engine,
            properties,
            contextFactory,
            clock);
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
    }

    private static ValueTask<CanonicalApplicationTestHost> StartHostAsync(
        IFlowExpressionEngine engine,
        IReadOnlyDictionary<string, object?> properties,
        IFlowMapContextFactory<JsonElement>? contextFactory = null,
        TimeProvider? clock = null)
    {
        var componentProperties = properties.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal);
        componentProperties[AssertionsComponentDefinition.Resources.Engine] = "Resources.engine";
        var resources = new List<string> { "engine" };
        if (contextFactory is not null)
        {
            componentProperties[AssertionsComponentDefinition.Resources.ContextFactory] =
                "Resources.contextFactory";
            resources.Add("contextFactory");
        }
        if (clock is not null)
        {
            componentProperties[AssertionsComponentDefinition.Resources.Clock] = "Resources.clock";
            resources.Add("clock");
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                AssertionsComponentDefinition.Types.Assertion,
                componentProperties,
                resources, discardUnmatched: ["Output", "Events"]),
            registry => registry.AddAssertions(),
            registerResources: context =>
            {
                context.Services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                    ApplicationAddress.Resource("engine"),
                    engine);
                if (contextFactory is not null)
                {
                    context.Services.AddExternalFluxFlowResource<
                        IFlowMapContextFactory<JsonElement>>(
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

    private sealed class RecordingContextFactory : IFlowMapContextFactory<JsonElement>
    {
        public JsonElement? Input { get; private set; }

        public FlowMapContext Create(JsonElement input)
        {
            Input = input;
            return new FlowMapContext
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["passed"] = true
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
