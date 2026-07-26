using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Validation.Composition;
using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting.DependencyInjection;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Validation.Composition.Tests;

public sealed class ValidationCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "node", ValidationCompositionPortNames.Input);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", ValidationCompositionPortNames.Output);

    [Fact]
    public void RegisterJsonSchemaValidator_registers_only_the_canonical_contract()
    {
        var registry = new CompositionNodeRegistry().RegisterJsonSchemaValidator();

        var validator = registry.Registrations[ValidationCompositionNodeTypes.JsonSchemaValidator];
        validator.Inputs.Keys.ShouldBe([ValidationCompositionPortNames.Input]);
        validator.Outputs.Keys.ShouldBe([
            ValidationCompositionPortNames.Output,
            CompositionComponentEvents.PortName
        ], ignoreOrder: false);
        validator.Inputs[ValidationCompositionPortNames.Input].MessageType.ShouldBe(
            typeof(JsonElement));
        validator.Outputs[ValidationCompositionPortNames.Output].MessageType.ShouldBe(
            typeof(JsonSchemaValidationResult));
        typeof(ValidationCompositionNodeRegistryExtensions).GetMethods()
            .ShouldNotContain(static method => method.IsGenericMethodDefinition);
    }

    [Fact]
    public void RegisterJsonSchemaValidator_supports_explicit_canonical_component_types()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterJsonSchemaValidator("json.validate.primary")
            .RegisterJsonSchemaValidator("json.validate.secondary");

        registry.Registrations.Keys.ShouldBe([
            "json.validate.primary",
            "json.validate.secondary"
        ], ignoreOrder: false);
        registry.Registrations.Values.ShouldAllBe(registration =>
            registration.Inputs[ValidationCompositionPortNames.Input].MessageType ==
                typeof(JsonElement) &&
            registration.Outputs[ValidationCompositionPortNames.Output].MessageType ==
                typeof(JsonSchemaValidationResult));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_canonical_metadata()
    {
        var metadata = DesignMetadata();

        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Type.ShouldBe(new ComponentType(ValidationCompositionNodeTypes.JsonSchemaValidator));
        metadata.DisplayName?.Value.ShouldBe("JSON Schema Validator");
        metadata.Category.ShouldBe(new ComponentCategory("Validation"));
        metadata.PreferredNodeName.ShouldBe(new ComponentPreferredNodeName("validate"));
        metadata.SuggestedEditorWidth.ShouldBe(460);
        metadata.Options.Select(option => (option.Name.Value, option.Kind)).ShouldBe([
            ("schema", OptionValueKind.Json),
            ("schemaPath", OptionValueKind.Text),
            ("schemaId", OptionValueKind.Text),
            ("inputType", OptionValueKind.Text),
            ("valueSelector", OptionValueKind.Text),
            ("boundedCapacity", OptionValueKind.Number)
        ]);
        metadata.Options.Single(option => option.Name.Value == "valueSelector")
            .DefaultValue.ShouldBe(JsonSchemaValidatorOptions.DefaultValueSelector);
        metadata.Options.Single(option => option.Name.Value == "boundedCapacity")
            .Min.ShouldBe(1);
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == ValidationCompositionResourceNames.Selector ||
            option.Name.Value == ValidationCompositionResourceNames.Clock ||
            option.Name.Value == "payloadSelector");
        metadata.Resources.Select(resource => (
            resource.Name.Value,
            resource.Order,
            resource.IsRequired,
            resource.ValueType?.Value)).ShouldBe([
            (ValidationCompositionResourceNames.Selector, 0, false, nameof(IJsonSchemaValueSelector)),
            (ValidationCompositionResourceNames.Clock, 1, false, nameof(TimeProvider))
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
            (ValidationCompositionPortNames.Input, PortDirection.Input, 0, true, nameof(JsonElement)),
            (ValidationCompositionPortNames.Output, PortDirection.Output, 1, true, nameof(JsonSchemaValidationResult))
        ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_option_hints()
    {
        var options = DesignMetadata().Options.ToDictionary(
            option => option.Name.Value,
            StringComparer.Ordinal);

        AssertOptionHints(options["schema"], "Schema", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Json);
        AssertOptionHints(options["schemaPath"], "Schema", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["schemaId"], "Diagnostics", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(options["inputType"], "Type Metadata", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            options["valueSelector"],
            "Selection",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text,
            relatedResource: ValidationCompositionResourceNames.Selector);
        AssertOptionHints(options["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public void Design_metadata_provider_describes_resource_picker_hints()
    {
        var resources = DesignMetadata().Resources.ToDictionary(
            resource => resource.Name.Value,
            StringComparer.Ordinal);

        AssertResourceHints(
            resources[ValidationCompositionResourceNames.Selector],
            ResourceDesignMetadataAttributeValues.Selector,
            "Resources.{name}");
        AssertResourceHints(
            resources[ValidationCompositionResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "Resources.{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentDesignMetadataCatalog.FromProviders([
            new ValidationComponentDesignMetadataProvider()
        ]);

        catalog.TryGet(
            new ComponentType(ValidationCompositionNodeTypes.JsonSchemaValidator),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull().Type.ShouldBe(
            new ComponentType(ValidationCompositionNodeTypes.JsonSchemaValidator));
    }

    [Fact]
    public async Task Canonical_host_emits_valid_and_invalid_normal_results()
    {
        await WithNodeAsync(
            async (ports, _) =>
            {
                var validReceive = ports.ReceiveAsync<JsonSchemaValidationResult>(Output, Timeout);
                var validInput = Order("A-001", 10L);
                (await ports.SendAsync(Input, FlowMessage.Create(validInput)))
                    .IsAccepted.ShouldBeTrue();
                var valid = (await validReceive).Message.ShouldNotBeNull();
                valid.IsError.ShouldBeFalse();
                valid.Value.IsValid.ShouldBeTrue();
                valid.Value.Input.GetRawText().ShouldBe(validInput.GetRawText());
                valid.Value.SchemaId.ShouldBe("orders");

                var invalidReceive = ports.ReceiveAsync<JsonSchemaValidationResult>(Output, Timeout);
                var invalidInput = Order("A-002", "wrong");
                (await ports.SendAsync(Input, FlowMessage.Create(invalidInput)))
                    .IsAccepted.ShouldBeTrue();
                var invalid = (await invalidReceive).Message.ShouldNotBeNull();
                invalid.IsError.ShouldBeFalse();
                invalid.Value.IsValid.ShouldBeFalse();
                invalid.Value.Input.GetRawText().ShouldBe(invalidInput.GetRawText());
                invalid.Value.Issues.ShouldNotBeEmpty();
            },
            Properties(
                ("schema", OrderSchemaJson()),
                ("schemaId", "orders"),
                ("boundedCapacity", 8)));
    }

    [Fact]
    public async Task Canonical_host_uses_selector_and_clock_resources()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-18T19:00:00Z");
        var selector = new BodySelector();
        await WithNodeAsync(
            async (ports, _) =>
            {
                var receive = ports.ReceiveAsync<JsonSchemaValidationResult>(Output, Timeout);
                var body = Order("A-003", 30L);
                var message = JsonSerializer.SerializeToElement(new { body });

                (await ports.SendAsync(Input, FlowMessage.Create(message)))
                    .IsAccepted.ShouldBeTrue();

                var result = (await receive).Message.ShouldNotBeNull().Value;
                selector.Calls.ShouldBe(1);
                selector.LastValueSelector.ShouldBe("body");
                result.Timestamp.ShouldBe(timestamp);
                result.Input.GetRawText().ShouldBe(message.GetRawText());
                result.Value.GetRawText().ShouldBe(body.GetRawText());
            },
            Properties(
                ("schema", OrderSchemaJson()),
                ("valueSelector", "body"),
                ("boundedCapacity", 8)),
            selector,
            new FakeTimeProvider(timestamp));
    }

    [Fact]
    public async Task Canonical_host_loads_schema_path_during_preparation()
    {
        var schemaPath = Path.Combine(
            Path.GetTempPath(),
            $"fluxflow-composition-schema-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(schemaPath, OrderSchemaJson().GetRawText());
        try
        {
            await WithNodeAsync(
                async (ports, _) =>
                {
                    var receive = ports.ReceiveAsync<JsonSchemaValidationResult>(Output, Timeout);
                    (await ports.SendAsync(
                            Input,
                            FlowMessage.Create(Order("A-004", 40L))))
                        .IsAccepted.ShouldBeTrue();

                    (await receive).Message.ShouldNotBeNull().Value.IsValid.ShouldBeTrue();
                },
                Properties(
                    ("schemaPath", schemaPath),
                    ("boundedCapacity", 8)));
        }
        finally
        {
            File.Delete(schemaPath);
        }
    }

    [Fact]
    public async Task Missing_schema_surfaces_preparation_failure()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(ValidationCompositionNodeTypes.JsonSchemaValidator),
            registry => registry.RegisterJsonSchemaValidator());

        AssertPreparationFailure(host, "schema");
    }

    [Theory]
    [InlineData("boundedCapacity", 0, "positive")]
    public async Task Invalid_options_surface_preparation_failure(
        string optionName,
        object optionValue,
        string expectedMessage)
    {
        var properties = Properties(
            ("schema", StringSchemaJson()),
            (optionName, optionValue));
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                ValidationCompositionNodeTypes.JsonSchemaValidator,
                properties),
            registry => registry.RegisterJsonSchemaValidator());

        AssertPreparationFailure(host, expectedMessage);
    }

    private static ComponentDesignMetadata DesignMetadata()
        => new ValidationComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

    private static async Task WithNodeAsync(
        Func<ApplicationPortRuntime, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?> properties,
        IJsonSchemaValueSelector? selector = null,
        TimeProvider? clock = null)
    {
        await using var host = await StartHostAsync(properties, selector, clock);
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
    }

    private static ValueTask<CanonicalApplicationTestHost> StartHostAsync(
        IReadOnlyDictionary<string, object?> properties,
        IJsonSchemaValueSelector? selector = null,
        TimeProvider? clock = null)
    {
        var componentProperties = properties.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal);
        var resources = new List<string>();
        if (selector is not null)
        {
            componentProperties[ValidationCompositionResourceNames.Selector] =
                "Resources.selector";
            resources.Add("selector");
        }
        if (clock is not null)
        {
            componentProperties[ValidationCompositionResourceNames.Clock] =
                "Resources.clock";
            resources.Add("clock");
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                ValidationCompositionNodeTypes.JsonSchemaValidator,
                componentProperties,
                resources),
            registry => registry.RegisterJsonSchemaValidator(),
            configureRuntimeServices: context =>
            {
                if (selector is not null)
                {
                    context.Services.AddExternalFluxFlowResource<IJsonSchemaValueSelector>(
                        ApplicationAddress.Resource("selector"),
                        selector);
                }
                if (clock is not null)
                {
                    context.Services.AddExternalFluxFlowResource<TimeProvider>(
                        ApplicationAddress.Resource("clock"),
                        clock);
                }
            });
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

    private static JsonElement Order(string id, object total)
        => JsonSerializer.SerializeToElement(new { id, total });

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
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString().Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private sealed class BodySelector : IJsonSchemaValueSelector
    {
        public int Calls { get; private set; }

        public string? LastValueSelector { get; private set; }

        public JsonElement Select(JsonElement input, JsonSchemaValidatorContext context)
        {
            Calls++;
            LastValueSelector = context.ValueSelector;
            return input.GetProperty("body");
        }
    }
}
