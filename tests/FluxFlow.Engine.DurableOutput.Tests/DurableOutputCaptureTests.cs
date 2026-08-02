using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

public sealed class DurableOutputCaptureTests
{
    [Theory]
    [InlineData(DurableOutputEnqueueStatus.Enqueued)]
    [InlineData(DurableOutputEnqueueStatus.AlreadyExists)]
    public async Task Accepted_store_status_persists_exact_value_envelope(
        DurableOutputEnqueueStatus status)
    {
        var clock = new FakeTimeProvider(DurableOutputTestData.CapturedAt);
        var store = new RecordingDurableOutputStore { Status = status };
        var capture = new DurableOutputCapture<string>(
            DurableOutputTestData.Output,
            "text-v1",
            DurableOutputTestData.TypeInfo<string>(),
            store,
            clock);
        var headers = new Dictionary<string, string> { ["source"] = "orders" };
        var message = FlowMessage.Restore(
            "captured payload",
            new MessageId("message-42"),
            new TraceId("trace-42"),
            DurableOutputTestData.MessageTimestamp,
            new CorrelationId("order-42"),
            new MessageId("cause-41"),
            headers);
        using var cancellation = new CancellationTokenSource();

        await capture.CaptureAsync(message, cancellation.Token);

        var envelope = store.Envelopes.ShouldHaveSingleItem();
        envelope.Address.ShouldBe(DurableOutputTestData.Output);
        envelope.ContractName.ShouldBe("text-v1");
        envelope.IsError.ShouldBeFalse();
        envelope.Payload.GetString().ShouldBe("captured payload");
        envelope.Error.ShouldBeNull();
        envelope.MessageId.ShouldBe(message.MessageId);
        envelope.TraceId.ShouldBe(message.TraceId);
        envelope.Timestamp.ShouldBe(message.Timestamp);
        envelope.CapturedAt.ShouldBe(DurableOutputTestData.CapturedAt);
        envelope.CorrelationId.ShouldBe(message.CorrelationId);
        envelope.CausationId.ShouldBe(message.CausationId);
        envelope.Headers.ShouldBe(message.Headers);
        envelope.SchemaVersion.ShouldBe(DurableOutputEnvelope.CurrentSchemaVersion);
        store.CancellationTokens.ShouldHaveSingleItem().ShouldBe(cancellation.Token);
    }

    [Fact]
    public async Task Error_message_persists_null_payload_and_complete_error_identity()
    {
        var store = new RecordingDurableOutputStore();
        var capture = Capture(store);
        var error = DurableOutputTestData.Error();
        var message = FlowMessage.RestoreError<string>(
            error,
            new MessageId("error-message"),
            new TraceId("error-trace"),
            DurableOutputTestData.MessageTimestamp,
            new CorrelationId("error-correlation"),
            new MessageId("error-cause"),
            new Dictionary<string, string> { ["source"] = "validation" });

        await capture.CaptureAsync(message);

        var envelope = store.Envelopes.ShouldHaveSingleItem();
        envelope.IsError.ShouldBeTrue();
        envelope.Payload.ValueKind.ShouldBe(JsonValueKind.Null);
        envelope.Error.ShouldBeSameAs(error);
        envelope.MessageId.ShouldBe(message.MessageId);
        envelope.TraceId.ShouldBe(message.TraceId);
        envelope.Timestamp.ShouldBe(message.Timestamp);
        envelope.CorrelationId.ShouldBe(message.CorrelationId);
        envelope.CausationId.ShouldBe(message.CausationId);
        envelope.Headers.ShouldBe(message.Headers);
    }

    [Fact]
    public async Task Conflict_surfaces_failure_after_one_store_call()
    {
        var store = new RecordingDurableOutputStore
        {
            Status = DurableOutputEnqueueStatus.Conflict
        };
        var capture = Capture(store);
        var message = FlowMessage.Create("conflicting");

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await capture.CaptureAsync(message));

