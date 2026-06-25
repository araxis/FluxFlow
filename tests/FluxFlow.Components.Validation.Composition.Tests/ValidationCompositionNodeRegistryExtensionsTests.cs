using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Validation.Composition;
using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Validation.Composition.Tests;

public sealed class ValidationCompositionNodeRegistryExtensionsTests
{
    [Fact]
    public void RegisterJsonSchemaValidator_registers_closed_validator_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterJsonSchemaValidator<InputMessage>();

        var validator =
            registry.Registrations[ValidationCompositionNodeTypes.JsonSchemaValidator];
        validator.Inputs[ValidationCompositionPortNames.Input].MessageType.ShouldBe(
            typeof(InputMessage));
        validator.Outputs[ValidationCompositionPortNames.Output].MessageType.ShouldBe(
            typeof(JsonSchemaValidationResult<InputMessage>));
        validator.Outputs[ValidationCompositionPortNames.Valid].MessageType.ShouldBe(
            typeof(InputMessage));
        validator.Outputs[ValidationCompositionPortNames.Invalid].MessageType.ShouldBe(
            typeof(InputMessage));
    }

    [Fact]
    public void RegisterJsonSchemaValidator_supports_multiple_custom_node_types()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterJsonSchemaValidator<InputMessage>("json.schema-validator.input")
            .RegisterJsonSchemaValidator<string>("json.schema-validator.string");

        registry.Registrations["json.schema-validator.input"]
            .Inputs[ValidationCompositionPortNames.Input].MessageType.ShouldBe(
                typeof(InputMessage));
        registry.Registrations["json.schema-validator.string"]
            .Inputs[ValidationCompositionPortNames.Input].MessageType.ShouldBe(
                typeof(string));
        registry.Registrations["json.schema-validator.input"]
            .Outputs[ValidationCompositionPortNames.Output].MessageType.ShouldBe(
                typeof(JsonSchemaValidationResult<InputMessage>));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_json_schema_validator_metadata()
    {
        var metadata = new ValidationComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Type.ShouldBe(new ComponentType(ValidationCompositionNodeTypes.JsonSchemaValidator));
        metadata.DisplayName.ShouldBe("JSON Schema Validator");
        metadata.Category.ShouldBe("Validation");
        metadata.PreferredNodeName.ShouldBe("validate");
        metadata.SuggestedEditorWidth.ShouldBe(460);
        metadata.Options.Select(option => (option.Name, option.Kind)).ShouldBe([
            ("schema", OptionValueKind.Json),
            ("schemaPath", OptionValueKind.Text),
            ("schemaId", OptionValueKind.Text),
            ("inputType", OptionValueKind.Text),
            ("valueSelector", OptionValueKind.Text),
            ("payloadSelector", OptionValueKind.Text),
            ("boundedCapacity", OptionValueKind.Number)
        ]);
        metadata.Options.Single(option => option.Name == "valueSelector")
            .DefaultValue.ShouldBe("input");
        metadata.Options.Single(option => option.Name == "boundedCapacity")
            .Min.ShouldBe(1);
        metadata.Options.Select(option => option.Name)
            .ShouldNotContain(ValidationCompositionResourceNames.Selector);
        metadata.Options.Select(option => option.Name)
            .ShouldNotContain(ValidationCompositionResourceNames.Clock);
        metadata.Resources.Select(resource => (
            resource.Name,
            resource.Order,
            resource.IsRequired,
            resource.ValueType)).ShouldBe([
            (ValidationCompositionResourceNames.Selector, 0, false, "IJsonSchemaValueSelector<TInput>"),
            (ValidationCompositionResourceNames.Clock, 1, false, nameof(TimeProvider))
        ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_json_schema_validator_ports()
    {
        var metadata = new ValidationComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

        metadata.Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.IsPrimary,
            port.ValueType)).ShouldBe([
            (ValidationCompositionPortNames.Input, PortDirection.Input, 0, true, "TInput"),
            (ValidationCompositionPortNames.Output, PortDirection.Output, 1, true, "JsonSchemaValidationResult<TInput>"),
            (ValidationCompositionPortNames.Valid, PortDirection.Output, 2, false, "TInput"),
            (ValidationCompositionPortNames.Invalid, PortDirection.Output, 3, false, "TInput")
        ]);
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var provider = new ValidationComponentDesignMetadataProvider();

        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.TryGet(
            new ComponentType(ValidationCompositionNodeTypes.JsonSchemaValidator),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull();
        metadata.Type.ShouldBe(new ComponentType(ValidationCompositionNodeTypes.JsonSchemaValidator));
    }

    [Fact]
    public async Task Hosted_validator_routes_valid_and_invalid_inputs()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "validate",
                    ValidationCompositionNodeTypes.JsonSchemaValidator,
                    node => node
                        .Configure("schema", OrderSchemaJson())
                        .Configure("schemaId", "orders")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry =>
                registry.RegisterJsonSchemaValidator<JsonElement>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var validatorNode = host.Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem();
        var input = validatorNode.Descriptor.Inputs[ValidationCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<JsonElement>>();
        var output = validatorNode.Descriptor.Outputs[ValidationCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<JsonSchemaValidationResult<JsonElement>>>();
        var valid = validatorNode.Descriptor.Outputs[ValidationCompositionPortNames.Valid]
            .ShouldBeOfType<CompositionOutputPort<JsonElement>>();
        var invalid = validatorNode.Descriptor.Outputs[ValidationCompositionPortNames.Invalid]
            .ShouldBeOfType<CompositionOutputPort<JsonElement>>();
        var results = new BufferBlock<FlowMessage<JsonSchemaValidationResult<JsonElement>>>();
        var validResults = new BufferBlock<FlowMessage<JsonElement>>();
        var invalidResults = new BufferBlock<FlowMessage<JsonElement>>();
        output.Source.LinkTo(results);
        valid.Source.LinkTo(validResults);
        invalid.Source.LinkTo(invalidResults);

        var accepted = FlowMessage.Create(
            JsonSerializer.SerializeToElement(new { id = "A-100", total = 125 }),
            new CorrelationId("valid-order"));
        var rejected = FlowMessage.Create(
            JsonSerializer.SerializeToElement(new { id = "A-101", total = "wrong" }),
            new CorrelationId("invalid-order"));

        (await input.Target.SendAsync(accepted)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        (await input.Target.SendAsync(rejected)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var acceptedResult = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        var rejectedResult = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        var routedValid = await validResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        var routedInvalid = await invalidResults.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        acceptedResult.CorrelationId.ShouldBe(new CorrelationId("valid-order"));
        acceptedResult.Payload.IsValid.ShouldBeTrue();
        acceptedResult.Payload.SchemaId.ShouldBe("orders");
        rejectedResult.CorrelationId.ShouldBe(new CorrelationId("invalid-order"));
        rejectedResult.Payload.IsValid.ShouldBeFalse();
        rejectedResult.Payload.Issues.ShouldNotBeEmpty();
        routedValid.CorrelationId.ShouldBe(new CorrelationId("valid-order"));
        routedValid.Payload.GetProperty("id").GetString().ShouldBe("A-100");
        routedInvalid.CorrelationId.ShouldBe(new CorrelationId("invalid-order"));
        routedInvalid.Payload.GetProperty("id").GetString().ShouldBe("A-101");
    }

    [Fact]
    public async Task Hosted_validator_binds_inline_schema_configuration()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "validate",
                    ValidationCompositionNodeTypes.JsonSchemaValidator,
                    node => node
                        .Configure("schema", StringSchemaJson())
                        .Configure("schemaId", "strings")
                        .Configure("payloadSelector", "body")
                        .Configure("inputType", "app.message")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry =>
                registry.RegisterJsonSchemaValidator<string>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var validatorNode = host.Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem();
        var input = validatorNode.Descriptor.Inputs[ValidationCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<string>>();
        var output = validatorNode.Descriptor.Outputs[ValidationCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<JsonSchemaValidationResult<string>>>();
        var results = new BufferBlock<FlowMessage<JsonSchemaValidationResult<string>>>();
        output.Source.LinkTo(results);

        (await input.Target.SendAsync(FlowMessage.Create("accepted"))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var result = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        result.Payload.IsValid.ShouldBeTrue();
        result.Payload.SchemaId.ShouldBe("strings");
        result.Payload.ValueSelector.ShouldBe("body");
    }

    [Fact]
    public async Task Hosted_validator_loads_schema_path_at_build_time()
    {
        var schemaPath = Path.Combine(
            Path.GetTempPath(),
            $"fluxflow-composition-schema-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(schemaPath, OrderSchemaJson().GetRawText());
        try
        {
            var services = new ServiceCollection();
            services
                .AddFluxFlowComposition(CompositionDefinitionBuilder
                    .Create()
                    .Workflow("main", workflow => workflow.Node(
                        "validate",
                        ValidationCompositionNodeTypes.JsonSchemaValidator,
                        node => node
                            .Configure("schemaPath", schemaPath)
                            .Configure("boundedCapacity", 8)))
                    .Build())
                .RegisterNodes(registry =>
                    registry.RegisterJsonSchemaValidator<string>())
                .Configure(options => options.StartRuntimeWithHost = false);

            await using var provider = services.BuildServiceProvider();
            var hostedService = provider.GetServices<IHostedService>()
                .ShouldHaveSingleItem();

            await hostedService.StartAsync(CancellationToken.None);

            var host = provider.GetRequiredService<ICompositionRuntimeHost>();
            var validatorNode = host.Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem();
            var input = validatorNode.Descriptor.Inputs[ValidationCompositionPortNames.Input]
                .ShouldBeOfType<CompositionInputPort<string>>();
            var valid = validatorNode.Descriptor.Outputs[ValidationCompositionPortNames.Valid]
                .ShouldBeOfType<CompositionOutputPort<string>>();
            var validResults = new BufferBlock<FlowMessage<string>>();
            valid.Source.LinkTo(validResults);

            (await input.Target.SendAsync(
                    FlowMessage.Create("""{"id":"A-200","total":200}"""))
                .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

            (await validResults.ReceiveAsync()
                .WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldContain("A-200");
        }
        finally
        {
            File.Delete(schemaPath);
        }
    }

    [Fact]
    public async Task Hosted_validator_uses_optional_keyed_selector()
    {
        var selector = new PayloadSelector();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IJsonSchemaValueSelector<InputMessage>>(
            "payload",
            selector);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "validate",
                    ValidationCompositionNodeTypes.JsonSchemaValidator,
                    node => node
                        .Resource(ValidationCompositionResourceNames.Selector, "payload")
                        .Configure("schema", OrderSchemaJson())
                        .Configure("valueSelector", "payload")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry =>
                registry.RegisterJsonSchemaValidator<InputMessage>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var validatorNode = host.Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem();
        var input = validatorNode.Descriptor.Inputs[ValidationCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<InputMessage>>();
        var output = validatorNode.Descriptor.Outputs[ValidationCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<JsonSchemaValidationResult<InputMessage>>>();
        var results = new BufferBlock<FlowMessage<JsonSchemaValidationResult<InputMessage>>>();
        output.Source.LinkTo(results);

        (await input.Target.SendAsync(FlowMessage.Create(
                new InputMessage("""{"id":"A-300","total":300}""")))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var result = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        selector.Calls.ShouldBe(1);
        selector.LastValueSelector.ShouldBe("payload");
        result.Payload.IsValid.ShouldBeTrue();
        result.Payload.ValueSelector.ShouldBe("payload");
    }

    [Fact]
    public async Task Hosted_validator_uses_optional_keyed_clock_for_results()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T13:00:00Z");
        var services = new ServiceCollection();
        services.AddKeyedSingleton<TimeProvider>(
            "fixed",
            new FakeTimeProvider(timestamp));
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "validate",
                    ValidationCompositionNodeTypes.JsonSchemaValidator,
                    node => node
                        .Resource(ValidationCompositionResourceNames.Clock, "fixed")
                        .Configure("schema", StringSchemaJson())
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry =>
                registry.RegisterJsonSchemaValidator<string>())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var validatorNode = host.Runtime.ShouldNotBeNull().Nodes.ShouldHaveSingleItem();
        var input = validatorNode.Descriptor.Inputs[ValidationCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<string>>();
        var output = validatorNode.Descriptor.Outputs[ValidationCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<JsonSchemaValidationResult<string>>>();
        var results = new BufferBlock<FlowMessage<JsonSchemaValidationResult<string>>>();
        output.Source.LinkTo(results);

        (await input.Target.SendAsync(FlowMessage.Create("accepted"))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var result = await results.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        result.Payload.Timestamp.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Missing_schema_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "validate",
                    ValidationCompositionNodeTypes.JsonSchemaValidator))
                .Build())
            .RegisterNodes(registry =>
                registry.RegisterJsonSchemaValidator<object>())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains("schema", StringComparison.OrdinalIgnoreCase));
    }

    private static JsonElement OrderSchemaJson()
        => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "id", "total" },
            properties = new
            {
                id = new { type = "string" },
                total = new { type = "number" }
            }
        });

    private static JsonElement StringSchemaJson()
        => JsonSerializer.SerializeToElement(new
        {
            type = "string",
            minLength = 1
        });

    private sealed record InputMessage(string Payload);

    private sealed class PayloadSelector : IJsonSchemaValueSelector<InputMessage>
    {
        public int Calls { get; private set; }

        public string? LastValueSelector { get; private set; }

        public object? Select(
            InputMessage input,
            JsonSchemaValidatorContext context)
        {
            Calls++;
            LastValueSelector = context.ValueSelector;
            using var document = JsonDocument.Parse(input.Payload);
            return document.RootElement.Clone();
        }
    }
}
