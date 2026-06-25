using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Diagnostics;
using FluxFlow.Components.Payloads.Nodes;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Text;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Payloads.Tests;

// Every test news the node directly — no engine, no registry. Messages travel as
// FlowMessage<T> envelopes; the correlation id flows request -> result for free.
public sealed class PayloadInspectNodeTests
{
    [Fact]
    public async Task Inspect_ClassifiesJsonObjectAndPreservesCorrelationId()
    {
        await using var node = new PayloadInspectNode(
            new PayloadInspectOptions { MaxPreviewBytes = 128 });
        var output = Sink(node.Output);

        var request = FlowMessage.Create(new PayloadInspectionRequest
        {
            Text = """{"name":"flux","count":2}""",
            ContentType = "application/json"
        });
        await node.Input.SendAsync(request);

        var received = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        received.CorrelationId.ShouldBe(request.CorrelationId);   // the whole point of the envelope
        var result = received.Payload;
        result.Kind.ShouldBe(PayloadKind.JsonObject);
        result.ByteCount.ShouldBeGreaterThan(0);
        result.ContentType.ShouldBe("application/json");
        result.DetectedEncoding.ShouldBe("utf-8");
        result.TextPreview.ShouldNotBeNull();
        result.TextPreview.ShouldContain("\"name\"");
        result.FormattedPreview.ShouldNotBeNull();
        result.FormattedPreview.ShouldContain("\n");
        result.ParseError.ShouldBeNull();
    }