        exception.Message.ShouldContain("conflicts with different persisted content");
        exception.Message.ShouldContain(DurableOutputTestData.Output.Value);
        exception.Message.ShouldContain(message.MessageId.Value);
        store.Envelopes.ShouldHaveSingleItem().MessageId.ShouldBe(message.MessageId);
    }

    [Fact]
    public async Task Store_failure_is_propagated_without_fallback()
    {
        var expected = new IOException("store unavailable");
        var store = new RecordingDurableOutputStore { EnqueueException = expected };
        var capture = Capture(store);
        var message = FlowMessage.Create("store-failure");

        var failure = await Should.ThrowAsync<IOException>(async () =>
            await capture.CaptureAsync(message));

        failure.ShouldBeSameAs(expected);
        store.Envelopes.ShouldHaveSingleItem().MessageId.ShouldBe(message.MessageId);
    }

    [Fact]
    public async Task Serialization_failure_occurs_before_store_enqueue()
    {
        var expected = new InvalidOperationException("serialization failed");
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        options.Converters.Add(new ThrowingConverter<UnserializablePayload>(expected));
        var typeInfo = (JsonTypeInfo<UnserializablePayload>)
            options.GetTypeInfo(typeof(UnserializablePayload));
        var store = new RecordingDurableOutputStore();
        var capture = new DurableOutputCapture<UnserializablePayload>(
            DurableOutputTestData.Output,
            "unserializable-v1",
            typeInfo,
            store,
            new FakeTimeProvider(DurableOutputTestData.CapturedAt));
        var message = FlowMessage.Create(new UnserializablePayload("value"));

        var failure = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await capture.CaptureAsync(message));

        failure.ShouldBeSameAs(expected);
        store.Envelopes.ShouldBeEmpty();
        store.CancellationTokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task Null_or_mismatched_store_result_is_rejected()
    {
        var message = FlowMessage.Create("invalid-result");
        var nullStore = new RecordingDurableOutputStore { ReturnNull = true };
        var mismatchedStore = new RecordingDurableOutputStore
        {
            ResultKey = new DurableOutputKey(
                DurableOutputTestData.SecondOutput,
                message.MessageId)
        };

        var nullFailure = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await Capture(nullStore).CaptureAsync(message));
        var mismatchedFailure = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await Capture(mismatchedStore).CaptureAsync(message));

        nullFailure.Message.ShouldContain("different key");
        mismatchedFailure.Message.ShouldContain("different key");
        nullStore.Envelopes.ShouldHaveSingleItem();
        mismatchedStore.Envelopes.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Pre_canceled_capture_does_not_reach_store_commit()
    {
        var store = new RecordingDurableOutputStore();
        var capture = Capture(store);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await capture.CaptureAsync(FlowMessage.Create("canceled"), canceled.Token));

        store.Envelopes.ShouldBeEmpty();
        store.CancellationTokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task Capture_rejects_null_message_before_serialization_or_store_access()
    {
        var store = new RecordingDurableOutputStore();

        var exception = await Should.ThrowAsync<ArgumentNullException>(async () =>
            await Capture(store).CaptureAsync(null!));

        exception.ParamName.ShouldBe("message");
        store.Envelopes.ShouldBeEmpty();
    }

    private static DurableOutputCapture<string> Capture(RecordingDurableOutputStore store)
        => new(
            DurableOutputTestData.Output,
            "text-v1",
            DurableOutputTestData.TypeInfo<string>(),
            store,
            new FakeTimeProvider(DurableOutputTestData.CapturedAt));

    private sealed record UnserializablePayload(string Value);

    private sealed class ThrowingConverter<T>(Exception exception) : JsonConverter<T>
    {
        public override T? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
            => throw new NotSupportedException();

        public override void Write(
            Utf8JsonWriter writer,
            T value,
            JsonSerializerOptions options)
            => throw exception;
    }
}
