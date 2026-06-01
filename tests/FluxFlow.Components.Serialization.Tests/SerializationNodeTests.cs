using FluxFlow.Components.Serialization.Contracts;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Serialization.Tests;

public sealed class SerializationNodeTests
{
    [Fact]
    public async Task JsonParse_ParsesTextAndEmitsResult()
    {
        var runtimeNode = CreateNode(
            SerializationComponentTypes.JsonParse,
            new { allowTrailingCommas = true });
        var input = GetInput<JsonParseRequest>(runtimeNode);
        var output = LinkOutput<JsonParseResult>(runtimeNode);

        await input.Target.SendAsync(new JsonParseRequest
        {
            Text = """{"name":"flux",}"""
        });
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Kind.ShouldBe(JsonValueKind.Object);
        result.Value.ShouldBeOfType<JsonObject>()["name"]!.GetValue<string>().ShouldBe("flux");
        result.ByteCount.ShouldBeGreaterThan(0);
        result.Encoding.ShouldBe("utf-8");
    }

    [Fact]
    public async Task JsonParse_EmitsErrorAndContinues()
    {
        var runtimeNode = CreateNode(
            SerializationComponentTypes.JsonParse,
            new { });
        var input = GetInput<JsonParseRequest>(runtimeNode);
        var output = LinkOutput<JsonParseResult>(runtimeNode);
        var errors = LinkOutput<FlowError>(
            runtimeNode,
            SerializationComponentPorts.Errors);

        await input.Target.SendAsync(new JsonParseRequest { Text = "{" });
        await input.Target.SendAsync(new JsonParseRequest { Text = """{"ok":true}""" });
        input.Target.Complete();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(SerializationErrorCodes.JsonParseFailed);
        result.Kind.ShouldBe(JsonValueKind.Object);
    }

    [Fact]
    public async Task JsonStringify_StringifiesValue()
    {
        var runtimeNode = CreateNode(
            SerializationComponentTypes.JsonStringify,
            new { });
        var input = GetInput<JsonStringifyRequest>(runtimeNode);
        var output = LinkOutput<JsonStringifyResult>(runtimeNode);

        await input.Target.SendAsync(new JsonStringifyRequest
        {
            Value = new Dictionary<string, object?>
            {
                ["ok"] = true
            }
        });
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Text.ShouldBe("""{"ok":true}""");
        result.Bytes.ShouldBe(Encoding.UTF8.GetBytes(result.Text));
        result.ByteCount.ShouldBe(result.Bytes.Length);
    }

    [Fact]
    public async Task TextEncode_EmitsBytes()
    {
        var runtimeNode = CreateNode(
            SerializationComponentTypes.TextEncode,
            new { });
        var input = GetInput<TextEncodeRequest>(runtimeNode);
        var output = LinkOutput<TextEncodeResult>(runtimeNode);

        await input.Target.SendAsync(new TextEncodeRequest { Text = "hello" });
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Bytes.ShouldBe(Encoding.UTF8.GetBytes("hello"));
        result.ByteCount.ShouldBe(5);
        result.Encoding.ShouldBe("utf-8");
    }

    [Fact]
    public async Task TextDecode_SkipsByteOrderMark()
    {
        var runtimeNode = CreateNode(
            SerializationComponentTypes.TextDecode,
            new { defaultEncoding = "utf-16" });
        var input = GetInput<TextDecodeRequest>(runtimeNode);
        var output = LinkOutput<TextDecodeResult>(runtimeNode);
        var encoding = Encoding.Unicode;
        var bytes = encoding.GetPreamble()
            .Concat(encoding.GetBytes("hello"))
            .ToArray();

        await input.Target.SendAsync(new TextDecodeRequest { Bytes = bytes });
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Text.ShouldBe("hello");
        result.Encoding.ShouldBe("utf-16");
        result.ByteCount.ShouldBe(bytes.Length);
    }

    [Fact]
    public async Task Base64Encode_EncodesText()
    {
        var runtimeNode = CreateNode(
            SerializationComponentTypes.Base64Encode,
            new { });
        var input = GetInput<Base64EncodeRequest>(runtimeNode);
        var output = LinkOutput<Base64EncodeResult>(runtimeNode);

        await input.Target.SendAsync(new Base64EncodeRequest { Text = "hello" });
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Text.ShouldBe("aGVsbG8=");
        result.ByteCount.ShouldBe(5);
        result.EncodedLength.ShouldBe(8);
    }

    [Fact]
    public async Task Base64Decode_DecodesText()
    {
        var runtimeNode = CreateNode(
            SerializationComponentTypes.Base64Decode,
            new { });
        var input = GetInput<Base64DecodeRequest>(runtimeNode);
        var output = LinkOutput<Base64DecodeResult>(runtimeNode);

        await input.Target.SendAsync(new Base64DecodeRequest
        {
            Text = "aGVsbG8=",
            DecodeText = true
        });
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Bytes.ShouldBe(Encoding.UTF8.GetBytes("hello"));
        result.ByteCount.ShouldBe(5);
        result.Text.ShouldBe("hello");
        result.Encoding.ShouldBe("utf-8");
    }

