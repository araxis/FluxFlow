using System.Text.Json;
using FluxFlow.Components.Assertions.Composition;
using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
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
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "node", AssertionsComponentPortNames.Input);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", AssertionsComponentPortNames.Output);

    [Fact]
    public void AddAssertionsComponents_registers_only_the_canonical_contract()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddAssertionsComponents());

        var assertion = registry.Components[AssertionsComponentTypes.Assert];
        assertion.Inputs.Keys.ShouldBe([AssertionsComponentPortNames.Input]);
        assertion.Outputs.Keys.ShouldBe([
            AssertionsComponentPortNames.Output,
            ComponentEvents.PortName
        ], ignoreOrder: false);
        assertion.Inputs[AssertionsComponentPortNames.Input].MessageType.ShouldBe(
            typeof(JsonElement));
        assertion.Outputs[AssertionsComponentPortNames.Output].MessageType.ShouldBe(
            typeof(AssertionResult<JsonElement>));
        typeof(AssertionsServiceCollectionExtensions).GetMethods()
            .ShouldNotContain(static method => method.IsGenericMethodDefinition);
    }

    [Fact]
    public void AddAssertionsComponents_is_idempotent()
    {
        var catalog = ComponentCatalogTestHost.Create(services =>
        {
            services.AddAssertionsComponents();
            services.AddAssertionsComponents();
        });

        catalog.Components.Keys.ShouldBe([AssertionsComponentTypes.Assert]);
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_canonical_metadata()
    {
        var metadata = DesignMetadata();

        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Type.ShouldBe(new ComponentType(AssertionsComponentTypes.Assert));
        metadata.DisplayName?.Value.ShouldBe("Assertion");
        metadata.Category.ShouldBe(new ComponentCategory("Assertions"));
        metadata.PreferredNodeName.ShouldBe(new ComponentPreferredNodeName("assert"));
        metadata.SuggestedEditorWidth.ShouldBe(420);
        metadata.Options.Select(option => (option.Name.Value, option.Kind)).ShouldBe([
            ("expression", OptionValueKind.Expression),
            ("expressionId", OptionValueKind.Text),
            ("expressionName", OptionValueKind.Text),
            ("inputType", OptionValueKind.Text),
            ("boundedCapacity", OptionValueKind.Number),
            ("description", OptionValueKind.Text),
            ("failureMessage", OptionValueKind.Text)
        ]);
        metadata.Options.Single(option => option.Name.Value == "expression")
            .IsRequired.ShouldBeTrue();
        metadata.Options.Single(option => option.Name.Value == "boundedCapacity")
            .Min.ShouldBe(1);
        metadata.Options.Single(option => option.Name.Value == "description")
            .DefaultValue.ShouldBe(AssertionOptions.DefaultDescription);
        metadata.Options.Single(option => option.Name.Value == "failureMessage")
            .DefaultValue.ShouldBe(AssertionOptions.DefaultFailureMessage);
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == AssertionsComponentResourceNames.Engine ||
            option.Name.Value == AssertionsComponentResourceNames.ContextFactory ||
            option.Name.Value == AssertionsComponentResourceNames.Clock ||
            option.Name.Value == "emitPassedInput" ||
            option.Name.Value == "emitFailedInput");
        metadata.Attributes.ShouldNotContain(attribute =>
            attribute.Key.Value == "omittedOptions" ||
            attribute.Key.Value == "omittedOptionsReason");
        metadata.Resources.Select(resource => (
            resource.Name.Value,
            resource.Order,
            resource.IsRequired,
            resource.ValueType?.Value)).ShouldBe([
            (AssertionsComponentResourceNames.Engine, 0, true, nameof(IFlowExpressionEngine)),
            (AssertionsComponentResourceNames.ContextFactory, 1, false, "IFlowMapContextFactory<JsonElement>"),
            (AssertionsComponentResourceNames.Clock, 2, false, nameof(TimeProvider))
        ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_canonical_ports()
    {
        DesignMetadata().Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.IsPrimary,
            port.ValueType?.Value)).ShouldBe([
            (AssertionsComponentPortNames.Input, PortDirection.Input, 0, true, nameof(JsonElement)),
            (AssertionsComponentPortNames.Output, PortDirection.Output, 1, true, "AssertionResult<JsonElement>")
        ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_option_hints()
    {
        var options = DesignMetadata().Options.ToDictionary(
            option => option.Name.Value,
            StringComparer.Ordinal);

        AssertOptionHints(
            options["expression"],
            "Assertions",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Expression,
            syntax: OptionDesignMetadataAttributeValues.Expression,
            relatedResource: AssertionsComponentResourceNames.Engine);
        AssertOptionHints(options["expressionId"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["expressionName"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["inputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(options["description"], "Results", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["failureMessage"], "Results", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
    }

    [Fact]
    public void Design_metadata_provider_describes_resource_picker_hints()
    {
        var resources = DesignMetadata().Resources.ToDictionary(
            resource => resource.Name.Value,
            StringComparer.Ordinal);

        AssertResourceHints(
            resources[AssertionsComponentResourceNames.Engine],
            ResourceDesignMetadataAttributeValues.ExpressionEngine,
            "Resources.{name}");
        AssertResourceHints(
            resources[AssertionsComponentResourceNames.ContextFactory],
            ResourceDesignMetadataAttributeValues.ContextFactory,
            "Resources.{name}");
        AssertResourceHints(
            resources[AssertionsComponentResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "Resources.{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentCatalogTestHost.CreateDesignMetadataCatalog(
            static services => services.AddAssertionsComponents());

        catalog.TryGet(
            new ComponentType(AssertionsComponentTypes.Assert),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull().Type.ShouldBe(
            new ComponentType(AssertionsComponentTypes.Assert));
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
                passed.Value.Input.ShouldBe(passedInput);
                passed.Value.Description.ShouldBe("score-check");

                var failedReceive = ports.ReceiveAsync<AssertionResult<JsonElement>>(Output, Timeout);
                var failedInput = JsonSerializer.SerializeToElement(3L);
                (await ports.SendAsync(Input, FlowMessage.Create(failedInput)))
                    .IsAccepted.ShouldBeTrue();
                var failed = (await failedReceive).Message.ShouldNotBeNull();
                failed.IsError.ShouldBeFalse();
                failed.Value.Input.ShouldBe(failedInput);
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
                result.Input.ShouldBe(input);
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
                AssertionsComponentTypes.Assert,
                Properties(("expression", "pass"))),
            registry => registry.AddAssertionsComponents());

        AssertPreparationFailure(host, AssertionsComponentResourceNames.Engine);
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

    private static ComponentDesignMetadata DesignMetadata()
        => new AssertionsComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

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
        componentProperties[AssertionsComponentResourceNames.Engine] = "Resources.engine";
        var resources = new List<string> { "engine" };
        if (contextFactory is not null)
        {
            componentProperties[AssertionsComponentResourceNames.ContextFactory] =
                "Resources.contextFactory";
            resources.Add("contextFactory");
        }
        if (clock is not null)
        {
            componentProperties[AssertionsComponentResourceNames.Clock] = "Resources.clock";
            resources.Add("clock");
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                AssertionsComponentTypes.Assert,
                componentProperties,
                resources),
            registry => registry.AddAssertionsComponents(),
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
            option.Attributes.ContainsKey(
                new ComponentAttributeName(OptionDesignMetadataAttributeNames.Editor))
                .ShouldBeFalse();
        }
        else
        {
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Editor)
                .ShouldBe(editor);
        }

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
