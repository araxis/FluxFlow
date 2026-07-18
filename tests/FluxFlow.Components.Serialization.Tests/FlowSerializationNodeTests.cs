using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Nodes;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Serialization.Tests;

public sealed class FlowSerializationNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Json_parse_returns_flow_value_and_reuses_content_decode()
    {
        await using var node = new FlowContentJsonParseNode();
        var output = Sink(node.Output);
        var content = FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("""{"name":"sample","count":2}"""),
            "application/json");

        await node.Input.SendAsync(FlowMessage.Create(content));
        await node.Input.SendAsync(FlowMessage.Create(content));

        var first = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        var second = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        first.Kind.ShouldBe(SerializationResultKinds.JsonParsed);
        first.IsError.ShouldBeFalse();
        first.Value.ShouldNotBeNull().GetObject()["name"].GetString().ShouldBe("sample");
        first.Value.GetObject()["count"].GetInteger().ShouldBe(2);
        second.Value.ShouldBeSameAs(first.Value);
    }

    [Fact]
    public async Task Json_parse_honors_parser_options()
    {
        await using var node = new FlowContentJsonParseNode(new SerializationNodeOptions
        {
            AllowTrailingCommas = true,
            SkipComments = true
        });
        var output = Sink(node.Output);
        var content = FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("""{/* note */"ok":true,}"""),
            "text/plain");

        await node.Input.SendAsync(FlowMessage.Create(content));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.IsError.ShouldBeFalse();
        result.Value.ShouldNotBeNull().GetObject()["ok"].GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Json_parse_returns_error_result_and_continues()
    {
        await using var node = new FlowContentJsonParseNode();
        var output = Sink(node.Output);
        var bad = FlowMessage.Create(
            FlowContent.FromBytes(Encoding.UTF8.GetBytes("{"), "application/json"));
        var good = FlowMessage.Create(
            FlowContent.FromBytes(Encoding.UTF8.GetBytes("{}"), "application/json"));

        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(good);

        var failure = await output.ReceiveAsync().WaitAsync(Timeout);
        var success = await output.ReceiveAsync().WaitAsync(Timeout);
        failure.CorrelationId.ShouldBe(bad.CorrelationId);
        failure.Payload.Kind.ShouldBe(SerializationResultKinds.JsonParseFailed);
        failure.Payload.Error.ShouldNotBeNull().Code
            .ShouldBe(SerializationErrorCodeNames.JsonParseFailed);
        success.CorrelationId.ShouldBe(good.CorrelationId);
        success.Payload.IsError.ShouldBeFalse();
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Json_parse_out_of_range_number_is_a_normal_error()
    {
        await using var node = new FlowContentJsonParseNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(
            FlowContent.FromBytes(Encoding.UTF8.GetBytes("1e99999"), "application/json")));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.IsError.ShouldBeTrue();
        result.Error.ShouldNotBeNull().Code
            .ShouldBe(SerializationErrorCodeNames.JsonParseFailed);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Json_stringify_emits_deterministic_plain_json_content()
    {
        await using var node = new FlowValueJsonStringifyNode();
        var output = Sink(node.Output);
        var value = FlowValue.FromObject(new Dictionary<string, FlowValue>
        {
            ["z"] = FlowValue.From(2L),
            ["a"] = FlowValue.FromArray([FlowValue.From(true), FlowValue.Null])
        });

        var message = FlowMessage.Create(value);
        await node.Input.SendAsync(message);

        var response = await output.ReceiveAsync().WaitAsync(Timeout);
        response.CorrelationId.ShouldBe(message.CorrelationId);
        response.TraceId.ShouldBe(message.TraceId);
        response.CausationId.ShouldBe(message.MessageId);
        var content = response.Payload.Value.ShouldNotBeNull();
        content.ContentType.ShouldBe("application/json");
        content.Encoding.ShouldBe("utf-8");
        Encoding.UTF8.GetString(content.OriginalBytes.AsSpan())
            .ShouldBe("""{"a":[true,null],"z":2}""");
    }

    [Fact]
    public async Task Text_encode_requires_string_and_later_input_continues()
    {
        await using var node = new FlowValueTextEncodeNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From(1L)));
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("hello")));

        var failure = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        var success = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        failure.Error.ShouldNotBeNull().Code
            .ShouldBe(SerializationErrorCodeNames.TextEncodeFailed);
        Encoding.UTF8.GetString(success.Value.ShouldNotBeNull().OriginalBytes.AsSpan())
            .ShouldBe("hello");
    }

    [Fact]
    public async Task Text_decode_uses_quoted_content_type_charset()
    {
        await using var node = new FlowContentTextDecodeNode();
        var output = Sink(node.Output);
        var content = FlowContent.FromBytes(
            Encoding.Latin1.GetBytes("räksmörgås"),
            "text/plain; charset=\"iso-8859-1\"");

        await node.Input.SendAsync(FlowMessage.Create(content));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.Value.ShouldNotBeNull().GetString().ShouldBe("räksmörgås");
    }

    [Fact]
    public async Task Text_decode_invalid_declared_encoding_uses_configured_fallback()
    {
        await using var node = new FlowContentTextDecodeNode();
        var output = Sink(node.Output);
        var content = FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("hello"),
            "text/plain",
            "missing-encoding");

        await node.Input.SendAsync(FlowMessage.Create(content));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.IsError.ShouldBeFalse();
        result.Value.ShouldNotBeNull().GetString().ShouldBe("hello");
    }

    [Fact]
    public async Task Base64_round_trip_preserves_exact_content_bytes()
    {
        await using var encode = new FlowContentBase64EncodeNode();
        await using var decode = new FlowValueBase64DecodeNode();
        var encoded = Sink(encode.Output);
        var decoded = Sink(decode.Output);
        var bytes = new byte[] { 0, 1, 2, 253, 254, 255 };

        await encode.Input.SendAsync(FlowMessage.Create(
            FlowContent.FromBytes(bytes, "application/example")));
        var text = (await encoded.ReceiveAsync().WaitAsync(Timeout))
            .Payload.Value.ShouldNotBeNull();
        text.GetString().ShouldBe(Convert.ToBase64String(bytes));

        await decode.Input.SendAsync(FlowMessage.Create(text));
        var content = (await decoded.ReceiveAsync().WaitAsync(Timeout))
            .Payload.Value.ShouldNotBeNull();
        content.ContentType.ShouldBe("application/octet-stream");
        content.OriginalBytes.AsSpan().SequenceEqual(bytes).ShouldBeTrue();
    }

    [Fact]
    public async Task Base64_decode_invalid_text_returns_error_result()
    {
        await using var node = new FlowValueBase64DecodeNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("not-base64")));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.Kind.ShouldBe(SerializationResultKinds.Base64DecodeFailed);
        result.Error.ShouldNotBeNull().Code
            .ShouldBe(SerializationErrorCodeNames.Base64DecodeFailed);
    }

    [Fact]
    public async Task Input_and_output_limits_are_normal_result_errors()
    {
        await using var parse = new FlowContentJsonParseNode(new SerializationNodeOptions
        {
            MaxInputBytes = 2
        });
        await using var decode = new FlowValueBase64DecodeNode(new SerializationNodeOptions
        {
            MaxOutputBytes = 2
        });
        var parsed = Sink(parse.Output);
        var decoded = Sink(decode.Output);

        await parse.Input.SendAsync(FlowMessage.Create(
            FlowContent.FromBytes(Encoding.UTF8.GetBytes("{} "), "application/json")));
        await decode.Input.SendAsync(FlowMessage.Create(FlowValue.From("aGVsbG8=")));

        (await parsed.ReceiveAsync().WaitAsync(Timeout)).Payload.Error
            .ShouldNotBeNull().Code.ShouldBe(SerializationErrorCodeNames.InputTooLarge);
        (await decoded.ReceiveAsync().WaitAsync(Timeout)).Payload.Error
            .ShouldNotBeNull().Code.ShouldBe(SerializationErrorCodeNames.OutputTooLarge);
    }

    [Fact]
    public async Task Null_content_is_a_normal_error_and_does_not_stop_the_node()
    {
        await using var node = new FlowContentJsonParseNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create<FlowContent>(null!));
        await node.Input.SendAsync(FlowMessage.Create(
            FlowContent.FromBytes(Encoding.UTF8.GetBytes("null"), "application/json")));

        (await output.ReceiveAsync().WaitAsync(Timeout)).Payload.Error
            .ShouldNotBeNull().Code.ShouldBe(SerializationErrorCodeNames.MissingInput);
        (await output.ReceiveAsync().WaitAsync(Timeout)).Payload.IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task Success_and_failure_emit_diagnostic_events()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-18T12:00:00Z"));
        await using var node = new FlowValueTextEncodeNode(clock: clock);
        Sink(node.Output);
        var events = Sink(node.Events);

        var bad = FlowMessage.Create(FlowValue.From(false));
        var good = FlowMessage.Create(FlowValue.From("ok"));
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(good);

        var failed = await events.ReceiveAsync().WaitAsync(Timeout);
        var succeeded = await events.ReceiveAsync().WaitAsync(Timeout);
        failed.Name.ShouldBe(SerializationDiagnosticNames.TextEncodeFailed);
        failed.Level.ShouldBe(FlowEventLevel.Warning);
        failed.CorrelationId.ShouldBe(bad.CorrelationId);
        failed.Timestamp.ShouldBe(clock.GetUtcNow());
        succeeded.Name.ShouldBe(SerializationDiagnosticNames.TextEncoded);
        succeeded.Level.ShouldBe(FlowEventLevel.Information);
        succeeded.CorrelationId.ShouldBe(good.CorrelationId);
    }

    [Fact]
    public void Canonical_node_rejects_invalid_static_options()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new FlowContentJsonParseNode(new SerializationNodeOptions
            {
                BoundedCapacity = 0
            })).Message.ShouldContain("boundedCapacity");
        Should.Throw<ArgumentException>(() =>
            new FlowValueTextEncodeNode(new SerializationNodeOptions
            {
                DefaultEncoding = "missing-encoding"
            })).Message.ShouldContain("defaultEncoding");
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }
}
