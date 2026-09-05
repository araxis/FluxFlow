using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Nodes;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Serialization.Tests;

public sealed class SerializationNodeTests
{
    [Fact]
    public async Task JsonParse_ProducesOwnedJsonElement()
    {
        await using var node = new JsonParseNode();
        var output = Sink(node.Output);
        await node.Input.SendAsync(Message(FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("{\"count\":2,\"enabled\":true}"),
            "application/json")));

        var result = await Receive(output);

        result.IsError.ShouldBeFalse();
        result.Value.ToJsonElement().GetProperty("count").GetInt32().ShouldBe(2);
        result.Value.ToJsonElement().GetProperty("enabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task JsonParse_HonorsParserOptions()
    {
        await using var node = new JsonParseNode(new SerializationNodeOptions
        {
            AllowTrailingCommas = true,
            SkipComments = true
        });
        var output = Sink(node.Output);
        await node.Input.SendAsync(Message(FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("{/*comment*/\"value\":1,}"),
            "application/json")));

        (await Receive(output)).Value!.ToJsonElement().GetProperty("value").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task ConversionFailure_IsErrorDataAndLaterInputContinues()
    {
        await using var node = new JsonParseNode();
        var output = Sink(node.Output);
        await node.Input.SendAsync(Message(FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("{invalid"),
            "application/json")));
        await node.Input.SendAsync(Message(FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("{\"ok\":true}"),
            "application/json")));

        var failure = await Receive(output);
        var success = await Receive(output);

