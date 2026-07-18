using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Serialization.Composition.Tests;

public sealed class SerializationCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Register_serialization_nodes_registers_canonical_metadata()
    {
        var registry = RegisterAll(new CompositionNodeRegistry());

        AssertMetadata<FlowContent, FlowValue>(registry, SerializationCompositionNodeTypes.JsonParse);
        AssertMetadata<FlowValue, FlowContent>(registry, SerializationCompositionNodeTypes.JsonStringify);
        AssertMetadata<FlowValue, FlowContent>(registry, SerializationCompositionNodeTypes.TextEncode);
        AssertMetadata<FlowContent, FlowValue>(registry, SerializationCompositionNodeTypes.TextDecode);
        AssertMetadata<FlowContent, FlowValue>(registry, SerializationCompositionNodeTypes.Base64Encode);
        AssertMetadata<FlowValue, FlowContent>(registry, SerializationCompositionNodeTypes.Base64Decode);
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_serialization_metadata()
    {
        var metadata = DesignMetadataByType();

        metadata.Keys.ShouldBe([
            SerializationCompositionNodeTypes.JsonParse,
            SerializationCompositionNodeTypes.JsonStringify,
            SerializationCompositionNodeTypes.TextEncode,
            SerializationCompositionNodeTypes.TextDecode,
            SerializationCompositionNodeTypes.Base64Encode,
            SerializationCompositionNodeTypes.Base64Decode
        ], ignoreOrder: false);

        foreach (var item in metadata.Values)
        {
            ComponentDesignMetadataValidator.Validate(item).ShouldBeEmpty();
            item.Category.ShouldBe(new ComponentCategory("Serialization"));
            item.SuggestedEditorWidth.ShouldBe(420);
            item.Options.ShouldNotContain(option =>
                option.Name.Value == SerializationCompositionResourceNames.Clock);
            AssertClockResource(item);
        }
    }

    [Fact]
    public void Design_metadata_provider_describes_canonical_ports()
    {
        var metadata = DesignMetadataByType();

        AssertDesignPorts(metadata[SerializationCompositionNodeTypes.JsonParse],
            nameof(FlowContent), "FlowResult<FlowValue>");
        AssertDesignPorts(metadata[SerializationCompositionNodeTypes.JsonStringify],
            nameof(FlowValue), "FlowResult<FlowContent>");
        AssertDesignPorts(metadata[SerializationCompositionNodeTypes.TextEncode],
            nameof(FlowValue), "FlowResult<FlowContent>");
        AssertDesignPorts(metadata[SerializationCompositionNodeTypes.TextDecode],
            nameof(FlowContent), "FlowResult<FlowValue>");
        AssertDesignPorts(metadata[SerializationCompositionNodeTypes.Base64Encode],
            nameof(FlowContent), "FlowResult<FlowValue>");
        AssertDesignPorts(metadata[SerializationCompositionNodeTypes.Base64Decode],
            nameof(FlowValue), "FlowResult<FlowContent>");
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
        var catalog = ComponentDesignMetadataCatalog.FromProviders(
            [new SerializationComponentDesignMetadataProvider()]);

        catalog.All.Count.ShouldBe(6);
        catalog.TryGet(
            new ComponentType(SerializationCompositionNodeTypes.JsonParse),
            out var parse).ShouldBeTrue();
        parse.ShouldNotBeNull().DisplayName?.Value.ShouldBe("JSON Parse");
        catalog.TryGet(
            new ComponentType(SerializationCompositionNodeTypes.Base64Decode),
            out var decode).ShouldBeTrue();
        decode.ShouldNotBeNull().DisplayName?.Value.ShouldBe("Base64 Decode");
    }

    [Fact]
    public async Task Hosted_json_parse_binds_options_and_returns_flow_value()
    {
        var result = await RunNodeAsync<FlowContent, FlowValue>(
            SerializationCompositionNodeTypes.JsonParse,
            registry => registry.RegisterJsonParse(),
            FlowContent.FromBytes(
                Encoding.UTF8.GetBytes("""{"name":"sample",}"""),
                "application/json"),
            node => node.Configure("allowTrailingCommas", true));

        result.CorrelationId.ShouldBe(new CorrelationId("json.parse"));
        result.Payload.IsError.ShouldBeFalse();
        result.Payload.Value.ShouldNotBeNull().GetObject()["name"]
            .GetString().ShouldBe("sample");
    }

    [Fact]
    public async Task Hosted_json_stringify_returns_json_content()
    {
        var result = await RunNodeAsync<FlowValue, FlowContent>(
            SerializationCompositionNodeTypes.JsonStringify,
            registry => registry.RegisterJsonStringify(),
            FlowValue.FromObject(new Dictionary<string, FlowValue>
            {
                ["ok"] = FlowValue.From(true)
            }));

        var content = result.Payload.Value.ShouldNotBeNull();
        content.ContentType.ShouldBe("application/json");
        Encoding.UTF8.GetString(content.OriginalBytes.AsSpan())
            .ShouldBe("""{"ok":true}""");
    }

    [Fact]
    public async Task Hosted_text_encode_returns_text_content()
    {
        var result = await RunNodeAsync<FlowValue, FlowContent>(
            SerializationCompositionNodeTypes.TextEncode,
            registry => registry.RegisterTextEncode(),
            FlowValue.From("hello"));

        var content = result.Payload.Value.ShouldNotBeNull();
        content.ContentType.ShouldBe("text/plain");
        content.Encoding.ShouldBe("utf-8");
        Encoding.UTF8.GetString(content.OriginalBytes.AsSpan()).ShouldBe("hello");
    }

    [Fact]
    public async Task Hosted_text_decode_binds_encoding_and_skips_preamble()
    {
        var encoding = Encoding.Unicode;
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("hello")).ToArray();
        var result = await RunNodeAsync<FlowContent, FlowValue>(
            SerializationCompositionNodeTypes.TextDecode,
            registry => registry.RegisterTextDecode(),
            FlowContent.FromBytes(bytes, "text/plain"),
            node => node.Configure("defaultEncoding", "utf-16"));

        result.Payload.Value.ShouldNotBeNull().GetString().ShouldBe("hello");
    }

    [Fact]
    public async Task Hosted_base64_encode_returns_string_value()
    {
        var result = await RunNodeAsync<FlowContent, FlowValue>(
            SerializationCompositionNodeTypes.Base64Encode,
            registry => registry.RegisterBase64Encode(),
            FlowContent.FromBytes(Encoding.UTF8.GetBytes("hello")));

        result.Payload.Value.ShouldNotBeNull().GetString().ShouldBe("aGVsbG8=");
    }

    [Fact]
    public async Task Hosted_base64_decode_returns_binary_content()
    {
        var result = await RunNodeAsync<FlowValue, FlowContent>(
            SerializationCompositionNodeTypes.Base64Decode,
            registry => registry.RegisterBase64Decode(),
            FlowValue.From("aGVsbG8="));

        var content = result.Payload.Value.ShouldNotBeNull();
        content.ContentType.ShouldBe("application/octet-stream");
        content.OriginalBytes.AsSpan()
            .SequenceEqual(Encoding.UTF8.GetBytes("hello")).ShouldBeTrue();
    }

    [Fact]
    public async Task Hosted_node_uses_optional_keyed_clock_for_events()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-18T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        await WithNodeAsync<FlowContent, FlowValue>(
            SerializationCompositionNodeTypes.JsonParse,
            registry => registry.RegisterJsonParse(),
            async (input, output, descriptor) =>
            {
                output.Source.LinkTo(DataflowBlock.NullTarget<
                    FlowMessage<FlowResult<FlowValue>>>());
                var events = Link(descriptor.Events.ShouldNotBeNull());
                await input.Target.SendAsync(FlowMessage.Create(
                    FlowContent.FromBytes(Encoding.UTF8.GetBytes("{}"), "application/json")));

                var @event = await events.ReceiveAsync().WaitAsync(Timeout);
                @event.Name.ShouldBe(SerializationDiagnosticNames.JsonParsed);
                @event.Timestamp.ShouldBe(timestamp);
            },
            node => node.Resource(SerializationCompositionResourceNames.Clock, "fixed"),
            services => services.AddKeyedSingleton<TimeProvider>("fixed", clock));
    }

    [Fact]
    public async Task Hosted_expected_failure_uses_output_and_continues()
    {
        await WithNodeAsync<FlowContent, FlowValue>(
            SerializationCompositionNodeTypes.JsonParse,
            registry => registry.RegisterJsonParse(),
            async (input, output, descriptor) =>
            {
                descriptor.Errors.ShouldBeNull();
                var results = Link(output.Source);
                var bad = FlowMessage.Create(
                    FlowContent.FromBytes(Encoding.UTF8.GetBytes("{"), "application/json"),
                    new CorrelationId("bad"));
                var good = FlowMessage.Create(
                    FlowContent.FromBytes(Encoding.UTF8.GetBytes("{}"), "application/json"),
                    new CorrelationId("good"));

                await input.Target.SendAsync(bad);
                await input.Target.SendAsync(good);

                var failure = await results.ReceiveAsync().WaitAsync(Timeout);
                var success = await results.ReceiveAsync().WaitAsync(Timeout);
                failure.CorrelationId.ShouldBe(bad.CorrelationId);
                failure.Payload.Error.ShouldNotBeNull().Code
                    .ShouldBe(SerializationErrorCodeNames.JsonParseFailed);
                success.CorrelationId.ShouldBe(good.CorrelationId);
                success.Payload.IsError.ShouldBeFalse();
            });
    }

    [Fact]
    public async Task Invalid_configuration_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "encode",
                    SerializationCompositionNodeTypes.TextEncode,
                    node => node.Configure("boundedCapacity", 0)))
                .Build())
            .RegisterNodes(registry => registry.RegisterTextEncode())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains("boundedCapacity", StringComparison.OrdinalIgnoreCase));
    }

    private static CompositionNodeRegistry RegisterAll(CompositionNodeRegistry registry)
        => registry
            .RegisterJsonParse()
            .RegisterJsonStringify()
            .RegisterTextEncode()
            .RegisterTextDecode()
            .RegisterBase64Encode()
            .RegisterBase64Decode();

    private static void AssertMetadata<TInput, TOutput>(
        CompositionNodeRegistry registry,
        string nodeType)
    {
        var registration = registry.Registrations[nodeType];
        registration.Inputs[SerializationCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(TInput));
        registration.Outputs[SerializationCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FlowResult<TOutput>));
    }

    private static IReadOnlyDictionary<string, ComponentDesignMetadata> DesignMetadataByType()
        => new SerializationComponentDesignMetadataProvider()
            .GetMetadata()
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
        input.Name.Value.ShouldBe(SerializationCompositionPortNames.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType?.Value.ShouldBe(inputType);
        input.IsPrimary.ShouldBeTrue();
        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(SerializationCompositionPortNames.Output);
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
        resource.Name.Value.ShouldBe(SerializationCompositionResourceNames.Clock);
        resource.DisplayName?.Value.ShouldBe("Clock");
        resource.Order.ShouldBe(0);
        resource.IsRequired.ShouldBeFalse();
        resource.ValueType?.Value.ShouldBe(nameof(TimeProvider));
    }

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;

    private static async Task<FlowMessage<FlowResult<TOutput>>> RunNodeAsync<TInput, TOutput>(
        string nodeType,
        Func<CompositionNodeRegistry, CompositionNodeRegistry> register,
        TInput inputValue,
        Action<NodeDefinitionBuilder>? configureNode = null)
    {
        FlowMessage<FlowResult<TOutput>>? result = null;
        await WithNodeAsync<TInput, TOutput>(
            nodeType,
            register,
            async (input, output, _) =>
            {
                var results = Link(output.Source);
                var message = FlowMessage.Create(inputValue, new CorrelationId(nodeType));
                (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();
                result = await results.ReceiveAsync().WaitAsync(Timeout);
            },
            configureNode);
        return result.ShouldNotBeNull();
    }

    private static async Task WithNodeAsync<TInput, TOutput>(
        string nodeType,
        Func<CompositionNodeRegistry, CompositionNodeRegistry> register,
        Func<
            CompositionInputPort<TInput>,
            CompositionOutputPort<FlowResult<TOutput>>,
            ComposedNode,
            Task> run,
        Action<NodeDefinitionBuilder>? configureNode = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node("node", nodeType, configureNode))
                .Build())
            .RegisterNodes(registry => register(registry))
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var descriptor = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem()
            .Descriptor;
        var input = descriptor.Inputs[SerializationCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<TInput>>();
        var output = descriptor.Outputs[SerializationCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<FlowResult<TOutput>>>();
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
}
