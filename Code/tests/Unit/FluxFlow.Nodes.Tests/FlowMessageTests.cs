using System.Text.Json;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowMessageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Envelope_preserves_declared_names_and_payload_and_header_keys(bool generated)
    {
        using var payload = JsonDocument.Parse("""{"MixedCase":1,"lower":2,"snake_case":3}""");
        FlowMessage message = FlowMessage.Create(new JsonFlowValue(payload.RootElement),
            headers: new Dictionary<string, FlowValue> { ["X-Mixed-Case"] = "kept", ["lower"] = "also kept" });
        var json = generated
            ? JsonSerializer.Serialize(message, FlowJsonContext.Default.FlowMessage)
            : JsonSerializer.Serialize(message);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).ShouldBe(
            ["TraceId", "MessageId", "CausationId", "CorrelationId", "Timestamp", "Headers", "IsError", "Value", "Error"]);
        root.GetProperty("Value").GetRawText().ShouldBe(payload.RootElement.GetRawText());
        root.GetProperty("Headers").GetProperty("X-Mixed-Case").GetString().ShouldBe("kept");
        root.GetProperty("Headers").GetProperty("lower").GetString().ShouldBe("also kept");
        var restored = generated
            ? JsonSerializer.Deserialize(json, FlowJsonContext.Default.FlowMessage)!
            : JsonSerializer.Deserialize<FlowMessage>(json)!;
        restored.Value.Json.GetRawText().ShouldBe(payload.RootElement.GetRawText());
        restored.MessageId.ShouldBe(message.MessageId);
        restored.Headers.Keys.Order().ShouldBe(message.Headers.Keys.Order());
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"CausationId\":null,\"CorrelationId\":null,\"Headers\":null")]
    public void Optional_envelope_fields_accept_absence_and_explicit_null(string optionalFields)
    {
        var json = $$"""
            {"TraceId":"trace-9","MessageId":"message-9","Timestamp":"2026-07-17T01:02:03+00:00","IsError":false,"Value":"body","Error":null{{optionalFields}}}
            """;
        var common = JsonSerializer.Deserialize<FlowMessage>(json).ShouldNotBeNull();
        var typed = JsonSerializer.Deserialize<FlowMessage<ScalarValue<string>>>(json).ShouldNotBeNull();
        foreach (FlowMessage message in new FlowMessage[] { common, typed })
        {
            message.CausationId.ShouldBeNull();
            message.CorrelationId.ShouldBeNull();
            message.Headers.ShouldBeEmpty();
            message.MessageId.Value.ShouldBe("message-9");
            message.TraceId.Value.ShouldBe("trace-9");
        }
        common.Value.Json.GetString().ShouldBe("body");
        typed.Value.Data.ShouldBe("body");
    }

    [Fact]
    public void Canonical_message_exposes_non_throwing_script_friendly_cases()
    {
        var success = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { orderId = 42 }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var failure = FlowMessage.Create(
            new FlowError("order.invalid", "Invalid order.", "validation"));

        success.IsError.ShouldBeFalse();
        success.TryGetValue(out var value).ShouldBeTrue();
        value.Json.GetProperty("orderId").GetInt32().ShouldBe(42);
        success.TryGetError(out _).ShouldBeFalse();

        failure.IsError.ShouldBeTrue();
        failure.Value.ShouldBeSameAs(failure.Error);
        failure.TryGetValue(out _).ShouldBeFalse();
        failure.TryGetError(out var error).ShouldBeTrue();
        error.Code.ShouldBe("order.invalid");
    }

    [Fact]
    public void Canonical_message_preserves_explicit_json_null_as_a_success_value()
    {
        var message = FlowMessage.Create(JsonFlowValue.Null);

        message.IsError.ShouldBeFalse();
        message.TryGetValue(out var value).ShouldBeTrue();
        value.ShouldBeSameAs(JsonFlowValue.Null);
        value.Json.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void Canonical_message_json_round_trips_the_flat_expression_contract()
    {
        const string json = """
            {"TraceId":"trace-9","MessageId":"message-9","CausationId":null,"CorrelationId":null,"Timestamp":"2026-07-17T01:02:03+00:00","Headers":{},"IsError":false,"Value":{"orderId":42},"Error":null}
            """;

        var message = JsonSerializer.Deserialize<FlowMessage>(json).ShouldNotBeNull();

        message.IsError.ShouldBeFalse();
        message.TryGetValue(out var value).ShouldBeTrue();
        value.Json.GetProperty("orderId").GetInt32().ShouldBe(42);
        JsonSerializer.Serialize(message).ShouldBe(json);
    }

    [Fact]
    public void Create_ProducesValueCaseWithIdentity()
    {
        var message = FlowMessage.Create(new ScalarValue<string>("hello"));

        message.IsError.ShouldBeFalse();
        message.Value.Data.ShouldBe("hello");
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
        var message = TestMessages.Error<ScalarValue<string>>(error);

        message.IsError.ShouldBeTrue();
        message.Error.ShouldBeSameAs(error);
        Should.Throw<InvalidOperationException>(() => _ = message.Value);
    }

    [Fact]
    public void Create_rejects_null_typed_payload()
    {
        Should.Throw<ArgumentNullException>(() => FlowMessage.Create<ScalarValue<string>>(null!));
    }

    [Fact]
    public void Match_InvokesOnlyActiveCase()
    {
        var valueCalls = 0;
        var errorCalls = 0;
        var success = FlowMessage.Create(new ScalarValue<int>(3));
        var failure = TestMessages.Error<ScalarValue<int>>(
            new FlowError("failed", "Failed.", "processing"));

        success.Match<int>(
            value => { valueCalls++; return value; },
            _ => { errorCalls++; return -1; }).ShouldBe(3);
        failure.Match<int>(
            value => { valueCalls++; return value; },
            _ => { errorCalls++; return -1; }).ShouldBe(-1);

        valueCalls.ShouldBe(1);
        errorCalls.ShouldBe(1);
    }

    [Fact]
    public void With_DerivesValueAndPreservesLineage()
    {
        var correlationId = new CorrelationId("business-1");
        var original = FlowMessage.Create(new ScalarValue<int>(1),
            correlationId,
            new TraceId("trace-1"),
            new Dictionary<string, FlowValue> { ["source"] = "orders" });

        var next = original.WithValue(new ScalarValue<string>("two"));

        next.IsError.ShouldBeFalse();
        next.Value.Data.ShouldBe("two");
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
        var original = TestMessages.Error<ScalarValue<int>>(
            error,
            new CorrelationId("business-1"),
            new TraceId("trace-1"),
            new Dictionary<string, FlowValue> { ["attempt"] = "2" });

        var next = original.WithValue(original.Error!);

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
        var headers = new Dictionary<string, FlowValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["Kind"] = "original"
        };
        var message = FlowMessage.Create(new ScalarValue<string>("payload"), headers: headers);

        headers["Kind"] = "changed";
        headers["new"] = "later";

        message.Headers["Kind"].Json.GetString().ShouldBe("original");
        message.Headers.ContainsKey("kind").ShouldBeFalse();
        message.Headers.ContainsKey("new").ShouldBeFalse();
    }

    [Fact]
    public void Json_RoundTripsStableSuccessProjection()
    {
        const string json = """
            {"TraceId":"trace-9","MessageId":"message-9","CausationId":"message-8","CorrelationId":"business-9","Timestamp":"2026-07-17T01:02:03+00:00","Headers":{"attempt":"2"},"IsError":false,"Value":"body","Error":null}
            """;

        var message = JsonSerializer.Deserialize<FlowMessage<ScalarValue<string>>>(json).ShouldNotBeNull();

        message.IsError.ShouldBeFalse();
        message.Value.Data.ShouldBe("body");
        message.Headers["attempt"].Json.GetString().ShouldBe("2");
        JsonSerializer.Serialize(message).ShouldBe(json);
    }

    [Fact]
    public void Json_RoundTripsStableErrorProjection()
    {
        const string json = """
            {"TraceId":"trace-9","MessageId":"message-9","CausationId":null,"CorrelationId":null,"Timestamp":"2026-07-17T01:02:03+00:00","Headers":{},"IsError":true,"Value":null,"Error":{"Code":"order.invalid","Message":"The order is invalid.","Category":"validation","IsTransient":false,"Details":{"field":"customerId"}}}
            """;

        var message = JsonSerializer.Deserialize<FlowMessage<ScalarValue<string>>>(json).ShouldNotBeNull();

        message.IsError.ShouldBeTrue();
        message.Error!.Code.ShouldBe("order.invalid");
        message.Error.Details!.Json.GetProperty("field").GetString().ShouldBe("customerId");
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
        var headers = new Dictionary<string, FlowValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["Source"] = "durable-input"
        };

        var message = TestMessages.Restore(new ScalarValue<string>("payload"),
            messageId,
            traceId,
            timestamp,
            correlationId,
            causationId,
            headers);
        headers["Source"] = "changed";
        headers["later"] = "ignored";

        message.IsError.ShouldBeFalse();
        message.Value.Data.ShouldBe("payload");
        message.Error.ShouldBeNull();
        message.MessageId.ShouldBe(messageId);
        message.TraceId.ShouldBe(traceId);
        message.Timestamp.ShouldBe(timestamp);
        message.CorrelationId.ShouldBe(correlationId);
        message.CausationId.ShouldBe(causationId);
        message.Headers.Count.ShouldBe(1);
        message.Headers["Source"].Json.GetString().ShouldBe("durable-input");
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
            details: new JsonFlowValue(JsonSerializer.SerializeToElement(new { field = "customerId" })));
        var headers = new Dictionary<string, FlowValue> { ["attempt"] = "3" };

        var message = TestMessages.RestoreError<ScalarValue<string>>(
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
        message.Error!.Details!.Json.GetProperty("field").GetString().ShouldBe("customerId");
        Should.Throw<InvalidOperationException>(() => _ = message.Value);
        message.MessageId.ShouldBe(messageId);
        message.TraceId.ShouldBe(traceId);
        message.Timestamp.ShouldBe(timestamp);
        message.CorrelationId.ShouldBe(correlationId);
        message.CausationId.ShouldBe(causationId);
        message.Headers.Count.ShouldBe(1);
        message.Headers["attempt"].Json.GetString().ShouldBe("3");
    }

    [Fact]
    public void Restore_factories_reject_missing_persisted_identity_or_error()
    {
        var timestamp = new DateTimeOffset(2026, 7, 29, 8, 17, 30, TimeSpan.Zero);
        var messageId = new MessageId("message-valid");
        var traceId = new TraceId("trace-valid");

        Should.Throw<ArgumentException>(() =>
                TestMessages.Restore(new ScalarValue<string>("payload"), default(MessageId), traceId, timestamp))
            .ParamName.ShouldBe("messageId");
        Should.Throw<ArgumentException>(() =>
                TestMessages.Restore(new ScalarValue<string>("payload"), messageId, default(TraceId), timestamp))
            .ParamName.ShouldBe("traceId");
        Should.Throw<ArgumentNullException>(() =>
                TestMessages.RestoreError<ScalarValue<string>>(null!, messageId, traceId, timestamp))
            .ParamName.ShouldBe("error");
    }

    [Theory]
    [InlineData("false", "null", "{\"Code\":\"bad\",\"Message\":\"Bad.\",\"Category\":\"test\"}")]
    [InlineData("true", "\"value\"", "{\"Code\":\"bad\",\"Message\":\"Bad.\",\"Category\":\"test\"}")]
    [InlineData("true", "null", "null")]
    public void Json_RejectsContradictoryCases(string isError, string value, string error)
    {
        var json = $$"""
            {"TraceId":"trace-1","MessageId":"message-1","CausationId":null,"CorrelationId":null,"Timestamp":"2026-07-17T01:02:03+00:00","Headers":{},"IsError":{{isError}},"Value":{{value}},"Error":{{error}}}
            """;

        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<FlowMessage<ScalarValue<string>>>(json));
    }

    [Fact]
    public async Task Message_IsSafeForConcurrentReads()
    {
        var message = FlowMessage.Create(new ScalarValue<string>("payload"),
            headers: new Dictionary<string, FlowValue> { ["source"] = "test" });

        var reads = Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
            (message.Value.Data, message.Headers["source"].Json.GetString(), message.TraceId))).ToArray();
        foreach (var read in await Task.WhenAll(reads))
        {
            read.ShouldBe(("payload", "test", message.TraceId));
        }
    }
}
