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

    [Fact]
    public void Restore_PreservesPersistedValueIdentityAndCopiesHeaders()
    {
        var timestamp = new DateTimeOffset(2026, 7, 29, 8, 15, 30, TimeSpan.Zero);
        var messageId = new MessageId("message-restored");
        var traceId = new TraceId("trace-restored");
        var correlationId = new CorrelationId("order-42");
        var causationId = new MessageId("message-cause");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Source"] = "durable-input"
        };

        var message = FlowMessage.Restore(
            "payload",
            messageId,
            traceId,
            timestamp,
            correlationId,
            causationId,
            headers);
        headers["Source"] = "changed";
        headers["later"] = "ignored";

        message.IsError.ShouldBeFalse();
        message.Value.ShouldBe("payload");
        message.Error.ShouldBeNull();
        message.MessageId.ShouldBe(messageId);
        message.TraceId.ShouldBe(traceId);
        message.Timestamp.ShouldBe(timestamp);
        message.CorrelationId.ShouldBe(correlationId);
        message.CausationId.ShouldBe(causationId);
        message.Headers.ShouldBe(new Dictionary<string, string> { ["Source"] = "durable-input" });
        message.Headers.ContainsKey("source").ShouldBeFalse();
        message.Headers.ContainsKey("later").ShouldBeFalse();
    }

    [Fact]
    public void RestoreError_PreservesPersistedErrorIdentityAndCopiesHeaders()
    {
        var timestamp = new DateTimeOffset(2026, 7, 29, 8, 16, 30, TimeSpan.Zero);
        var messageId = new MessageId("message-error");
        var traceId = new TraceId("trace-error");
        var correlationId = new CorrelationId("order-43");
        var causationId = new MessageId("message-cause");
        var error = new FlowError(
            "order.invalid",
            "Invalid order.",
            "validation",
            details: JsonSerializer.SerializeToElement(new { field = "customerId" }));
        var headers = new Dictionary<string, string> { ["attempt"] = "3" };

        var message = FlowMessage.RestoreError<string>(
            error,
            messageId,
            traceId,
            timestamp,
            correlationId,
            causationId,
            headers);
        headers["attempt"] = "4";

        message.IsError.ShouldBeTrue();
        message.Error.ShouldBeSameAs(error);
        message.Error!.Details!.Value.GetProperty("field").GetString().ShouldBe("customerId");
        Should.Throw<InvalidOperationException>(() => _ = message.Value);
        message.MessageId.ShouldBe(messageId);
        message.TraceId.ShouldBe(traceId);
        message.Timestamp.ShouldBe(timestamp);
        message.CorrelationId.ShouldBe(correlationId);
        message.CausationId.ShouldBe(causationId);
        message.Headers.ShouldBe(new Dictionary<string, string> { ["attempt"] = "3" });
    }

    [Fact]
    public void Restore_factories_reject_missing_persisted_identity_or_error()
    {
        var timestamp = new DateTimeOffset(2026, 7, 29, 8, 17, 30, TimeSpan.Zero);
        var messageId = new MessageId("message-valid");
        var traceId = new TraceId("trace-valid");

        Should.Throw<ArgumentException>(() =>
                FlowMessage.Restore("payload", default(MessageId), traceId, timestamp))
            .ParamName.ShouldBe("messageId");
        Should.Throw<ArgumentException>(() =>
                FlowMessage.Restore("payload", messageId, default(TraceId), timestamp))
            .ParamName.ShouldBe("traceId");
        Should.Throw<ArgumentNullException>(() =>
                FlowMessage.RestoreError<string>(null!, messageId, traceId, timestamp))
            .ParamName.ShouldBe("error");
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
