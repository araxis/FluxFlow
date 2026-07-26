using System.Text.Json;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowMessageTests
{
    [Fact]
    public void Create_ProducesValueCaseWithIdentity()
    {
        var message = FlowMessage.Create("hello");

        message.IsError.ShouldBeFalse();
        message.Value.ShouldBe("hello");
        message.Error.ShouldBeNull();
        message.CorrelationId.ShouldBeNull();
        message.TraceId.IsEmpty.ShouldBeFalse();
        message.MessageId.IsEmpty.ShouldBeFalse();
        message.CausationId.ShouldBeNull();
    }

    [Fact]
    public void CreateError_ProducesErrorCase()
    {
        var error = new FlowError("input.invalid", "Invalid input.", "validation");
        var message = FlowMessage.CreateError<string>(error);

        message.IsError.ShouldBeTrue();
        message.Error.ShouldBeSameAs(error);
        Should.Throw<InvalidOperationException>(() => _ = message.Value);
    }

    [Fact]
    public void Create_AllowsExplicitNullableSuccess()
    {
        var message = FlowMessage.Create<string?>(null);

        message.IsError.ShouldBeFalse();
        message.Value.ShouldBeNull();
        message.Error.ShouldBeNull();
    }

    [Fact]
    public void Match_InvokesOnlyActiveCase()
    {
        var valueCalls = 0;
        var errorCalls = 0;
        var success = FlowMessage.Create(3);
        var failure = FlowMessage.CreateError<int>(
            new FlowError("failed", "Failed.", "processing"));

        success.Match(
            value => { valueCalls++; return value; },
            _ => { errorCalls++; return -1; }).ShouldBe(3);
        failure.Match(
            value => { valueCalls++; return value; },
            _ => { errorCalls++; return -1; }).ShouldBe(-1);

        valueCalls.ShouldBe(1);
        errorCalls.ShouldBe(1);
    }

    [Fact]
    public void With_DerivesValueAndPreservesLineage()
    {
        var correlationId = new CorrelationId("business-1");
        var original = FlowMessage.Create(
            1,
            correlationId,
            new TraceId("trace-1"),
            new Dictionary<string, string> { ["source"] = "orders" });

        var next = original.With("two");

        next.IsError.ShouldBeFalse();
        next.Value.ShouldBe("two");
        next.CorrelationId.ShouldBe(correlationId);
        next.TraceId.ShouldBe(original.TraceId);
        next.Headers.ShouldBeSameAs(original.Headers);
        next.MessageId.ShouldNotBe(original.MessageId);
        next.CausationId.ShouldBe(original.MessageId);
        next.Timestamp.ShouldBeGreaterThanOrEqualTo(original.Timestamp);
    }

    [Fact]
    public void WithError_ForwardsSameErrorAcrossOutputTypes()
    {
        var error = new FlowError("transport.failed", "Transport failed.", "transport", true);
        var original = FlowMessage.CreateError<int>(
            error,
            new CorrelationId("business-1"),
            new TraceId("trace-1"),
            new Dictionary<string, string> { ["attempt"] = "2" });

        var next = original.WithError<Guid>(original.Error!);

        next.IsError.ShouldBeTrue();
        next.Error.ShouldBeSameAs(error);
        next.TraceId.ShouldBe(original.TraceId);
        next.CorrelationId.ShouldBe(original.CorrelationId);
        next.Headers.ShouldBeSameAs(original.Headers);
        next.MessageId.ShouldNotBe(original.MessageId);
        next.CausationId.ShouldBe(original.MessageId);
    }

    [Fact]
    public void Headers_AreCopiedAndUseOrdinalKeys()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Kind"] = "original"
        };
        var message = FlowMessage.Create("payload", headers: headers);

        headers["Kind"] = "changed";
        headers["new"] = "later";

        message.Headers["Kind"].ShouldBe("original");
        message.Headers.ContainsKey("kind").ShouldBeFalse();
        message.Headers.ContainsKey("new").ShouldBeFalse();
    }

    [Fact]
    public void Json_RoundTripsStableSuccessProjection()
    {
        const string json = """
            {"traceId":"trace-9","messageId":"message-9","causationId":"message-8","correlationId":"business-9","timestamp":"2026-07-17T01:02:03+00:00","headers":{"attempt":"2"},"isError":false,"value":"body","error":null}
            """;

        var message = JsonSerializer.Deserialize<FlowMessage<string>>(json).ShouldNotBeNull();

        message.IsError.ShouldBeFalse();
        message.Value.ShouldBe("body");
        message.Headers["attempt"].ShouldBe("2");
        JsonSerializer.Serialize(message).ShouldBe(json);
    }

    [Fact]
    public void Json_RoundTripsStableErrorProjection()
    {
        const string json = """
            {"traceId":"trace-9","messageId":"message-9","causationId":null,"correlationId":null,"timestamp":"2026-07-17T01:02:03+00:00","headers":{},"isError":true,"value":null,"error":{"code":"order.invalid","message":"The order is invalid.","category":"validation","isTransient":false,"details":{"field":"customerId"}}}
            """;

        var message = JsonSerializer.Deserialize<FlowMessage<string>>(json).ShouldNotBeNull();

        message.IsError.ShouldBeTrue();
        message.Error!.Code.ShouldBe("order.invalid");
        message.Error.Details!.Value.GetProperty("field").GetString().ShouldBe("customerId");
        JsonSerializer.Serialize(message).ShouldBe(json);
    }

    [Theory]
    [InlineData("false", "null", "{\"code\":\"bad\",\"message\":\"Bad.\",\"category\":\"test\"}")]
    [InlineData("true", "\"value\"", "{\"code\":\"bad\",\"message\":\"Bad.\",\"category\":\"test\"}")]
    [InlineData("true", "null", "null")]
    public void Json_RejectsContradictoryCases(string isError, string value, string error)
    {
        var json = $$"""
            {"traceId":"trace-1","messageId":"message-1","causationId":null,"correlationId":null,"timestamp":"2026-07-17T01:02:03+00:00","headers":{},"isError":{{isError}},"value":{{value}},"error":{{error}}}
            """;

        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<FlowMessage<string>>(json));
    }

    [Fact]
    public async Task Message_IsSafeForConcurrentReads()
    {
        var message = FlowMessage.Create(
            "payload",
            headers: new Dictionary<string, string> { ["source"] = "test" });

        var reads = Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
            (message.Value, message.Headers["source"], message.TraceId))).ToArray();
        foreach (var read in await Task.WhenAll(reads))
        {
            read.ShouldBe(("payload", "test", message.TraceId));
        }
    }
}
