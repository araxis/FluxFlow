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
        ApplicationAddress.WorkflowPort("main", "node", AssertionsComponentDefinition.Ports.Input);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", AssertionsComponentDefinition.Ports.Output);

    [Fact]
    public void AddAssertions_registers_only_the_canonical_contract()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddFluxFlowComponents().AddAssertions());

        var assertion = registry.Components[AssertionsComponentDefinition.Types.Assertion];
        assertion.Inputs.Keys.ShouldBe([AssertionsComponentDefinition.Ports.Input]);
        assertion.Outputs.Keys.ShouldBe([
            AssertionsComponentDefinition.Ports.Output,
            ComponentEvents.PortName
        ], ignoreOrder: false);
        assertion.Inputs[AssertionsComponentDefinition.Ports.Input].MessageType.ShouldBe(
            typeof(JsonElement));
        assertion.Outputs[AssertionsComponentDefinition.Ports.Output].MessageType.ShouldBe(
            typeof(AssertionResult<JsonElement>));
        assertion.Options.Values.Select(option => (
            option.Name,
            option.ValueType,
            option.IsRequired)).ShouldBe([
            (AssertionsComponentDefinition.Options.Expression, typeof(string), true),
            (AssertionsComponentDefinition.Options.ExpressionId, typeof(string), false),
            (AssertionsComponentDefinition.Options.ExpressionName, typeof(string), false),
            (AssertionsComponentDefinition.Options.InputType, typeof(string), false),
            (AssertionsComponentDefinition.Options.BoundedCapacity, typeof(int), false),
            (AssertionsComponentDefinition.Options.Description, typeof(string), false),
            (AssertionsComponentDefinition.Options.FailureMessage, typeof(string), false)
        ], ignoreOrder: true);
        assertion.Resources.Values.Select(resource => (
            resource.Name,
            resource.ServiceType,
            resource.IsRequired)).ShouldBe([
            (AssertionsComponentDefinition.Resources.Engine, typeof(IFlowExpressionEngine), true),
            (AssertionsComponentDefinition.Resources.ContextFactory, typeof(IFlowMapContextFactory<JsonElement>), false),
            (AssertionsComponentDefinition.Resources.Clock, typeof(TimeProvider), false)
        ], ignoreOrder: true);
        typeof(AssertionsServiceCollectionExtensions).GetMethods()
            .ShouldNotContain(static method => method.IsGenericMethodDefinition);
    }

    [Fact]
    public void AddAssertions_is_idempotent()
    {
        var catalog = ComponentCatalogTestHost.Create(services =>
        {
            services.AddFluxFlowComponents().AddAssertions();
            services.AddFluxFlowComponents().AddAssertions();
        });

        catalog.Components.Keys.ShouldBe([AssertionsComponentDefinition.Types.Assertion]);
    }

    [Fact]
    public void Design_declaration_returns_valid_canonical_metadata()
    {
        var metadata = DesignMetadata();

        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Type.ShouldBe(new ComponentType(AssertionsComponentDefinition.Types.Assertion));
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
            ("failureMessage", OptionValueKind.Text),
            ("processing", OptionValueKind.Text)
        ]);
        metadata.Options.Single(option => option.Name.Value == "expression")
            .IsRequired.ShouldBeTrue();
        metadata.Options.Single(option => option.Name.Value == "description")
            .DefaultValue.ShouldBe(AssertionOptions.DefaultDescription);
        metadata.Options.Single(option => option.Name.Value == "failureMessage")
            .DefaultValue.ShouldBe(AssertionOptions.DefaultFailureMessage);
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == AssertionsComponentDefinition.Resources.Engine ||
            option.Name.Value == AssertionsComponentDefinition.Resources.ContextFactory ||
            option.Name.Value == AssertionsComponentDefinition.Resources.Clock ||
            option.Name.Value == "emitPassedInput" ||
            option.Name.Value == "emitFailedInput");
        metadata.Resources.Select(resource => (
            resource.Name.Value,
            resource.Order,
            resource.IsRequired,
            resource.ValueType?.Value)).ShouldBe([
            (AssertionsComponentDefinition.Resources.Engine, 0, true, nameof(IFlowExpressionEngine)),
            (AssertionsComponentDefinition.Resources.ContextFactory, 1, false, nameof(IFlowMapContextFactory<JsonElement>)),
            (AssertionsComponentDefinition.Resources.Clock, 2, false, nameof(TimeProvider)),
            ("processing", int.MaxValue, false, "CompositionProcessingProfile")
        ]);
    }

    [Fact]
    public void Design_declaration_describes_canonical_ports()
    {
        DesignMetadata().Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.IsPrimary,
            port.ValueType?.Value)).ShouldBe([
            (AssertionsComponentDefinition.Ports.Input, PortDirection.Input, 0, true, nameof(JsonElement)),
            (AssertionsComponentDefinition.Ports.Output, PortDirection.Output, 1, true, "AssertionResult<JsonElement>"),
            (ComponentEvents.PortName, PortDirection.Output, int.MaxValue, false, nameof(ComponentEvent))
        ]);
    }

    [Fact]
    public void Design_declaration_describes_option_hints()
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
            relatedResource: AssertionsComponentDefinition.Resources.Engine);
        AssertOptionHints(options["expressionId"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["expressionName"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["inputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["description"], "Results", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["failureMessage"], "Results", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            options["processing"],
            "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text,
            relatedResource: "processing");
    }

    [Fact]
    public void Design_declaration_describes_resource_picker_hints()
    {
        var resources = DesignMetadata().Resources.ToDictionary(
            resource => resource.Name.Value,
            StringComparer.Ordinal);

        AssertResourceHints(
            resources[AssertionsComponentDefinition.Resources.Engine],
            ResourceDesignMetadataAttributeValues.ExpressionEngine,
            "Resources.{name}");
        AssertResourceHints(
            resources[AssertionsComponentDefinition.Resources.ContextFactory],
            ResourceDesignMetadataAttributeValues.ContextFactory,
            "Resources.{name}");
        AssertResourceHints(
            resources[AssertionsComponentDefinition.Resources.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "Resources.{name}");
    }

    [Fact]
    public void Design_declaration_loads_into_catalog()
    {
        var catalog = ComponentCatalogTestHost.CreateDesignMetadataCatalog(
            static services => services.AddFluxFlowComponents().AddAssertions());

        catalog.TryGet(
            new ComponentType(AssertionsComponentDefinition.Types.Assertion),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull().Type.ShouldBe(
            new ComponentType(AssertionsComponentDefinition.Types.Assertion));
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
                AssertionsComponentDefinition.Types.Assertion,
                Properties(("expression", "pass"))),
            registry => registry.AddFluxFlowComponents().AddAssertions());

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

    private static ComponentDesignMetadata DesignMetadata()
        => ComponentCatalogTestHost.CreateDesignMetadataCatalog(
                static services => services.AddFluxFlowComponents().AddAssertions())
            .All
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
                resources),
            registry => registry.AddFluxFlowComponents().AddAssertions(),
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
