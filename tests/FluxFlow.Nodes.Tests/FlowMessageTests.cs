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
        message.MessageId.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Create_HonorsSuppliedCorrelationId()
    {
        var id = new CorrelationId("trace-1");
        FlowMessage.Create("x", id).CorrelationId.ShouldBe(id);
    }

    [Fact]
    public void With_PreservesCorrelationAndHeaders_SwapsPayload_NewMessageId()
    {
        var original = FlowMessage.Create(1) with
        {
            Headers = new Dictionary<string, object?> { ["k"] = "v" }
        };

        var next = original.With("two");

        next.Payload.ShouldBe("two");
        next.CorrelationId.ShouldBe(original.CorrelationId);     // correlation flows forward
        next.Headers["k"].ShouldBe("v");                          // headers carried
        next.MessageId.ShouldNotBe(original.MessageId);           // new hop identity
    }

    [Fact]
    public void Headers_AreCopiedOnAssignment()
    {
        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = "original"
        };

        var message = FlowMessage.Create("payload") with
        {
            Headers = headers
        };

        headers["kind"] = "changed";
        headers["new"] = "later";

        message.Headers["kind"].ShouldBe("original");
        message.Headers.ContainsKey("new").ShouldBeFalse();
    }

    [Fact]
    public void Headers_UseOrdinalKeysAfterAssignment()
    {
        var message = FlowMessage.Create("payload") with
        {
            Headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
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
            Headers = new Dictionary<string, object?>
            {
                ["kind"] = "source"
            }
        };

        var next = original.With("mapped");

        next.Headers.ShouldNotBeSameAs(original.Headers);
        next.Headers["kind"].ShouldBe("source");
    }

    [Fact]
    public void Json_RoundTripsCorrelationIdAndPayload()
    {
        var message = FlowMessage.Create("body", new CorrelationId("trace-9"));

        var json = JsonSerializer.Serialize(message);
        var restored = JsonSerializer.Deserialize<FlowMessage<string>>(json).ShouldNotBeNull();

        restored.CorrelationId.ShouldBe(message.CorrelationId);
        restored.Payload.ShouldBe("body");
    }
}
