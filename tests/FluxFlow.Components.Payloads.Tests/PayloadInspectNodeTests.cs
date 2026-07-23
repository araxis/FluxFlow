using System.Collections.Immutable;
using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Diagnostics;
using FluxFlow.Components.Payloads.Nodes;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Payloads.Tests;

public sealed class PayloadInspectNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Inspects_declared_json_and_preserves_content_and_message_identity()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-18T12:00:00Z");
        await using var node = new PayloadInspectNode(
            new PayloadInspectOptions { MaxPreviewBytes = 128 },
            clock: new FakeTimeProvider(timestamp));
        var output = Sink(node.Output);
        var content = FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("""{"name":"sample","count":2}"""),
            "application/json");
        var message = FlowMessage.Create(content);

        (await node.Input.SendAsync(message)).ShouldBeTrue();

        var received = await output.ReceiveAsync().WaitAsync(Timeout);
        received.CorrelationId.ShouldBe(message.CorrelationId);
        received.TraceId.ShouldBe(message.TraceId);
        received.CausationId.ShouldBe(message.MessageId);
        received.Payload.Kind.ShouldBe(PayloadInspectionResultKinds.Inspected);
        received.Payload.IsError.ShouldBeFalse();
        received.Payload.Timestamp.ShouldBe(timestamp);
        var inspection = received.Payload.Value.ShouldNotBeNull();
        inspection.Content.ShouldBeSameAs(content);
        inspection.DecodedValue.ShouldNotBeNull().Kind.ShouldBe(FlowValueKind.Object);
        inspection.Kind.ShouldBe(PayloadKind.JsonObject);
        inspection.DetectedEncoding.ShouldBe("utf-8");
        inspection.TextPreview.ShouldNotBeNull().ShouldContain("\"name\"");
        inspection.FormattedPreview.ShouldNotBeNull().ShouldContain("\n");
    }

    [Fact]
    public async Task Reuses_flow_content_decode_cache_with_a_host_codec_catalog()
    {
        var codec = new CountingCodec(FlowValue.FromObject(new Dictionary<string, FlowValue>
        {
            ["decoded"] = FlowValue.From(true)
        }));
        var catalog = new FlowContentCodecCatalog(
        [
            new(FlowContentCodecMatch.ExactMediaType, "application/example", codec)
        ],
        new BinaryFlowContentCodec());
        await using var node = new PayloadInspectNode(codecs: catalog);
        var output = Sink(node.Output);
        var content = FlowContent.FromBytes(new byte[] { 1, 2, 3 }, "application/example");

        await node.Input.SendAsync(FlowMessage.Create(content));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.IsError.ShouldBeFalse();
        result.Value.ShouldNotBeNull().Kind.ShouldBe(PayloadKind.Value);
        result.Value.DecodedValue.ShouldBeSameAs(content.ReadAsFlowValue(catalog));
        codec.DecodeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Falls_back_to_utf8_for_an_invalid_declared_text_encoding()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);
        var content = FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("hello"),
            "text/plain",
            "missing-encoding");

        await node.Input.SendAsync(FlowMessage.Create(content));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.IsError.ShouldBeFalse();
        var inspection = result.Value.ShouldNotBeNull();
        inspection.Kind.ShouldBe(PayloadKind.Text);
        inspection.DetectedEncoding.ShouldBe("utf-8");
        inspection.TextPreview.ShouldBe("hello");
    }

    [Fact]
    public async Task Emits_invalid_declared_json_as_a_normal_error_result_and_continues()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);
        var events = Sink(node.Events);
        var invalid = FlowContent.FromBytes(Encoding.UTF8.GetBytes("{"), "application/json");
        var valid = FlowContent.FromBytes(Encoding.UTF8.GetBytes("{}"), "application/json");

        await node.Input.SendAsync(FlowMessage.Create(invalid));
        await node.Input.SendAsync(FlowMessage.Create(valid));

        var failure = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        failure.Kind.ShouldBe(PayloadInspectionResultKinds.ParseFailed);
        failure.IsError.ShouldBeTrue();
        failure.Error!.Code.ShouldBe(PayloadErrorCodeNames.ParseFailed);
        failure.Error.Category.ShouldBe("Payloads");
        failure.Value.ShouldNotBeNull().Content.ShouldBeSameAs(invalid);
        failure.Value.ParseError.ShouldNotBeNullOrWhiteSpace();

        var success = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        success.IsError.ShouldBeFalse();
        success.Value.ShouldNotBeNull().Content.ShouldBeSameAs(valid);

        (await events.ReceiveAsync().WaitAsync(Timeout)).Name
            .ShouldBe(PayloadDiagnosticNames.Failed);
        (await events.ReceiveAsync().WaitAsync(Timeout)).Name
            .ShouldBe(PayloadDiagnosticNames.Inspected);

        node.Complete();
        await node.Completion.WaitAsync(Timeout);
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Emits_null_content_as_a_normal_error_result_and_continues()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);
        var invalid = new FlowMessage<FlowContent>(new CorrelationId("null"), null!);
        var valid = FlowMessage.Create(
            FlowContent.FromBytes(Encoding.UTF8.GetBytes("hello"), "text/plain"));

        await node.Input.SendAsync(invalid);
        await node.Input.SendAsync(valid);

        var failure = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        failure.Kind.ShouldBe(PayloadInspectionResultKinds.InspectFailed);
        failure.IsError.ShouldBeTrue();
        failure.Error!.Code.ShouldBe(PayloadErrorCodeNames.InspectFailed);
        failure.Value.ShouldNotBeNull().ParseError.ShouldNotBeNull()
            .ShouldContain("requires FlowContent");

        var success = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        success.IsError.ShouldBeFalse();
        success.Value.ShouldNotBeNull().TextPreview.ShouldBe("hello");
    }

    [Fact]
    public async Task Keeps_unknown_media_as_binary_without_content_sniffing()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);
        var bytes = Encoding.UTF8.GetBytes("""{"looks":"json"}""");
        var content = FlowContent.FromBytes(bytes, "application/octet-stream");

        await node.Input.SendAsync(FlowMessage.Create(content));

        var inspection = (await output.ReceiveAsync().WaitAsync(Timeout))
            .Payload.Value.ShouldNotBeNull();
        inspection.Kind.ShouldBe(PayloadKind.Binary);
        inspection.DecodedValue.ShouldNotBeNull().GetBinary().AsSpan()
            .SequenceEqual(bytes).ShouldBeTrue();
        inspection.TextPreview.ShouldBeNull();
    }

    [Fact]
    public async Task Rejects_oversized_content_before_decoding()
    {
        var codec = new CountingCodec(FlowValue.From("unused"));
        var catalog = new FlowContentCodecCatalog(
        [
            new(FlowContentCodecMatch.ExactMediaType, "application/example", codec)
        ],
        new BinaryFlowContentCodec());
        await using var node = new PayloadInspectNode(
            new PayloadInspectOptions { MaxInputBytes = 3 },
            catalog);
        var output = Sink(node.Output);
        var content = FlowContent.FromBytes(new byte[] { 1, 2, 3, 4 }, "application/example");

        await node.Input.SendAsync(FlowMessage.Create(content));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.Kind.ShouldBe(PayloadInspectionResultKinds.InputTooLarge);
        result.IsError.ShouldBeTrue();
        result.Error!.Code.ShouldBe(PayloadErrorCodeNames.InputTooLarge);
        result.Value.ShouldNotBeNull().ByteCount.ShouldBe(4);
        result.Value.Content.ShouldBeSameAs(content);
        codec.DecodeCount.ShouldBe(0);
    }

    [Fact]
    public async Task Inspects_xml_and_text_base64_through_declared_media_types()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);
        var xml = FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("<root><value>1</value></root>"),
            "application/xml");
        var base64 = FlowContent.FromBytes(
            Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"))),
            "text/plain");

        await node.Input.SendAsync(FlowMessage.Create(xml));
        await node.Input.SendAsync(FlowMessage.Create(base64));

        var xmlResult = (await output.ReceiveAsync().WaitAsync(Timeout))
            .Payload.Value.ShouldNotBeNull();
        xmlResult.Kind.ShouldBe(PayloadKind.Xml);
        xmlResult.FormattedPreview.ShouldNotBeNull().ShouldContain("<value>1</value>");

        var base64Result = (await output.ReceiveAsync().WaitAsync(Timeout))
            .Payload.Value.ShouldNotBeNull();
        base64Result.Kind.ShouldBe(PayloadKind.Base64);
        base64Result.Base64DecodedByteCount.ShouldBe(5);
        base64Result.FormattedPreview.ShouldBe("hello");
    }

    [Fact]
    public async Task Inspects_value_backed_content_without_serialization_at_the_boundary()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);
        var value = FlowValue.FromObject(new Dictionary<string, FlowValue>
        {
            ["active"] = FlowValue.From(true)
        });
        var content = FlowContent.FromValue(value);

        await node.Input.SendAsync(FlowMessage.Create(content));

        var inspection = (await output.ReceiveAsync().WaitAsync(Timeout))
            .Payload.Value.ShouldNotBeNull();
        inspection.Kind.ShouldBe(PayloadKind.Value);
        inspection.Content.ShouldBeSameAs(content);
        inspection.DecodedValue.ShouldBeSameAs(value);
    }

    [Theory]
    [InlineData("{}", PayloadKind.JsonObject)]
    [InlineData("[]", PayloadKind.JsonArray)]
    [InlineData("42", PayloadKind.JsonScalar)]
    public async Task Classifies_each_declared_json_shape(string json, PayloadKind expectedKind)
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);
        var content = FlowContent.FromBytes(Encoding.UTF8.GetBytes(json), "application/json");

        await node.Input.SendAsync(FlowMessage.Create(content));

        var inspection = (await output.ReceiveAsync().WaitAsync(Timeout))
            .Payload.Value.ShouldNotBeNull();
        inspection.Kind.ShouldBe(expectedKind);
    }

    [Fact]
    public async Task Honors_a_quoted_charset_from_content_type()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);
        var bytes = Encoding.Unicode.GetBytes("{\"name\":\"sample\"}");
        var content = FlowContent.FromBytes(bytes, "application/json; charset=\"utf-16\"");

        await node.Input.SendAsync(FlowMessage.Create(content));

        var inspection = (await output.ReceiveAsync().WaitAsync(Timeout))
            .Payload.Value.ShouldNotBeNull();
        inspection.Kind.ShouldBe(PayloadKind.JsonObject);
        inspection.DetectedEncoding.ShouldBe("utf-16");
        inspection.TextPreview.ShouldNotBeNull().ShouldContain("sample");
    }

    [Fact]
    public async Task Preserves_empty_content_and_preview_limits()
    {
        await using var node = new PayloadInspectNode(new PayloadInspectOptions
        {
            MaxPreviewBytes = 3,
            MaxFormattedChars = 10
        });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(
            FlowContent.FromBytes(ReadOnlyMemory<byte>.Empty, "text/plain")));
        await node.Input.SendAsync(FlowMessage.Create(FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("{\"message\":\"abcdef\"}"),
            "application/json")));

        var empty = (await output.ReceiveAsync().WaitAsync(Timeout))
            .Payload.Value.ShouldNotBeNull();
        empty.Kind.ShouldBe(PayloadKind.Empty);
        empty.TextPreview.ShouldBe(string.Empty);

        var truncated = (await output.ReceiveAsync().WaitAsync(Timeout))
            .Payload.Value.ShouldNotBeNull();
        truncated.TextPreview.ShouldBe("{\"m");
        truncated.TextPreviewTruncated.ShouldBeTrue();
        truncated.FormattedPreview.ShouldNotBeNull().Length.ShouldBe(10);
        truncated.FormattedPreviewTruncated.ShouldBeTrue();
    }

    [Fact]
    public async Task Fans_out_each_result_to_every_consumer()
    {
        await using var node = new PayloadInspectNode();
        var first = Sink(node.Output);
        var second = Sink(node.Output);
        var content = FlowContent.FromBytes(Encoding.UTF8.GetBytes("hello"), "text/plain");

        await node.Input.SendAsync(FlowMessage.Create(content));

        (await first.ReceiveAsync().WaitAsync(Timeout)).Payload.IsError.ShouldBeFalse();
        (await second.ReceiveAsync().WaitAsync(Timeout)).Payload.IsError.ShouldBeFalse();
    }

    [Theory]
    [InlineData("maxInputBytes")]
    [InlineData("maxPreviewBytes")]
    [InlineData("maxFormattedChars")]
    [InlineData("boundedCapacity")]
    public void Rejects_non_positive_limits(string option)
    {
        var options = option switch
        {
            "maxInputBytes" => new PayloadInspectOptions { MaxInputBytes = 0 },
            "maxPreviewBytes" => new PayloadInspectOptions { MaxPreviewBytes = 0 },
            "maxFormattedChars" => new PayloadInspectOptions { MaxFormattedChars = 0 },
            "boundedCapacity" => new PayloadInspectOptions { BoundedCapacity = 0 },
            _ => throw new InvalidOperationException()
        };

        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new PayloadInspectNode(options));
        exception.Message.ShouldContain(option);
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink);
        return sink;
    }

    private sealed class CountingCodec(FlowValue value) : IFlowContentCodec
    {
        private int _decodeCount;

        public int DecodeCount => Volatile.Read(ref _decodeCount);

        public FlowValue Decode(ImmutableArray<byte> content, string? encoding)
        {
            Interlocked.Increment(ref _decodeCount);
            return value;
        }
    }
}