        failure.IsError.ShouldBeTrue();
        failure.Error!.Code.ShouldBe(SerializationErrorCodeNames.JsonParseFailed);
        success.IsError.ShouldBeFalse();
        success.Value.ToJsonElement().GetProperty("ok").GetBoolean().ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, "{\"name\":\"sample\",\"items\":[1,2]}")]
    [InlineData(true, "{\n  \"name\": \"sample\",\n  \"items\": [\n    1,\n    2\n  ]\n}")]
    public async Task JsonStringify_reuses_format_options_without_changing_exact_utf8_output(
        bool writeIndented,
        string expected)
    {
        expected = expected.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        using var document = await JsonDocument.ParseAsync(new MemoryStream(
            Encoding.UTF8.GetBytes("{\"name\":\"sample\",\"items\":[1,2]}")));
        await using var node = new JsonStringifyNode(new SerializationNodeOptions
        {
            WriteIndented = writeIndented,
            DefaultEncoding = "utf-8"
        });
        var output = Sink(node.Output);
        await node.Input.SendAsync(Message(document.RootElement.Clone()));
        await node.Input.SendAsync(Message(document.RootElement.Clone()));

        var first = await Receive(output);
        var second = await Receive(output);

        first.IsError.ShouldBeFalse();
        second.IsError.ShouldBeFalse();
        var firstContent = Read<FlowContent>(first.Value);
        var secondContent = Read<FlowContent>(second.Value);
        firstContent.Bytes.AsSpan().ToArray().ShouldBe(Encoding.UTF8.GetBytes(expected));
        secondContent.Bytes.ShouldBe(firstContent.Bytes);
        firstContent.ContentType.ShouldBe("application/json");
        secondContent.ContentType.ShouldBe("application/json");
        firstContent.Encoding.ShouldBe("utf-8");
        secondContent.Encoding.ShouldBe("utf-8");
        var converters = typeof(JsonStringifyNode).Assembly.GetType(
            "FluxFlow.Components.Serialization.Nodes.SerializationConverters")
            .ShouldNotBeNull();
        var cachedOptions = converters
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(JsonSerializerOptions))
            .ToArray();
        cachedOptions.Length.ShouldBe(2);
        cachedOptions.ShouldAllBe(field => field.IsInitOnly);
        cachedOptions
            .Select(field => field.GetValue(null).ShouldBeOfType<JsonSerializerOptions>().WriteIndented)
            .OrderBy(value => value)
            .ShouldBe([false, true]);
    }

    [Fact]
    public async Task TextEncodeAndDecode_RoundTripDeclaredEncoding()
    {
        await using var encoder = new TextEncodeNode(new SerializationNodeOptions
        {
            DefaultEncoding = "iso-8859-1"
        });
        await using var decoder = new TextDecodeNode();
        var encoded = Sink(encoder.Output);
        var decoded = Sink(decoder.Output);

        await encoder.Input.SendAsync(Message("café"));
        var content = Read<FlowContent>((await Receive(encoded)).Value!);
        await decoder.Input.SendAsync(Message(content));

        content.Bytes.ShouldBe([0x63, 0x61, 0x66, 0xe9]);
        Read<string>((await Receive(decoded)).Value!).ShouldBe("café");
    }

    [Fact]
    public async Task TextDecode_UsesQuotedCharsetAndFallbackForInvalidCharset()
    {
        await using var node = new TextDecodeNode();
        var output = Sink(node.Output);
        await node.Input.SendAsync(Message(FlowContent.FromBytes(
            new byte[] { 0x63, 0x61, 0x66, 0xe9 },
            "text/plain; charset=\"iso-8859-1\"")));
        await node.Input.SendAsync(Message(FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("value"),
            "text/plain; charset=not-valid")));

        Read<string>((await Receive(output)).Value!).ShouldBe("café");
        Read<string>((await Receive(output)).Value!).ShouldBe("value");
    }

    [Fact]
    public async Task Base64_RoundTripPreservesExactBytes()
    {
        await using var encoder = new Base64EncodeNode();
        await using var decoder = new Base64DecodeNode();
        var encoded = Sink(encoder.Output);
        var decoded = Sink(decoder.Output);
        var original = FlowContent.FromBytes(new byte[] { 0, 1, 2, 255 });

        await encoder.Input.SendAsync(Message(original));
        var text = Read<string>((await Receive(encoded)).Value!);
        await decoder.Input.SendAsync(Message(text));

        text.ShouldBe("AAEC/w==");
        Read<FlowContent>((await Receive(decoded)).Value!).Bytes.ShouldBe(original.Bytes);
    }

    [Fact]
    public async Task Base64Decode_InvalidTextProducesError()
    {
        await using var node = new Base64DecodeNode();
        var output = Sink(node.Output);
        await node.Input.SendAsync(Message("not-base64"));

        var result = await Receive(output);

        result.IsError.ShouldBeTrue();
        result.Error!.Code.ShouldBe(SerializationErrorCodeNames.Base64DecodeFailed);
    }

    [Fact]
    public async Task InputError_IsPropagatedWithoutConversion()
    {
        await using var node = new JsonParseNode();
        var output = Sink(node.Output);
        var error = new FlowError("upstream.failed", "Upstream failed.", "upstream");
        var input = FlowMessage.CreateError(error);
        await node.Input.SendAsync(input);

        var result = await Receive(output);

        result.IsError.ShouldBeTrue();
        result.Error.ShouldBeSameAs(error);
        result.TraceId.ShouldBe(input.TraceId);
        result.CausationId.ShouldBe(input.MessageId);
    }

    [Fact]
    public async Task Output_FansOutSameOwnedJsonValueWithoutReparse()
    {
        await using var node = new JsonParseNode();
        var first = Sink(node.Output);
        var second = Sink(node.Output);
        await node.Input.SendAsync(Message(FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("{\"id\":7}"),
            "application/json")));

        var firstResult = await Receive(first);
        var secondResult = await Receive(second);

        firstResult.ShouldBeSameAs(secondResult);
        firstResult.Value!.ToJsonElement().GetProperty("id").GetInt32().ShouldBe(7);
    }

    [Fact]
    public async Task LimitsProduceErrorData()
    {
        await using var node = new JsonParseNode(new SerializationNodeOptions
        {
            MaxInputBytes = 2
        });
        var output = Sink(node.Output);
        await node.Input.SendAsync(Message(FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("{} "),
            "application/json")));

        (await Receive(output)).Error!.Code.ShouldBe(SerializationErrorCodeNames.InputTooLarge);
    }

    [Fact]
    public async Task SuccessAndFailureEmitDiagnosticEvents()
    {
        await using var node = new JsonParseNode();
        var events = Sink(node.Events);
        await node.Input.SendAsync(Message(FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("{}"),
            "application/json")));
        await node.Input.SendAsync(Message(FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("{"),
            "application/json")));

        (await Receive(events)).ToFlowEvent().Name.ShouldBe(SerializationDiagnosticNames.JsonParsed);
        (await Receive(events)).ToFlowEvent().Name.ShouldBe(SerializationDiagnosticNames.JsonParseFailed);
    }

    [Fact]
    public void CanonicalNode_RejectsInvalidStaticOptions()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new JsonParseNode(
            new SerializationNodeOptions { BoundedCapacity = 0 }));
        Should.Throw<ArgumentException>(() => new TextEncodeNode(
            new SerializationNodeOptions { DefaultEncoding = "not-an-encoding" }));
    }

    [Fact]
    public async Task MaterializationFailure_IsRejectedAndLaterInputContinues()
    {
        await using var node = new Base64DecodeNode();
        var output = Sink(node.Output);
        var events = Sink(node.Events);

        await node.Input.SendAsync(Message(new { unexpected = true }));
        await node.Input.SendAsync(Message("AAEC/w=="));

        var failure = await Receive(output);
        var success = await Receive(output);
        failure.Error!.Code.ShouldBe("flow.value.expected_string");
        success.IsError.ShouldBeFalse();
        Read<FlowContent>(success.Value).Bytes.ShouldBe([0, 1, 2, 255]);
        (await Receive(events)).ToFlowEvent().Name.ShouldBe("base64.decode.rejected");
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink);
        return sink;
    }

    private static Task<T> Receive<T>(IReceivableSourceBlock<T> source)
        => source.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));

    private static FlowMessage Message<T>(T value)
        => FlowMessage.Create(FlowValue.From(value));

    private static T Read<T>(FlowValue value)
        => value.ToJsonElement().Deserialize<T>()!;
}
