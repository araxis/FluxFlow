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
        received.IsError.ShouldBeFalse();
        received.CorrelationId.ShouldBe(message.CorrelationId);
        received.TraceId.ShouldBe(message.TraceId);
        received.CausationId.ShouldBe(message.MessageId);
        var inspection = received.Value;
        inspection.Timestamp.ShouldBe(timestamp);
        inspection.Content.ShouldBeSameAs(content);
        inspection.Kind.ShouldBe(PayloadKind.JsonObject);
        inspection.JsonValue.ShouldNotBeNull().GetProperty("name").GetString()
            .ShouldBe("sample");
        inspection.DetectedEncoding.ShouldBe("utf-8");
        inspection.TextPreview.ShouldNotBeNull().ShouldContain("\"name\"");
        inspection.FormattedPreview.ShouldNotBeNull().ShouldContain("\n");
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

        var inspection = (await output.ReceiveAsync().WaitAsync(Timeout)).Value;
        inspection.Kind.ShouldBe(PayloadKind.Text);
        inspection.DetectedEncoding.ShouldBe("utf-8");
        inspection.TextPreview.ShouldBe("hello");
    }

    [Fact]
    public async Task Emits_invalid_declared_json_as_an_in_band_error_and_continues()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);
        var events = Sink(node.Events);
        var invalid = FlowContent.FromBytes(Encoding.UTF8.GetBytes("{"), "application/json");
        var valid = FlowContent.FromBytes(Encoding.UTF8.GetBytes("{}"), "application/json");

        await node.Input.SendAsync(FlowMessage.Create(invalid));
        await node.Input.SendAsync(FlowMessage.Create(valid));

        var failure = await output.ReceiveAsync().WaitAsync(Timeout);
        failure.IsError.ShouldBeTrue();
        failure.Error!.Code.ShouldBe(PayloadErrorCodeNames.ParseFailed);
        failure.Error.Category.ShouldBe("Payloads");
        failure.Error.Details.ShouldNotBeNull().GetProperty("byteCount").GetInt32()
            .ShouldBe(1);

        var success = await output.ReceiveAsync().WaitAsync(Timeout);
        success.IsError.ShouldBeFalse();
        success.Value.Content.ShouldBeSameAs(valid);

        (await events.ReceiveAsync().WaitAsync(Timeout)).Name
            .ShouldBe(PayloadDiagnosticNames.Failed);
        (await events.ReceiveAsync().WaitAsync(Timeout)).Name
            .ShouldBe(PayloadDiagnosticNames.Inspected);

        node.Complete();
        await node.Completion.WaitAsync(Timeout);
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Propagates_incoming_errors_without_inspection()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);
        var error = new FlowError(
            "upstream.failed",
            "Input was unavailable.",
            "Payloads",
            isTransient: false);

        await node.Input.SendAsync(FlowMessage.CreateError<FlowContent>(error));

        var result = await output.ReceiveAsync().WaitAsync(Timeout);
        result.IsError.ShouldBeTrue();
        result.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public async Task Keeps_unknown_media_as_exact_binary_without_content_sniffing()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);
        var bytes = Encoding.UTF8.GetBytes("""{"looks":"json"}""");
        var content = FlowContent.FromBytes(bytes, "application/octet-stream");

        await node.Input.SendAsync(FlowMessage.Create(content));

        var inspection = (await output.ReceiveAsync().WaitAsync(Timeout)).Value;
        inspection.Kind.ShouldBe(PayloadKind.Binary);
        inspection.Content.ShouldBeSameAs(content);
        inspection.Content.ShouldNotBeNull().Bytes.AsSpan().SequenceEqual(bytes).ShouldBeTrue();
        inspection.JsonValue.ShouldBeNull();
        inspection.TextPreview.ShouldBeNull();
    }

    [Fact]
    public async Task Rejects_oversized_content_before_decoding()
    {
        await using var node = new PayloadInspectNode(
            new PayloadInspectOptions { MaxInputBytes = 3 });
        var output = Sink(node.Output);
        var content = FlowContent.FromBytes(new byte[] { 1, 2, 3, 4 }, "application/json");

        await node.Input.SendAsync(FlowMessage.Create(content));

        var result = await output.ReceiveAsync().WaitAsync(Timeout);
        result.IsError.ShouldBeTrue();
        result.Error!.Code.ShouldBe(PayloadErrorCodeNames.InputTooLarge);
        result.Error.Details.ShouldNotBeNull().GetProperty("byteCount").GetInt32()
            .ShouldBe(4);
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

        var xmlResult = (await output.ReceiveAsync().WaitAsync(Timeout)).Value;
        xmlResult.Kind.ShouldBe(PayloadKind.Xml);
        xmlResult.FormattedPreview.ShouldNotBeNull().ShouldContain("<value>1</value>");

        var base64Result = (await output.ReceiveAsync().WaitAsync(Timeout)).Value;
        base64Result.Kind.ShouldBe(PayloadKind.Base64);
        base64Result.Base64DecodedByteCount.ShouldBe(5);
        base64Result.FormattedPreview.ShouldBe("hello");
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

        (await output.ReceiveAsync().WaitAsync(Timeout)).Value.Kind.ShouldBe(expectedKind);
    }

    [Fact]
    public async Task Honors_a_quoted_charset_from_content_type()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);
        var bytes = Encoding.Unicode.GetBytes("{\"name\":\"sample\"}");
        var content = FlowContent.FromBytes(bytes, "application/json; charset=\"utf-16\"");

        await node.Input.SendAsync(FlowMessage.Create(content));

        var inspection = (await output.ReceiveAsync().WaitAsync(Timeout)).Value;
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

        var empty = (await output.ReceiveAsync().WaitAsync(Timeout)).Value;
        empty.Kind.ShouldBe(PayloadKind.Empty);
        empty.TextPreview.ShouldBe(string.Empty);

        var truncated = (await output.ReceiveAsync().WaitAsync(Timeout)).Value;
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

        (await first.ReceiveAsync().WaitAsync(Timeout)).IsError.ShouldBeFalse();
        (await second.ReceiveAsync().WaitAsync(Timeout)).IsError.ShouldBeFalse();
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
}
