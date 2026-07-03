using FluxFlow.Components.Serialization;
using FluxFlow.Components.Serialization.Contracts;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Nodes;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Serialization.Tests;

// Every test news the node directly — no engine, no registry. Messages travel as
// FlowMessage<T> envelopes; the correlation id flows request -> result for free.
public sealed class SerializationNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task JsonParse_ParsesTextAndPreservesCorrelationId()
    {
        await using var node = new JsonParseNode(
            new SerializationNodeOptions { AllowTrailingCommas = true });
        var output = Sink(node.Output);

        var request = FlowMessage.Create(new JsonParseRequest { Text = """{"name":"flux",}""" });
        await node.Input.SendAsync(request);

        var received = await output.ReceiveAsync().WaitAsync(Timeout);
        received.CorrelationId.ShouldBe(request.CorrelationId);   // the whole point of the envelope
        var result = received.Payload;
        result.Kind.ShouldBe(JsonValueKind.Object);
        result.Value.ShouldBeOfType<JsonObject>()["name"]!.GetValue<string>().ShouldBe("flux");
        result.ByteCount.ShouldBeGreaterThan(0);
        result.Encoding.ShouldBe("utf-8");
    }

    [Fact]
    public async Task JsonParse_EmitsErrorAndContinues()
    {
        await using var node = new JsonParseNode();
        var output = Sink(node.Output);
        var errors = Sink(node.Errors);

        var bad = FlowMessage.Create(new JsonParseRequest { Text = "{" });
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create(new JsonParseRequest { Text = """{"ok":true}""" }));

        var error = await errors.ReceiveAsync().WaitAsync(Timeout);
        var result = await output.ReceiveAsync().WaitAsync(Timeout);

        error.Code.ShouldBe(SerializationErrorCodes.JsonParseFailed);
        error.CorrelationId.ShouldBe(bad.CorrelationId);
        result.Payload.Kind.ShouldBe(JsonValueKind.Object);   // later message still flows
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task JsonStringify_StringifiesValue()
    {
        await using var node = new JsonStringifyNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new JsonStringifyRequest
        {
            Value = new Dictionary<string, object?> { ["ok"] = true }
        }));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.Text.ShouldBe("""{"ok":true}""");
        result.Bytes.ShouldBe(Encoding.UTF8.GetBytes(result.Text));
        result.ByteCount.ShouldBe(result.Bytes.Length);
    }

    [Fact]
    public async Task TextEncode_EmitsBytes()
    {
        await using var node = new TextEncodeNode();
        var output = Sink(node.Output);

        var request = FlowMessage.Create(new TextEncodeRequest { Text = "hello" });
        await node.Input.SendAsync(request);

        var received = await output.ReceiveAsync().WaitAsync(Timeout);
        received.CorrelationId.ShouldBe(request.CorrelationId);
        var result = received.Payload;
        result.Bytes.ShouldBe(Encoding.UTF8.GetBytes("hello"));
        result.ByteCount.ShouldBe(5);
        result.Encoding.ShouldBe("utf-8");
    }

    [Fact]
    public async Task TextDecode_SkipsByteOrderMark()
    {
        await using var node = new TextDecodeNode(
            new SerializationNodeOptions { DefaultEncoding = "utf-16" });
        var output = Sink(node.Output);
        var encoding = Encoding.Unicode;
        var bytes = encoding.GetPreamble()
            .Concat(encoding.GetBytes("hello"))
            .ToArray();

        await node.Input.SendAsync(FlowMessage.Create(new TextDecodeRequest { Bytes = bytes }));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.Text.ShouldBe("hello");
        result.Encoding.ShouldBe("utf-16");
        result.ByteCount.ShouldBe(bytes.Length);
    }

    [Fact]
    public async Task Base64Encode_EncodesText()
    {
        await using var node = new Base64EncodeNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new Base64EncodeRequest { Text = "hello" }));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.Text.ShouldBe("aGVsbG8=");
        result.ByteCount.ShouldBe(5);
        result.EncodedLength.ShouldBe(8);
    }

    [Fact]
    public async Task Base64Decode_DecodesText()
    {
        await using var node = new Base64DecodeNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new Base64DecodeRequest
        {
            Text = "aGVsbG8=",
            DecodeText = true
        }));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.Bytes.ShouldBe(Encoding.UTF8.GetBytes("hello"));
        result.ByteCount.ShouldBe(5);
        result.Text.ShouldBe("hello");
        result.Encoding.ShouldBe("utf-8");
    }

    [Fact]
    public async Task TextEncode_EmitsOutputTooLargeErrorAndContinues()
    {
        await using var node = new TextEncodeNode(
            new SerializationNodeOptions { MaxOutputBytes = 2 });
        var output = Sink(node.Output);
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new TextEncodeRequest { Text = "hello" }));
        await node.Input.SendAsync(FlowMessage.Create(new TextEncodeRequest { Text = "ok" }));

        var error = await errors.ReceiveAsync().WaitAsync(Timeout);
        var result = await output.ReceiveAsync().WaitAsync(Timeout);

        error.Code.ShouldBe(SerializationErrorCodes.OutputTooLarge);
        result.Payload.Bytes.ShouldBe(Encoding.UTF8.GetBytes("ok"));
    }

    [Fact]
    public async Task TextEncode_EmitsInputTooLargeErrorAndContinues()
    {
        await using var node = new TextEncodeNode(
            new SerializationNodeOptions { MaxInputBytes = 2, MaxOutputBytes = 10 });
        var output = Sink(node.Output);
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new TextEncodeRequest { Text = "hello" }));
        await node.Input.SendAsync(FlowMessage.Create(new TextEncodeRequest { Text = "ok" }));

        var error = await errors.ReceiveAsync().WaitAsync(Timeout);
        var result = await output.ReceiveAsync().WaitAsync(Timeout);

        error.Code.ShouldBe(SerializationErrorCodes.InputTooLarge);
        result.Payload.Bytes.ShouldBe(Encoding.UTF8.GetBytes("ok"));
    }

    [Fact]
    public async Task Base64Decode_AllowsEmptyText()
    {
        await using var node = new Base64DecodeNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new Base64DecodeRequest { Text = "" }));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.Bytes.ShouldBeEmpty();
        result.ByteCount.ShouldBe(0);
    }

    [Fact]
    public async Task TextDecode_EmitsUnsupportedEncodingError()
    {
        await using var node = new TextDecodeNode();
        var errors = Sink(node.Errors);

        var request = FlowMessage.Create(new TextDecodeRequest
        {
            Bytes = [1, 2, 3],
            Encoding = "missing-encoding"
        });
        await node.Input.SendAsync(request);

        var error = await errors.ReceiveAsync().WaitAsync(Timeout);
        error.Code.ShouldBe(SerializationErrorCodes.UnsupportedEncoding);
        error.CorrelationId.ShouldBe(request.CorrelationId);
    }

    [Fact]
    public async Task Success_EmitsEventCarryingCorrelationIdStampedByClock()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-06-18T12:00:00Z"));
        await using var node = new JsonParseNode(clock: clock);
        Sink(node.Output);
        var events = Sink(node.Events);

        var request = FlowMessage.Create(new JsonParseRequest { Text = """{"ok":true}""" });
        await node.Input.SendAsync(request);

        var @event = await events.ReceiveAsync().WaitAsync(Timeout);
        @event.Name.ShouldBe(SerializationDiagnosticNames.JsonParsed);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.CorrelationId.ShouldBe(request.CorrelationId);
        @event.Timestamp.ShouldBe(clock.GetUtcNow());
        @event.Attributes["kind"].ShouldBe(JsonValueKind.Object.ToString());
    }

    [Fact]
    public async Task Failure_EmitsErrorEventCarryingCorrelationId()
    {
        await using var node = new JsonParseNode();
        var events = Sink(node.Events);

        var request = FlowMessage.Create(new JsonParseRequest { Text = "{" });
        await node.Input.SendAsync(request);

        var @event = await events.ReceiveAsync().WaitAsync(Timeout);
        @event.Name.ShouldBe(SerializationDiagnosticNames.JsonParseFailed);
        @event.Level.ShouldBe(FlowEventLevel.Error);
        @event.CorrelationId.ShouldBe(request.CorrelationId);
    }

    [Fact]
    public async Task Output_FansOutEveryResultToEveryConsumer()
    {
        // The usual workflow case, with NO engine: one node's output linked to two
        // downstream consumers (a "logger" and a "mapper"). Both see every result.
        await using var node = new Base64EncodeNode();
        var logger = Sink(node.Output);
        var mapper = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new Base64EncodeRequest { Text = "a" }));
        await node.Input.SendAsync(FlowMessage.Create(new Base64EncodeRequest { Text = "b" }));

        (await logger.ReceiveAsync().WaitAsync(Timeout)).Payload.Text.ShouldBe("YQ==");
        (await logger.ReceiveAsync().WaitAsync(Timeout)).Payload.Text.ShouldBe("Yg==");
        (await mapper.ReceiveAsync().WaitAsync(Timeout)).Payload.Text.ShouldBe("YQ==");
        (await mapper.ReceiveAsync().WaitAsync(Timeout)).Payload.Text.ShouldBe("Yg==");
    }

    [Fact]
    public void Node_RejectsInvalidOptions()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new JsonParseNode(new SerializationNodeOptions { BoundedCapacity = 0 }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void Node_RejectsInvalidMaxInputBytes()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new JsonParseNode(new SerializationNodeOptions { MaxInputBytes = 0 }));

        exception.Message.ShouldContain("maxInputBytes");
    }

    [Fact]
    public void Node_RejectsInvalidMaxOutputBytes()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new JsonParseNode(new SerializationNodeOptions { MaxOutputBytes = 0 }));

        exception.Message.ShouldContain("maxOutputBytes");
    }

    [Fact]
    public void Node_RejectsEmptyDefaultEncoding()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new JsonParseNode(new SerializationNodeOptions { DefaultEncoding = " " }));

        exception.Message.ShouldContain("defaultEncoding");
    }

    [Fact]
    public void Node_RejectsUnsupportedDefaultEncoding()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new JsonParseNode(new SerializationNodeOptions { DefaultEncoding = "missing-encoding" }));

        exception.Message.ShouldContain("defaultEncoding");
        exception.Message.ShouldContain("not supported");
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = false });
        return sink;
    }
}
