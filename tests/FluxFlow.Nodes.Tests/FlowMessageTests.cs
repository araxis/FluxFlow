using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using System.Text.Json;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowMessageTests
{
    [Fact]
    public void Create_AssignsCorrelationIdAndPayload()
    {
        var message = FlowMessage.Create("hello");

        message.Payload.ShouldBe("hello");
        message.CorrelationId.IsEmpty.ShouldBeFalse();
        message.TraceId.IsEmpty.ShouldBeFalse();
        message.MessageId.IsEmpty.ShouldBeFalse();
        message.CausationId.ShouldBeNull();
    }

    [Fact]
    public void Create_HonorsSuppliedCorrelationId()
    {
        var id = new CorrelationId("trace-1");
        FlowMessage.Create("x", id).CorrelationId.ShouldBe(id);
    }

    [Fact]
    public void Create_HonorsSuppliedTraceId()
    {
        var traceId = new TraceId("trace-1");

        FlowMessage.Create("x", traceId: traceId).TraceId.ShouldBe(traceId);
    }

    [Fact]
    public void Create_ReplacesDefaultStructIdentifiers()
    {
        var message = FlowMessage.Create(
            "x",
            correlationId: default(CorrelationId),
            traceId: default(TraceId));

        message.CorrelationId.IsEmpty.ShouldBeFalse();
        message.TraceId.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void With_PreservesCorrelationAndHeaders_SwapsPayload_NewMessageId()
    {
        var original = FlowMessage.Create(1) with
        {
            Headers = new Dictionary<string, FlowValue> { ["k"] = "v" }
        };

        var next = original.With("two");

        next.Payload.ShouldBe("two");
        next.CorrelationId.ShouldBe(original.CorrelationId);
        next.TraceId.ShouldBe(original.TraceId);
        next.Headers["k"].GetString().ShouldBe("v");
        next.MessageId.ShouldNotBe(original.MessageId);
        next.CausationId.ShouldBe(original.MessageId);
    }

    [Fact]
    public void Headers_AreCopiedOnAssignment()
    {
        var headers = new Dictionary<string, FlowValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = "original"
        };

        var message = FlowMessage.Create("payload") with
        {
            Headers = headers
        };

        headers["kind"] = "changed";
        headers["new"] = "later";

        message.Headers["kind"].GetString().ShouldBe("original");
        message.Headers.ContainsKey("new").ShouldBeFalse();
    }

    [Fact]
    public void Headers_UseOrdinalKeysAfterAssignment()
    {
        var message = FlowMessage.Create("payload") with
        {
            Headers = new Dictionary<string, FlowValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["Kind"] = "event"
            }
        };

        message.Headers.ContainsKey("Kind").ShouldBeTrue();
        message.Headers.ContainsKey("kind").ShouldBeFalse();
    }

    [Fact]
    public void With_CopiesHeadersIntoNextMessage()
    {
        var original = FlowMessage.Create(1) with
        {
            Headers = new Dictionary<string, FlowValue>
            {
                ["kind"] = "source"
            }
        };

        var next = original.With("mapped");

        next.Headers.ShouldBeSameAs(original.Headers);
        next.Headers["kind"].GetString().ShouldBe("source");
    }

    [Fact]
    public void With_AdvancesTimestamp()
    {
        var original = FlowMessage.Create("first") with
        {
            Timestamp = DateTimeOffset.UnixEpoch
        };

        var next = original.With("next");

        next.Timestamp.ShouldBeGreaterThan(original.Timestamp);
    }

    [Fact]
    public void Json_RoundTripsEnvelopeIdentityHeadersAndPayload()
    {
        var message = FlowMessage.Create(
            "body",
            new CorrelationId("correlation-9"),
            new TraceId("trace-9")) with
        {
            Headers = new Dictionary<string, FlowValue>
            {
                ["attempt"] = FlowValue.From(2L)
            }
        };

        var json = JsonSerializer.Serialize(message);
        var restored = JsonSerializer.Deserialize<FlowMessage<string>>(json).ShouldNotBeNull();

        restored.CorrelationId.ShouldBe(message.CorrelationId);
        restored.TraceId.ShouldBe(message.TraceId);
        restored.MessageId.ShouldBe(message.MessageId);
        restored.Headers["attempt"].GetInteger().ShouldBe(2);
        restored.Payload.ShouldBe("body");
    }

    [Fact]
    public void JsonContractIsStable()
    {
        var message = new FlowMessage<string>(new CorrelationId("correlation-9"), "body")
        {
            TraceId = new TraceId("trace-9"),
            MessageId = new MessageId("message-9"),
            CausationId = new MessageId("message-8"),
            Timestamp = new DateTimeOffset(2026, 7, 17, 1, 2, 3, TimeSpan.Zero),
            Headers = new Dictionary<string, FlowValue>
            {
                ["attempt"] = FlowValue.From(2L)
            }
        };

        JsonSerializer.Serialize(message).ShouldBe(
            "{\"CorrelationId\":\"correlation-9\",\"Payload\":\"body\"," +
            "\"TraceId\":\"trace-9\",\"MessageId\":\"message-9\"," +
            "\"CausationId\":\"message-8\",\"Timestamp\":\"2026-07-17T01:02:03+00:00\"," +
            "\"Headers\":{\"attempt\":{\"kind\":\"integer\",\"value\":\"2\"}}}");
    }
}
