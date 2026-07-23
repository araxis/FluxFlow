using FluxFlow.Components.Assertions.Composition;
using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Assertions.Composition.Tests;

public sealed class AssertionsCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "node", AssertionsCompositionPortNames.Input);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", AssertionsCompositionPortNames.Output);

    [Fact]
    public void RegisterAssertion_registers_only_the_canonical_contract()
    {
        var registry = new CompositionNodeRegistry().RegisterAssertion();

        var assertion = registry.Registrations[AssertionsCompositionNodeTypes.Assert];
        assertion.Inputs.Keys.ShouldBe([AssertionsCompositionPortNames.Input]);
        assertion.Outputs.Keys.ShouldBe([
            AssertionsCompositionPortNames.Output,
            CompositionComponentEvents.PortName
        ], ignoreOrder: false);
        assertion.Inputs[AssertionsCompositionPortNames.Input].MessageType.ShouldBe(
            typeof(FlowValue));
        assertion.Outputs[AssertionsCompositionPortNames.Output].MessageType.ShouldBe(
            typeof(FlowResult<FlowValueAssertionResult>));
        typeof(AssertionsCompositionNodeRegistryExtensions).GetMethods()
            .ShouldNotContain(static method => method.IsGenericMethodDefinition);
    }

    [Fact]
    public void RegisterAssertion_supports_explicit_canonical_component_types()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterAssertion("data.assert.primary")
            .RegisterAssertion("data.assert.secondary");

        registry.Registrations.Keys.ShouldBe([
            "data.assert.primary",
            "data.assert.secondary"
        ], ignoreOrder: false);
        registry.Registrations.Values.ShouldAllBe(registration =>
            registration.Inputs[AssertionsCompositionPortNames.Input].MessageType ==
                typeof(FlowValue) &&
            registration.Outputs[AssertionsCompositionPortNames.Output].MessageType ==
                typeof(FlowResult<FlowValueAssertionResult>));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_canonical_metadata()
    {
        var metadata = DesignMetadata();

        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Type.ShouldBe(new ComponentType(AssertionsCompositionNodeTypes.Assert));
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
            .DefaultValue.ShouldBe(FlowValueAssertionOptions.DefaultDescription);
        metadata.Options.Single(option => option.Name.Value == "failureMessage")
            .DefaultValue.ShouldBe(FlowValueAssertionOptions.DefaultFailureMessage);
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == AssertionsCompositionResourceNames.Engine ||
            option.Name.Value == AssertionsCompositionResourceNames.ContextFactory ||
            option.Name.Value == AssertionsCompositionResourceNames.Clock ||
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
            (AssertionsCompositionResourceNames.Engine, 0, true, nameof(IFlowExpressionEngine)),
            (AssertionsCompositionResourceNames.ContextFactory, 1, false, "IFlowMapContextFactory<FlowValue>"),
            (AssertionsCompositionResourceNames.Clock, 2, false, nameof(TimeProvider))
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
            (AssertionsCompositionPortNames.Input, PortDirection.Input, 0, true, nameof(FlowValue)),
            (AssertionsCompositionPortNames.Output, PortDirection.Output, 1, true, "FlowResult<FlowValueAssertionResult>")
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
            relatedResource: AssertionsCompositionResourceNames.Engine);
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
            resources[AssertionsCompositionResourceNames.Engine],
            ResourceDesignMetadataAttributeValues.ExpressionEngine,
            "Resources.{name}");
        AssertResourceHints(
            resources[AssertionsCompositionResourceNames.ContextFactory],
            ResourceDesignMetadataAttributeValues.ContextFactory,
            "Resources.{name}");
        AssertResourceHints(
            resources[AssertionsCompositionResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "Resources.{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentDesignMetadataCatalog.FromProviders([
            new AssertionsComponentDesignMetadataProvider()
        ]);

        catalog.TryGet(
            new ComponentType(AssertionsCompositionNodeTypes.Assert),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull().Type.ShouldBe(
            new ComponentType(AssertionsCompositionNodeTypes.Assert));
    }

    [Fact]
    public async Task Canonical_host_emits_passed_and_failed_normal_results()
    {
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, _) =>
                ((FlowValue)context.Variables["input"]!).GetInteger() >= 10);
        await WithNodeAsync(
            engine,
            async (ports, _) =>
            {
                var passedReceive = ports.ReceiveAsync<
                    FlowResult<FlowValueAssertionResult>>(Output, Timeout);
                var passedInput = FlowValue.From(12L);
                (await ports.SendAsync(Input, FlowMessage.Create(passedInput)))
                    .IsAccepted.ShouldBeTrue();
                var passed = (await passedReceive).Message.ShouldNotBeNull().Payload;
                passed.Kind.ShouldBe(AssertionResultKinds.Passed);
                passed.IsError.ShouldBeFalse();
                passed.Value.ShouldNotBeNull().Input.ShouldBeSameAs(passedInput);
                passed.Value.Description.ShouldBe("score-check");

                var failedReceive = ports.ReceiveAsync<
                    FlowResult<FlowValueAssertionResult>>(Output, Timeout);
                var failedInput = FlowValue.From(3L);
                (await ports.SendAsync(Input, FlowMessage.Create(failedInput)))
                    .IsAccepted.ShouldBeTrue();
                var failed = (await failedReceive).Message.ShouldNotBeNull().Payload;
                failed.Kind.ShouldBe(AssertionResultKinds.Failed);
                failed.IsError.ShouldBeFalse();
                failed.Value.ShouldNotBeNull().Input.ShouldBeSameAs(failedInput);
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
                var receive = ports.ReceiveAsync<
                    FlowResult<FlowValueAssertionResult>>(Output, Timeout);
                var input = FlowValue.From("value");

                (await ports.SendAsync(Input, FlowMessage.Create(input)))
                    .IsAccepted.ShouldBeTrue();

                var result = (await receive).Message.ShouldNotBeNull()
                    .Payload.Value.ShouldNotBeNull();
                contextFactory.Input.ShouldBeSameAs(input);
                result.Input.ShouldBeSameAs(input);
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
                var firstReceive = ports.ReceiveAsync<
                    FlowResult<FlowValueAssertionResult>>(Output, Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(FlowValue.From("first"))))
                    .IsAccepted.ShouldBeTrue();
                var failure = (await firstReceive).Message.ShouldNotBeNull().Payload;
                failure.Kind.ShouldBe(AssertionResultKinds.EvaluationFailed);
                failure.Error.ShouldNotBeNull().Code
                    .ShouldBe(AssertionErrorCodeNames.EvaluationFailed);

                var secondReceive = ports.ReceiveAsync<
                    FlowResult<FlowValueAssertionResult>>(Output, Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(FlowValue.From("second"))))
                    .IsAccepted.ShouldBeTrue();
                var success = (await secondReceive).Message.ShouldNotBeNull().Payload;
                success.Kind.ShouldBe(AssertionResultKinds.Passed);
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
                AssertionsCompositionNodeTypes.Assert,
                Properties(("expression", "pass"))),
            registry => registry.RegisterAssertion());

        AssertPreparationFailure(host, AssertionsCompositionResourceNames.Engine);
    }

    [Theory]
    [InlineData("expression", " ", "expression")]
    [InlineData("inputType", " ", "inputType")]
    [InlineData("boundedCapacity", 0, "boundedCapacity")]
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
        Func<ApplicationPortRuntime, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?> properties,
        IFlowMapContextFactory<FlowValue>? contextFactory = null,
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
        IFlowMapContextFactory<FlowValue>? contextFactory = null,
        TimeProvider? clock = null)
    {
        var componentProperties = properties.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal);
        componentProperties[AssertionsCompositionResourceNames.Engine] = "Resources.engine";
        var resources = new List<string> { "engine" };
        if (contextFactory is not null)
        {
            componentProperties[AssertionsCompositionResourceNames.ContextFactory] =
                "Resources.contextFactory";
            resources.Add("contextFactory");
        }
        if (clock is not null)
        {
            componentProperties[AssertionsCompositionResourceNames.Clock] = "Resources.clock";
            resources.Add("clock");
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                AssertionsCompositionNodeTypes.Assert,
                componentProperties,
                resources),
            registry => registry.RegisterAssertion(),
            configureRuntimeServices: context =>
            {
                context.Services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                    ApplicationAddress.Resource("engine"),
                    engine);
                if (contextFactory is not null)
                {
                    context.Services.AddExternalFluxFlowResource<
                        IFlowMapContextFactory<FlowValue>>(
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
        host.StartResult.Update!.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        host.StartResult.Update.Failures.ShouldContain(failure =>
            failure.Stage == ApplicationRevisionFailureStage.Preparation &&
            failure.Error.Details.GetObject()["exceptionMessage"].GetString().Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private sealed class RecordingContextFactory : IFlowMapContextFactory<FlowValue>
    {
        public FlowValue? Input { get; private set; }

        public FlowMapContext Create(FlowValue input)
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
