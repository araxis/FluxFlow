using System.Text;
using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Serialization.Composition.Tests;

public sealed class SerializationServiceCollectionExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        SerializationComponentDefinition.Ports.Input);
    private static readonly ApplicationAddress Output = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        SerializationComponentDefinition.Ports.Output);
    private static readonly ApplicationAddress Events = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        ComponentEvents.PortName);

    [Fact]
    public void AddSerializationComponents_registers_canonical_metadata()
    {
        var registry = ComponentCatalogTestHost.Create(AddSerializationComponents);

        AssertMetadata<FlowContent, JsonElement>(registry, SerializationComponentDefinition.Types.JsonParse);
        AssertMetadata<JsonElement, FlowContent>(registry, SerializationComponentDefinition.Types.JsonStringify);
        AssertMetadata<string, FlowContent>(registry, SerializationComponentDefinition.Types.TextEncode);
        AssertMetadata<FlowContent, string>(registry, SerializationComponentDefinition.Types.TextDecode);
        AssertMetadata<FlowContent, string>(registry, SerializationComponentDefinition.Types.Base64Encode);
        AssertMetadata<string, FlowContent>(registry, SerializationComponentDefinition.Types.Base64Decode);
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_serialization_metadata()
    {
        var metadata = DesignMetadataByType();

        metadata.Keys.ShouldBe([
            SerializationComponentDefinition.Types.JsonParse,
            SerializationComponentDefinition.Types.JsonStringify,
            SerializationComponentDefinition.Types.TextEncode,
            SerializationComponentDefinition.Types.TextDecode,
            SerializationComponentDefinition.Types.Base64Encode,
            SerializationComponentDefinition.Types.Base64Decode
        ], ignoreOrder: false);

        foreach (var item in metadata.Values)
        {
            ComponentDesignMetadataValidator.Validate(item).ShouldBeEmpty();
            item.Category.ShouldBe(new ComponentCategory("Serialization"));
            item.SuggestedEditorWidth.ShouldBe(420);
            item.Options.ShouldNotContain(option =>
                option.Name.Value == SerializationComponentDefinition.Resources.Clock);
            AssertClockResource(item);
        }
    }

    [Fact]
    public void Design_metadata_provider_describes_canonical_ports()
    {
        var metadata = DesignMetadataByType();

        AssertDesignPorts(metadata[SerializationComponentDefinition.Types.JsonParse],
            nameof(FlowContent), nameof(JsonElement));
        AssertDesignPorts(metadata[SerializationComponentDefinition.Types.JsonStringify],
            nameof(JsonElement), nameof(FlowContent));
        AssertDesignPorts(metadata[SerializationComponentDefinition.Types.TextEncode],
            nameof(String), nameof(FlowContent));
        AssertDesignPorts(metadata[SerializationComponentDefinition.Types.TextDecode],
            nameof(FlowContent), nameof(String));
        AssertDesignPorts(metadata[SerializationComponentDefinition.Types.Base64Encode],
            nameof(FlowContent), nameof(String));
        AssertDesignPorts(metadata[SerializationComponentDefinition.Types.Base64Decode],
            nameof(String), nameof(FlowContent));
    }

    [Fact]
    public void Design_metadata_provider_describes_shared_options_and_hints()
    {
        foreach (var item in DesignMetadataByType().Values)
        {
            AssertSharedOptions(item);
            var options = OptionsByName(item);
            AssertOptionHints(options["boundedCapacity"], "Runtime",
                OptionDesignMetadataAttributeValues.Advanced,
                OptionDesignMetadataAttributeValues.Number);
            AssertOptionHints(options["defaultEncoding"], "Encoding",
                OptionDesignMetadataAttributeValues.Advanced,
                OptionDesignMetadataAttributeValues.Text);
            AssertOptionHints(options["maxInputBytes"], "Runtime",
                OptionDesignMetadataAttributeValues.Advanced,
                OptionDesignMetadataAttributeValues.Number);
            AssertOptionHints(options["maxOutputBytes"], "Runtime",
                OptionDesignMetadataAttributeValues.Advanced,
                OptionDesignMetadataAttributeValues.Number);
            AssertOptionHints(options["writeIndented"], "JSON",
                OptionDesignMetadataAttributeValues.Advanced);
            AssertOptionHints(options["allowTrailingCommas"], "JSON",
                OptionDesignMetadataAttributeValues.Advanced);
            AssertOptionHints(options["skipComments"], "JSON",
                OptionDesignMetadataAttributeValues.Advanced);
        }
    }

    [Fact]
    public void Design_metadata_provider_describes_host_owned_clock_picker()
    {
        foreach (var item in DesignMetadataByType().Values)
        {
            var resource = item.Resources.ShouldHaveSingleItem();
            AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.Ownership)
                .ShouldBe(ResourceDesignMetadataAttributeValues.HostOwned);
            AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.PickerKind)
                .ShouldBe(ResourceDesignMetadataAttributeValues.Clock);
            AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.KeyPattern)
                .ShouldBe("Resources.{name}");
        }
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentCatalogTestHost.CreateDesignMetadataCatalog(
            static services => services.AddSerializationComponents());

        catalog.All.Count.ShouldBe(6);
        catalog.TryGet(
            new ComponentType(SerializationComponentDefinition.Types.JsonParse),
            out var parse).ShouldBeTrue();
        parse.ShouldNotBeNull().DisplayName?.Value.ShouldBe("JSON Parse");
        catalog.TryGet(
            new ComponentType(SerializationComponentDefinition.Types.Base64Decode),
            out var decode).ShouldBeTrue();
        decode.ShouldNotBeNull().DisplayName?.Value.ShouldBe("Base64 Decode");
    }

    [Fact]
    public async Task Hosted_json_parse_binds_options_and_returns_json()
    {
        var result = await RunNodeAsync<FlowContent, JsonElement>(
            SerializationComponentDefinition.Types.JsonParse,
            FlowContent.FromBytes(
                Encoding.UTF8.GetBytes("""{"name":"sample",}"""),
                "application/json"),
            Properties(("allowTrailingCommas", true)));

        result.CorrelationId.ShouldBe(new CorrelationId("json.parse"));
        result.IsError.ShouldBeFalse();
        result.Value.GetProperty("name").GetString().ShouldBe("sample");
    }

    [Fact]
    public async Task Hosted_json_stringify_returns_json_content()
    {
        var result = await RunNodeAsync<JsonElement, FlowContent>(
            SerializationComponentDefinition.Types.JsonStringify,
            JsonSerializer.SerializeToElement(new { ok = true }));

        var content = result.Value;
        content.ContentType.ShouldBe("application/json");
        Encoding.UTF8.GetString(content.Bytes.AsSpan())
            .ShouldBe("""{"ok":true}""");
    }

    [Fact]
    public async Task Hosted_text_encode_returns_text_content()
    {
        var result = await RunNodeAsync<string, FlowContent>(
            SerializationComponentDefinition.Types.TextEncode,
            "hello");

        var content = result.Value;
        content.ContentType.ShouldBe("text/plain");
        content.Encoding.ShouldBe("utf-8");
        Encoding.UTF8.GetString(content.Bytes.AsSpan()).ShouldBe("hello");
    }

    [Fact]
    public async Task Hosted_text_decode_binds_encoding_and_skips_preamble()
    {
        var encoding = Encoding.Unicode;
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("hello")).ToArray();
        var result = await RunNodeAsync<FlowContent, string>(
            SerializationComponentDefinition.Types.TextDecode,
            FlowContent.FromBytes(bytes, "text/plain"),
            Properties(("defaultEncoding", "utf-16")));

        result.Value.ShouldBe("hello");
    }

    [Fact]
    public async Task Hosted_base64_encode_returns_string_value()
    {
        var result = await RunNodeAsync<FlowContent, string>(
            SerializationComponentDefinition.Types.Base64Encode,
            FlowContent.FromBytes(Encoding.UTF8.GetBytes("hello")));

        result.Value.ShouldBe("aGVsbG8=");
    }

    [Fact]
    public async Task Hosted_base64_decode_returns_binary_content()
    {
        var result = await RunNodeAsync<string, FlowContent>(
            SerializationComponentDefinition.Types.Base64Decode,
            "aGVsbG8=");

        var content = result.Value;
        content.ContentType.ShouldBe("application/octet-stream");
        content.Bytes.AsSpan()
            .SequenceEqual(Encoding.UTF8.GetBytes("hello")).ShouldBeTrue();
    }

    [Fact]
    public async Task Hosted_node_uses_optional_keyed_clock_for_events()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-18T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        await WithNodeAsync<FlowContent, JsonElement>(
            SerializationComponentDefinition.Types.JsonParse,
            async (ports, _) =>
            {
                var receive = ports.ReceiveAsync<ComponentEvent>(Events, Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(
                    FlowContent.FromBytes(
                        Encoding.UTF8.GetBytes("{}"),
                        "application/json")))).IsAccepted.ShouldBeTrue();

                var @event = (await receive).Message.ShouldNotBeNull().Value;
                @event.Name.ShouldBe(SerializationDiagnosticNames.JsonParsed);
                @event.Timestamp.ShouldBe(timestamp);
            },
            clock: clock);
    }

    [Fact]
    public async Task Hosted_expected_failure_uses_output_and_continues()
    {
        await WithNodeAsync<FlowContent, JsonElement>(
            SerializationComponentDefinition.Types.JsonParse,
            async (ports, _) =>
            {
                var bad = FlowMessage.Create(
                    FlowContent.FromBytes(Encoding.UTF8.GetBytes("{"), "application/json"),
                    new CorrelationId("bad"));
                var good = FlowMessage.Create(
                    FlowContent.FromBytes(Encoding.UTF8.GetBytes("{}"), "application/json"),
                    new CorrelationId("good"));

                var failureReceive = ports.ReceiveAsync<JsonElement>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, bad)).IsAccepted.ShouldBeTrue();
                var failure = (await failureReceive).Message.ShouldNotBeNull();

                var successReceive = ports.ReceiveAsync<JsonElement>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, good)).IsAccepted.ShouldBeTrue();
                var success = (await successReceive).Message.ShouldNotBeNull();
                failure.CorrelationId.ShouldBe(bad.CorrelationId);
                failure.Error.ShouldNotBeNull().Code
                    .ShouldBe(SerializationErrorCodeNames.JsonParseFailed);
                success.CorrelationId.ShouldBe(good.CorrelationId);
                success.IsError.ShouldBeFalse();
            });
    }

    [Fact]
    public async Task Invalid_configuration_surfaces_factory_diagnostic()
    {
        await using var host = await StartHostAsync(
            SerializationComponentDefinition.Types.TextEncode,
            Properties(("boundedCapacity", 0)));

        AssertPreparationFailure(host, "boundedCapacity");
    }

    private static void AddSerializationComponents(IServiceCollection services)
        => services.AddSerializationComponents();

    private static void AssertMetadata<TInput, TOutput>(
        ComponentCatalog registry,
        string nodeType)
    {
        var registration = registry.Components[nodeType];
        registration.Inputs[SerializationComponentDefinition.Ports.Input].MessageType
            .ShouldBe(typeof(TInput));
        registration.Outputs[SerializationComponentDefinition.Ports.Output].MessageType
            .ShouldBe(typeof(TOutput));
    }

    private static IReadOnlyDictionary<string, ComponentDesignMetadata> DesignMetadataByType()
        => SerializationComponentDefinition.CreateMetadata()
            .ToDictionary(metadata => metadata.Type.Value, StringComparer.Ordinal);

    private static Dictionary<string, OptionDesignMetadata> OptionsByName(
        ComponentDesignMetadata metadata)
        => metadata.Options.ToDictionary(option => option.Name.Value, StringComparer.Ordinal);

    private static void AssertDesignPorts(
        ComponentDesignMetadata metadata,
        string inputType,
        string outputType)
    {
        metadata.Ports.Count.ShouldBe(2);
        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(SerializationComponentDefinition.Ports.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType?.Value.ShouldBe(inputType);
        input.IsPrimary.ShouldBeTrue();
        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(SerializationComponentDefinition.Ports.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(1);
        output.ValueType?.Value.ShouldBe(outputType);
        output.IsPrimary.ShouldBeTrue();
    }

    private static void AssertSharedOptions(ComponentDesignMetadata metadata)
    {
        var defaults = new SerializationNodeOptions();
        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "boundedCapacity",
            "defaultEncoding",
            "maxInputBytes",
            "maxOutputBytes",
            "writeIndented",
            "allowTrailingCommas",
            "skipComments"
        ], ignoreOrder: false);
        AssertOption(metadata, "boundedCapacity", OptionValueKind.Number, defaults.BoundedCapacity, 1);
        AssertOption(metadata, "defaultEncoding", OptionValueKind.Text, defaults.DefaultEncoding);
        AssertOption(metadata, "maxInputBytes", OptionValueKind.Number, defaults.MaxInputBytes, 1);
        AssertOption(metadata, "maxOutputBytes", OptionValueKind.Number, defaults.MaxOutputBytes, 1);
        AssertOption(metadata, "writeIndented", OptionValueKind.Boolean, defaults.WriteIndented);
        AssertOption(metadata, "allowTrailingCommas", OptionValueKind.Boolean, defaults.AllowTrailingCommas);
        AssertOption(metadata, "skipComments", OptionValueKind.Boolean, defaults.SkipComments);
    }

    private static void AssertOption(
        ComponentDesignMetadata metadata,
        string name,
        OptionValueKind kind,
        object? defaultValue,
        double? min = null)
    {
        var option = metadata.Options.Single(option => option.Name.Value == name);
        option.Kind.ShouldBe(kind);
        option.DefaultValue.ShouldBe(defaultValue);
        option.Min.ShouldBe(min);
    }

    private static void AssertOptionHints(
        OptionDesignMetadata option,
        string section,
        string importance,
        string? editor = null)
    {
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Section)
            .ShouldBe(section);
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Importance)
            .ShouldBe(importance);
        var editorName = new ComponentAttributeName(OptionDesignMetadataAttributeNames.Editor);
        if (editor is null)
            option.Attributes.ContainsKey(editorName).ShouldBeFalse();
        else
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Editor).ShouldBe(editor);
        option.Attributes.ContainsKey(new ComponentAttributeName(
            OptionDesignMetadataAttributeNames.Syntax)).ShouldBeFalse();
        option.Attributes.ContainsKey(new ComponentAttributeName(
            OptionDesignMetadataAttributeNames.RelatedResource)).ShouldBeFalse();
    }

    private static void AssertClockResource(ComponentDesignMetadata metadata)
    {
        var resource = metadata.Resources.ShouldHaveSingleItem();
        resource.Name.Value.ShouldBe(SerializationComponentDefinition.Resources.Clock);
        resource.DisplayName?.Value.ShouldBe("Clock");
        resource.Order.ShouldBe(0);
        resource.IsRequired.ShouldBeFalse();
        resource.ValueType?.Value.ShouldBe(nameof(TimeProvider));
    }

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;

    private static async Task<FlowMessage<TOutput>> RunNodeAsync<TInput, TOutput>(
        string nodeType,
        TInput inputValue,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        FlowMessage<TOutput>? result = null;
        await WithNodeAsync<TInput, TOutput>(
            nodeType,
            async (ports, _) =>
            {
                var receive = ports.ReceiveAsync<TOutput>(Output, Timeout);
                var message = FlowMessage.Create(inputValue, new CorrelationId(nodeType));
                (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();
                result = (await receive).Message.ShouldNotBeNull();
            },
            properties);
        return result.ShouldNotBeNull();
    }

    private static async Task WithNodeAsync<TInput, TOutput>(
        string nodeType,
        Func<ApplicationPorts, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?>? properties = null,
        TimeProvider? clock = null)
    {
        await using var host = await StartHostAsync(nodeType, properties, clock);
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
    }

    private static ValueTask<CanonicalApplicationTestHost> StartHostAsync(
        string nodeType,
        IReadOnlyDictionary<string, object?>? properties = null,
        TimeProvider? clock = null)
    {
        var componentProperties = properties?.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal) ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        IReadOnlyList<string>? resources = null;
        if (clock is not null)
        {
            componentProperties[SerializationComponentDefinition.Resources.Clock] =
                "Resources.fixed";
            resources = ["fixed"];
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                nodeType,
                componentProperties,
                resources,
                componentName: "node"),
            AddSerializationComponents,
            registerResources: context =>
            {
                if (clock is not null)
                {
                    context.Services.AddExternalFluxFlowResource<TimeProvider>(
                        ApplicationAddress.Resource("fixed"),
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
}
