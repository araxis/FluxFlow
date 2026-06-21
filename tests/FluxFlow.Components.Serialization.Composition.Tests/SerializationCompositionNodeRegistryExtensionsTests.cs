using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Serialization;
using FluxFlow.Components.Serialization.Composition;
using FluxFlow.Components.Serialization.Contracts;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
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
    public void RegisterSerializationNodes_registers_request_result_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterJsonParse()
            .RegisterJsonStringify()
            .RegisterTextEncode()
            .RegisterTextDecode()
            .RegisterBase64Encode()
            .RegisterBase64Decode();

        AssertMetadata<JsonParseRequest, JsonParseResult>(
            registry,
            SerializationCompositionNodeTypes.JsonParse);
        AssertMetadata<JsonStringifyRequest, JsonStringifyResult>(
            registry,
            SerializationCompositionNodeTypes.JsonStringify);
        AssertMetadata<TextEncodeRequest, TextEncodeResult>(
            registry,
            SerializationCompositionNodeTypes.TextEncode);
        AssertMetadata<TextDecodeRequest, TextDecodeResult>(
            registry,
            SerializationCompositionNodeTypes.TextDecode);
        AssertMetadata<Base64EncodeRequest, Base64EncodeResult>(
            registry,
            SerializationCompositionNodeTypes.Base64Encode);
        AssertMetadata<Base64DecodeRequest, Base64DecodeResult>(
            registry,
            SerializationCompositionNodeTypes.Base64Decode);
    }

    [Fact]
    public async Task Hosted_json_parse_parses_text_and_preserves_correlation_id()
    {
        var result = await RunNodeAsync<JsonParseRequest, JsonParseResult>(
            SerializationCompositionNodeTypes.JsonParse,
            registry => registry.RegisterJsonParse(),
            new JsonParseRequest { Text = """{"name":"flux",}""" },
            node => node.Configure("allowTrailingCommas", true));

        result.CorrelationId.ShouldBe(new CorrelationId("json.parse"));
        result.Payload.Kind.ShouldBe(JsonValueKind.Object);
        result.Payload.Value.ShouldBeOfType<JsonObject>()["name"]!
            .GetValue<string>()
            .ShouldBe("flux");
    }

    [Fact]
    public async Task Hosted_json_stringify_serializes_value()
    {
        var result = await RunNodeAsync<JsonStringifyRequest, JsonStringifyResult>(
            SerializationCompositionNodeTypes.JsonStringify,
            registry => registry.RegisterJsonStringify(),
            new JsonStringifyRequest
            {
                Value = new Dictionary<string, object?> { ["ok"] = true }
            });

        result.CorrelationId.ShouldBe(new CorrelationId("json.stringify"));
        result.Payload.Text.ShouldBe("""{"ok":true}""");
        result.Payload.Bytes.ShouldBe(Encoding.UTF8.GetBytes(result.Payload.Text));
    }

    [Fact]
    public async Task Hosted_text_encode_encodes_text()
    {
        var result = await RunNodeAsync<TextEncodeRequest, TextEncodeResult>(
            SerializationCompositionNodeTypes.TextEncode,
            registry => registry.RegisterTextEncode(),
            new TextEncodeRequest { Text = "hello" });

        result.CorrelationId.ShouldBe(new CorrelationId("text.encode"));
        result.Payload.Bytes.ShouldBe(Encoding.UTF8.GetBytes("hello"));
        result.Payload.ByteCount.ShouldBe(5);
        result.Payload.Encoding.ShouldBe("utf-8");
    }

    [Fact]
    public async Task Hosted_text_decode_binds_options_and_decodes_bytes()
    {
        var encoding = Encoding.Unicode;
        var bytes = encoding.GetPreamble()
            .Concat(encoding.GetBytes("hello"))
            .ToArray();
        var result = await RunNodeAsync<TextDecodeRequest, TextDecodeResult>(
            SerializationCompositionNodeTypes.TextDecode,
            registry => registry.RegisterTextDecode(),
            new TextDecodeRequest { Bytes = bytes },
            node => node.Configure("defaultEncoding", "utf-16"));

        result.CorrelationId.ShouldBe(new CorrelationId("text.decode"));
        result.Payload.Text.ShouldBe("hello");
        result.Payload.Encoding.ShouldBe("utf-16");
        result.Payload.ByteCount.ShouldBe(bytes.Length);
    }

    [Fact]
    public async Task Hosted_base64_encode_encodes_text()
    {
        var result = await RunNodeAsync<Base64EncodeRequest, Base64EncodeResult>(
            SerializationCompositionNodeTypes.Base64Encode,
            registry => registry.RegisterBase64Encode(),
            new Base64EncodeRequest { Text = "hello" });

        result.CorrelationId.ShouldBe(new CorrelationId("base64.encode"));
        result.Payload.Text.ShouldBe("aGVsbG8=");
        result.Payload.ByteCount.ShouldBe(5);
        result.Payload.EncodedLength.ShouldBe(8);
    }

    [Fact]
    public async Task Hosted_base64_decode_decodes_text()
    {
        var result = await RunNodeAsync<Base64DecodeRequest, Base64DecodeResult>(
            SerializationCompositionNodeTypes.Base64Decode,
            registry => registry.RegisterBase64Decode(),
            new Base64DecodeRequest
            {
                Text = "aGVsbG8=",
                DecodeText = true
            });

        result.CorrelationId.ShouldBe(new CorrelationId("base64.decode"));
        result.Payload.Bytes.ShouldBe(Encoding.UTF8.GetBytes("hello"));
        result.Payload.Text.ShouldBe("hello");
        result.Payload.Encoding.ShouldBe("utf-8");
    }

    [Fact]
    public async Task Hosted_node_uses_optional_keyed_clock_for_events()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        await WithNodeAsync<JsonParseRequest, JsonParseResult>(
            SerializationCompositionNodeTypes.JsonParse,
            registry => registry.RegisterJsonParse(),
            async (input, output, descriptor) =>
            {
                var events = Link(descriptor.Events.ShouldNotBeNull());
                output.Source.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonParseResult>>());

                (await input.Target.SendAsync(FlowMessage.Create(
                        new JsonParseRequest { Text = """{"ok":true}""" }))
                    .WaitAsync(Timeout)).ShouldBeTrue();

                var @event = await events.ReceiveAsync().WaitAsync(Timeout);
                @event.Name.ShouldBe(SerializationDiagnosticNames.JsonParsed);
                @event.Timestamp.ShouldBe(timestamp);
            },
            configureNode: node => node.Resource(
                SerializationCompositionResourceNames.Clock,
                "fixed"),
            configureServices: services =>
                services.AddKeyedSingleton<TimeProvider>("fixed", clock));
    }

    [Fact]
    public async Task Hosted_node_emits_errors_and_continues_after_conversion_failure()
    {
        await WithNodeAsync<JsonParseRequest, JsonParseResult>(
            SerializationCompositionNodeTypes.JsonParse,
            registry => registry.RegisterJsonParse(),
            async (input, output, descriptor) =>
            {
                var results = Link(output.Source);
                var errors = Link(descriptor.Errors.ShouldNotBeNull());
                var bad = FlowMessage.Create(
                    new JsonParseRequest { Text = "{" },
                    new CorrelationId("bad"));
                var good = FlowMessage.Create(
                    new JsonParseRequest { Text = """{"ok":true}""" },
                    new CorrelationId("good"));

                (await input.Target.SendAsync(bad).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(good).WaitAsync(Timeout)).ShouldBeTrue();

                var error = await errors.ReceiveAsync().WaitAsync(Timeout);
                var result = await results.ReceiveAsync().WaitAsync(Timeout);

                error.Code.ShouldBe(SerializationErrorCodes.JsonParseFailed);
                error.CorrelationId.ShouldBe(bad.CorrelationId);
                result.CorrelationId.ShouldBe(good.CorrelationId);
                result.Payload.Kind.ShouldBe(JsonValueKind.Object);
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
            diagnostic.Message.Contains(
                "boundedCapacity",
                StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertMetadata<TInput, TOutput>(
        CompositionNodeRegistry registry,
        string nodeType)
    {
        var registration = registry.Registrations[nodeType];
        registration.Inputs[SerializationCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(TInput));
        registration.Outputs[SerializationCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(TOutput));
    }

    private static async Task<FlowMessage<TOutput>> RunNodeAsync<TInput, TOutput>(
        string nodeType,
        Func<CompositionNodeRegistry, CompositionNodeRegistry> register,
        TInput request,
        Action<NodeDefinitionBuilder>? configureNode = null)
    {
        FlowMessage<TOutput>? result = null;
        await WithNodeAsync<TInput, TOutput>(
            nodeType,
            register,
            async (input, output, _) =>
            {
                var results = Link(output.Source);
                var message = FlowMessage.Create(
                    request,
                    new CorrelationId(nodeType));

                (await input.Target.SendAsync(message).WaitAsync(Timeout))
                    .ShouldBeTrue();

                result = await results.ReceiveAsync().WaitAsync(Timeout);
            },
            configureNode);

        return result.ShouldNotBeNull();
    }

    private static async Task WithNodeAsync<TInput, TOutput>(
        string nodeType,
        Func<CompositionNodeRegistry, CompositionNodeRegistry> register,
        Func<CompositionInputPort<TInput>, CompositionOutputPort<TOutput>, ComposedNode, Task> run,
        Action<NodeDefinitionBuilder>? configureNode = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "node",
                    nodeType,
                    configureNode))
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
            .ShouldBeOfType<CompositionOutputPort<TOutput>>();

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