    [Fact]
    public async Task Inspect_ClassifiesJsonArrayAndScalar()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest { Text = "[1,2]" }));
        await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest { Text = "42" }));

        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        new[] { first.Payload.Kind, second.Payload.Kind }
            .ShouldBe([PayloadKind.JsonArray, PayloadKind.JsonScalar]);
    }

    [Fact]
    public async Task Inspect_ClassifiesXml()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest
        {
            Text = "<root><value>1</value></root>",
            ContentType = "application/xml"
        }));

        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        result.Kind.ShouldBe(PayloadKind.Xml);
        result.FormattedPreview.ShouldNotBeNull();
        result.FormattedPreview.ShouldContain("<root>");
        result.FormattedPreview.ShouldContain("<value>1</value>");
        result.ParseError.ShouldBeNull();
    }

    [Fact]
    public async Task Inspect_DecodesBytesWithContentTypeCharset()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);
        var bytes = Encoding.Unicode.GetBytes("""{"name":"flux"}""");

        await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest
        {
            Bytes = bytes,
            ContentType = "application/json; charset=utf-16"
        }));

        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        result.Kind.ShouldBe(PayloadKind.JsonObject);
        result.ByteCount.ShouldBe(bytes.Length);
        result.DetectedEncoding.ShouldBe("utf-16");
        result.TextPreview.ShouldNotBeNull();
        result.TextPreview.ShouldContain("\"name\"");
    }

    [Fact]
    public async Task Inspect_ClassifiesBase64()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest
        {
            Text = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"))
        }));

        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        result.Kind.ShouldBe(PayloadKind.Base64);
        result.Base64DecodedByteCount.ShouldBe(5);
        result.FormattedPreview.ShouldBe("hello");
        result.FormattedPreviewTruncated.ShouldBeFalse();
    }

    [Fact]
    public async Task Inspect_ClassifiesBinary()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest
        {
            Bytes = [0x00, 0x9F, 0xFF]
        }));

        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        result.Kind.ShouldBe(PayloadKind.Binary);
        result.ByteCount.ShouldBe(3);
        result.TextPreview.ShouldBeNull();
    }

    [Fact]
    public async Task Inspect_ClassifiesEmpty()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest()));

        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        result.Kind.ShouldBe(PayloadKind.Empty);
        result.ByteCount.ShouldBe(0);
        result.TextPreview.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task Inspect_ReportsParseErrorAsResultMetadata()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest
        {
            Text = "{",
            ContentType = "application/json"
        }));

        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        result.Kind.ShouldBe(PayloadKind.Text);
        result.ParseError.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Inspect_TruncatesPreviews()
    {
        await using var node = new PayloadInspectNode(new PayloadInspectOptions
        {
            MaxPreviewBytes = 3,
            MaxFormattedChars = 10
        });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest
        {
            Text = """{"message":"abcdef"}""",
            ContentType = "application/json"
        }));

        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        result.TextPreview.ShouldBe("""{"m""");
        result.TextPreviewTruncated.ShouldBeTrue();
        result.FormattedPreview.ShouldNotBeNull();
        result.FormattedPreview!.Length.ShouldBe(10);
        result.FormattedPreviewTruncated.ShouldBeTrue();
    }

    [Fact]
    public async Task Inspect_ReportsOversizedTextPayloadWithoutFormatting()
    {
        await using var node = new PayloadInspectNode(new PayloadInspectOptions { MaxInputBytes = 8 });
        var output = Sink(node.Output);
        var errors = Sink(node.Errors);
        var text = """{"name":"flux","count":2}""";

        await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest
        {
            Text = text,
            ContentType = "application/json"
        }));

        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        result.Kind.ShouldBe(PayloadKind.Text);
        result.ByteCount.ShouldBe(Encoding.UTF8.GetByteCount(text));
        result.FormattedPreview.ShouldNotBeNull();
        result.FormattedPreview.ShouldContain("payload too large");
        result.FormattedPreviewTruncated.ShouldBeTrue();
        errors.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Inspect_ReportsOversizedBytePayloadWithoutFormatting()
    {
        await using var node = new PayloadInspectNode(new PayloadInspectOptions { MaxInputBytes = 4 });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest
        {
            Bytes = [1, 2, 3, 4, 5]
        }));

        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        result.Kind.ShouldBe(PayloadKind.Binary);
        result.ByteCount.ShouldBe(5);
        result.FormattedPreview.ShouldNotBeNull();
        result.FormattedPreview.ShouldContain("payload too large");
    }

    [Fact]
    public void Inspect_RejectsInvalidMaxInputBytes()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new PayloadInspectNode(new PayloadInspectOptions { MaxInputBytes = 0 }));

        exception.Message.ShouldContain("maxInputBytes");
    }

    [Fact]
    public void Inspect_RejectsInvalidMaxPreviewBytes()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new PayloadInspectNode(new PayloadInspectOptions { MaxPreviewBytes = 0 }));

        exception.Message.ShouldContain("maxPreviewBytes");
    }

    [Fact]
    public void Inspect_RejectsInvalidMaxFormattedChars()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new PayloadInspectNode(new PayloadInspectOptions { MaxFormattedChars = 0 }));

        exception.Message.ShouldContain("maxFormattedChars");
    }

    [Fact]
    public void Inspect_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new PayloadInspectNode(new PayloadInspectOptions { BoundedCapacity = 0 }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public async Task Inspect_EmitsErrorWithCorrelationIdAndContinues()
    {
        await using var node = new PayloadInspectNode();
        var output = Sink(node.Output);
        var errors = Sink(node.Errors);

        var bad = FlowMessage.Create(new PayloadInspectionRequest
        {
            Text = "hello",
            EncodingHint = "missing-encoding"
        });
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest { Text = "hello" }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;

        error.Code.ShouldBe(PayloadErrorCodes.UnsupportedEncoding);
        error.CorrelationId.ShouldBe(bad.CorrelationId);
        error.Context!.ShouldContain("encodingHint=missing-encoding");
        result.Kind.ShouldBe(PayloadKind.Text);
        result.TextPreview.ShouldBe("hello");

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Inspect_EmitsEventCarryingCorrelationId()
    {
        await using var node = new PayloadInspectNode();
        Sink(node.Output);
        var events = Sink(node.Events);

        var request = FlowMessage.Create(new PayloadInspectionRequest { Text = "hello" });
        await node.Input.SendAsync(request);

        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        @event.Name.ShouldBe(PayloadDiagnosticNames.Inspected);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.CorrelationId.ShouldBe(request.CorrelationId);
        @event.Attributes["kind"].ShouldBe("Text");
        @event.Attributes["byteCount"].ShouldBe(5);
    }

    [Fact]
    public async Task Inspect_StampsResultWithInjectedClock()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero));
        await using var node = new PayloadInspectNode(clock: clock);
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest { Text = "hello" }));

        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        result.Timestamp.ShouldBe(clock.GetUtcNow());
    }

    [Fact]
    public async Task Output_FansOutEveryResultToEveryConsumer()
    {
        // One node's output linked to two downstream consumers (a "logger" and a
        // "mapper"), with NO engine. Both see every result.
        await using var node = new PayloadInspectNode();
        var logger = Sink(node.Output);
        var mapper = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest { Text = "[1]" }));
        await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest { Text = "{}" }));

        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Kind.ShouldBe(PayloadKind.JsonArray);
        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Kind.ShouldBe(PayloadKind.JsonObject);
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Kind.ShouldBe(PayloadKind.JsonArray);
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Kind.ShouldBe(PayloadKind.JsonObject);
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = false });
        return sink;
    }
}