    [Fact]
    public async Task TextEncode_EmitsOutputTooLargeErrorAndContinues()
    {
        var runtimeNode = CreateNode(
            SerializationComponentTypes.TextEncode,
            new { maxOutputBytes = 2 });
        var input = GetInput<TextEncodeRequest>(runtimeNode);
        var output = LinkOutput<TextEncodeResult>(runtimeNode);
        var errors = LinkOutput<FlowError>(
            runtimeNode,
            SerializationComponentPorts.Errors);

        await input.Target.SendAsync(new TextEncodeRequest { Text = "hello" });
        await input.Target.SendAsync(new TextEncodeRequest { Text = "ok" });
        input.Target.Complete();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(SerializationErrorCodes.OutputTooLarge);
        result.Bytes.ShouldBe(Encoding.UTF8.GetBytes("ok"));
    }

    [Fact]
    public async Task TextEncode_EmitsInputTooLargeErrorAndContinues()
    {
        var runtimeNode = CreateNode(
            SerializationComponentTypes.TextEncode,
            new
            {
                maxInputBytes = 2,
                maxOutputBytes = 10
            });
        var input = GetInput<TextEncodeRequest>(runtimeNode);
        var output = LinkOutput<TextEncodeResult>(runtimeNode);
        var errors = LinkOutput<FlowError>(
            runtimeNode,
            SerializationComponentPorts.Errors);

        await input.Target.SendAsync(new TextEncodeRequest { Text = "hello" });
        await input.Target.SendAsync(new TextEncodeRequest { Text = "ok" });
        input.Target.Complete();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(SerializationErrorCodes.InputTooLarge);
        result.Bytes.ShouldBe(Encoding.UTF8.GetBytes("ok"));
    }

    [Fact]
    public async Task Base64Decode_AllowsEmptyText()
    {
        var runtimeNode = CreateNode(
            SerializationComponentTypes.Base64Decode,
            new { });
        var input = GetInput<Base64DecodeRequest>(runtimeNode);
        var output = LinkOutput<Base64DecodeResult>(runtimeNode);

        await input.Target.SendAsync(new Base64DecodeRequest { Text = "" });
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Bytes.ShouldBeEmpty();
        result.ByteCount.ShouldBe(0);
    }

    [Fact]
    public async Task TextDecode_EmitsUnsupportedEncodingError()
    {
        var runtimeNode = CreateNode(
            SerializationComponentTypes.TextDecode,
            new { });
        var input = GetInput<TextDecodeRequest>(runtimeNode);
        var errors = LinkOutput<FlowError>(
            runtimeNode,
            SerializationComponentPorts.Errors);

        await input.Target.SendAsync(new TextDecodeRequest
        {
            Bytes = [1, 2, 3],
            Encoding = "missing-encoding"
        });
        input.Target.Complete();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(SerializationErrorCodes.UnsupportedEncoding);
    }

    [Fact]
    public async Task JsonParse_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(
            SerializationComponentTypes.JsonParse,
            new { });
        var input = GetInput<JsonParseRequest>(runtimeNode);
        var output = LinkOutput<JsonParseResult>(runtimeNode);
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(
                diagnostics,
                new DataflowLinkOptions { PropagateCompletion = true });

        await input.Target.SendAsync(new JsonParseRequest { Text = """{"ok":true}""" });
        input.Target.Complete();
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(SerializationDiagnosticNames.JsonParsed);
        diagnostic.Attributes["kind"].ShouldBe(JsonValueKind.Object.ToString());
    }

    [Fact]
    public void Node_RejectsInvalidOptions()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(SerializationComponentTypes.JsonParse, new { boundedCapacity = 0 }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void RegisterSerializationComponents_RegistersAllNodes()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterSerializationComponents();

        registry.TryGetFactory(SerializationComponentTypes.JsonParse, out _).ShouldBeTrue();
        registry.TryGetFactory(SerializationComponentTypes.JsonStringify, out _).ShouldBeTrue();
        registry.TryGetFactory(SerializationComponentTypes.TextEncode, out _).ShouldBeTrue();
        registry.TryGetFactory(SerializationComponentTypes.TextDecode, out _).ShouldBeTrue();
        registry.TryGetFactory(SerializationComponentTypes.Base64Encode, out _).ShouldBeTrue();
        registry.TryGetFactory(SerializationComponentTypes.Base64Decode, out _).ShouldBeTrue();
    }

    private static RuntimeNode CreateNode(NodeType type, object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterSerializationComponents();
        registry.TryGetFactory(type, out var factory).ShouldBeTrue();
        return factory(CreateContext(type, configuration));
    }

    private static RuntimeNodeFactoryContext CreateContext(
        NodeType type,
        object configuration)
    {
        var root = JsonSerializer.SerializeToElement(configuration);
        var values = root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        return new RuntimeNodeFactoryContext(
            new NodeName("serialization"),
            new NodeDefinition
            {
                Type = type,
                Configuration = values
            },
            "main",
            new Dictionary<NodeName, RuntimeNode>());
    }

    private static InputPort<T> GetInput<T>(RuntimeNode runtimeNode)
        => runtimeNode.FindInput(new PortName(SerializationComponentPorts.Input))
            .ShouldBeOfType<InputPort<T>>();

    private static BufferBlock<T> LinkOutput<T>(
        RuntimeNode runtimeNode,
        string portName = SerializationComponentPorts.Output)
    {
        var target = new BufferBlock<T>();
        runtimeNode.FindOutput(new PortName(portName))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName("items"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
        return target;
    }
}
