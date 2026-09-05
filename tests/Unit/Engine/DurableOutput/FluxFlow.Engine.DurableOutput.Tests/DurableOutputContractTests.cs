using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

public sealed class DurableOutputContractTests
{
    [Fact]
    public void Envelope_preserves_complete_identity_and_owns_payload_and_headers()
    {
        using var document = JsonDocument.Parse("{\"value\":42}");
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = "original"
        };

        var envelope = DurableOutputTestData.Envelope(
            document.RootElement,
            headers: headers,
            schemaVersion: 3);
        headers["source"] = "changed";
        document.Dispose();

        envelope.Address.ShouldBe(DurableOutputTestData.Output);
        envelope.ContractName.ShouldBe("text-v1");
        envelope.IsError.ShouldBeFalse();
        envelope.Payload.GetProperty("value").GetInt32().ShouldBe(42);
        envelope.Error.ShouldBeNull();
        envelope.MessageId.ShouldBe(new MessageId("message-1"));
        envelope.TraceId.ShouldBe(new TraceId("trace-1"));
        envelope.Timestamp.ShouldBe(DurableOutputTestData.MessageTimestamp);
        envelope.CapturedAt.ShouldBe(DurableOutputTestData.CapturedAt);
        envelope.CorrelationId.ShouldBe(new CorrelationId("order-1"));
        envelope.CausationId.ShouldBe(new MessageId("cause-1"));
        envelope.Headers.ShouldBe(new Dictionary<string, string> { ["source"] = "original" });
        envelope.SchemaVersion.ShouldBe(3);
        envelope.Key.ShouldBe(new DurableOutputKey(
            DurableOutputTestData.Output,
            new MessageId("message-1")));
        Should.Throw<NotSupportedException>(() =>
            ((IDictionary<string, string>)envelope.Headers).Add("new", "value"));
    }

    [Fact]
    public void Envelope_normalizes_empty_optional_identity_and_headers()
    {
        var envelope = new DurableOutputEnvelope(
            DurableOutputTestData.Output,
            "text-v1",
            isError: false,
            JsonSerializer.SerializeToElement("payload"),
            error: null,
            new MessageId("message-1"),
            new TraceId("trace-1"),
            DurableOutputTestData.MessageTimestamp,
            DurableOutputTestData.CapturedAt,
            default(CorrelationId),
            default(MessageId),
            headers: null);

        envelope.CorrelationId.ShouldBeNull();
        envelope.CausationId.ShouldBeNull();
        envelope.Headers.ShouldBeEmpty();
        envelope.SchemaVersion.ShouldBe(DurableOutputEnvelope.CurrentSchemaVersion);
    }

    [Fact]
    public void Error_envelope_requires_error_and_null_payload()
    {
        var error = DurableOutputTestData.Error();
        var nullPayload = JsonSerializer.SerializeToElement<object?>(null);

        var envelope = new DurableOutputEnvelope(
            DurableOutputTestData.Output,
            "error-v1",
            isError: true,
            nullPayload,
            error,
            new MessageId("error-message"),
            new TraceId("error-trace"),
            DurableOutputTestData.MessageTimestamp,
            DurableOutputTestData.CapturedAt);

        envelope.IsError.ShouldBeTrue();
        envelope.Payload.ValueKind.ShouldBe(JsonValueKind.Null);
        envelope.Error.ShouldBeSameAs(error);

        Should.Throw<ArgumentException>(() => new DurableOutputEnvelope(
            DurableOutputTestData.Output,
            "error-v1",
            isError: true,
            nullPayload,
            error: null,
            new MessageId("error-message"),
            new TraceId("error-trace"),
            DurableOutputTestData.MessageTimestamp,
            DurableOutputTestData.CapturedAt)).ParamName.ShouldBe("error");
        Should.Throw<ArgumentException>(() => new DurableOutputEnvelope(
            DurableOutputTestData.Output,
            "error-v1",
            isError: true,
            JsonSerializer.SerializeToElement("not-null"),
            error,
            new MessageId("error-message"),
            new TraceId("error-trace"),
            DurableOutputTestData.MessageTimestamp,
            DurableOutputTestData.CapturedAt)).ParamName.ShouldBe("payload");
        Should.Throw<ArgumentException>(() => new DurableOutputEnvelope(
            DurableOutputTestData.Output,
            "value-v1",
            isError: false,
            JsonSerializer.SerializeToElement("value"),
            error,
            new MessageId("message-1"),
            new TraceId("trace-1"),
            DurableOutputTestData.MessageTimestamp,
            DurableOutputTestData.CapturedAt)).ParamName.ShouldBe("error");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" text-v1")]
    [InlineData("text-v1 ")]
    public void Envelope_rejects_invalid_contract_names(string contractName)
    {
        var exception = Should.Throw<ArgumentException>(() =>
            DurableOutputTestData.Envelope(contractName: contractName));

        exception.ParamName.ShouldBe("contractName");
    }

    [Fact]
    public void Envelope_rejects_null_contract_name()
    {
        var exception = Should.Throw<ArgumentNullException>(() =>
            DurableOutputTestData.Envelope(contractName: null!));

        exception.ParamName.ShouldBe("contractName");
    }

    [Fact]
    public void Envelope_rejects_non_workflow_addresses_and_empty_identity()
    {
        Should.Throw<ArgumentNullException>(() => new DurableOutputEnvelope(
            null!,
            "text-v1",
            isError: false,
            JsonSerializer.SerializeToElement("payload"),
            error: null,
            new MessageId("message-1"),
            new TraceId("trace-1"),
            DurableOutputTestData.MessageTimestamp,
            DurableOutputTestData.CapturedAt));
        Should.Throw<ArgumentException>(() =>
            DurableOutputTestData.Envelope(address: ApplicationAddress.Resource("store")))
            .ParamName.ShouldBe("address");
        Should.Throw<ArgumentException>(() =>
            DurableOutputTestData.Envelope(messageId: default(MessageId)))
            .ParamName.ShouldBe("messageId");
        Should.Throw<ArgumentException>(() =>
            DurableOutputTestData.Envelope(traceId: default(TraceId)))
            .ParamName.ShouldBe("traceId");
    }

    [Fact]
    public void Envelope_rejects_undefined_payload_and_nonpositive_schema_versions()
    {
        Should.Throw<ArgumentException>(() =>
            DurableOutputTestData.Envelope(payload: default(JsonElement)))
            .ParamName.ShouldBe("payload");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            DurableOutputTestData.Envelope(schemaVersion: 0))
            .ParamName.ShouldBe("schemaVersion");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            DurableOutputTestData.Envelope(schemaVersion: -1))
            .ParamName.ShouldBe("schemaVersion");
    }

    [Fact]
    public void Envelope_rejects_invalid_headers()
    {
        Should.Throw<ArgumentException>(() => DurableOutputTestData.Envelope(
            headers: new Dictionary<string, string> { [""] = "value" }));
        Should.Throw<ArgumentException>(() => DurableOutputTestData.Envelope(
            headers: new Dictionary<string, string> { [" "] = "value" }));
        Should.Throw<ArgumentException>(() => DurableOutputTestData.Envelope(
            headers: new Dictionary<string, string> { ["key"] = null! }))
            .ParamName.ShouldBe("headers");
    }

    [Fact]
    public void Key_requires_complete_identity_and_has_stable_text()
    {
        var key = new DurableOutputKey(
            DurableOutputTestData.Output,
            new MessageId("message-1"));

        key.Address.ShouldBe(DurableOutputTestData.Output);
        key.MessageId.ShouldBe(new MessageId("message-1"));
        key.ToString().ShouldBe($"{DurableOutputTestData.Output}/message-1");
        Should.Throw<ArgumentNullException>(() =>
            new DurableOutputKey(null!, new MessageId("message-1")));
        Should.Throw<ArgumentException>(() =>
            new DurableOutputKey(DurableOutputTestData.Output, default))
            .ParamName.ShouldBe("messageId");
    }

    [Theory]
    [InlineData(DurableOutputEnqueueStatus.Enqueued, true)]
    [InlineData(DurableOutputEnqueueStatus.AlreadyExists, true)]
    [InlineData(DurableOutputEnqueueStatus.Conflict, false)]
    public void Enqueue_result_exposes_exact_status_and_acceptance(
        DurableOutputEnqueueStatus status,
        bool isAccepted)
    {
        var key = DurableOutputTestData.Envelope().Key;

        var result = new DurableOutputEnqueueResult(key, status);

        result.Key.ShouldBe(key);
        result.Status.ShouldBe(status);
        result.IsAccepted.ShouldBe(isAccepted);
    }

    [Fact]
    public void Enqueue_result_rejects_default_key_and_unknown_status()
    {
        Should.Throw<ArgumentException>(() =>
            new DurableOutputEnqueueResult(default, DurableOutputEnqueueStatus.Enqueued))
            .ParamName.ShouldBe("key");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new DurableOutputEnqueueResult(
                DurableOutputTestData.Envelope().Key,
                (DurableOutputEnqueueStatus)99))
            .ParamName.ShouldBe("status");
    }
}
