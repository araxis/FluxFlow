using FluxFlow.Core.Messaging;

namespace FluxFlow.Testing;

internal static class TestMessages
{
    internal static FlowMessage<T> Error<T>(FlowError error, CorrelationId? correlationId = null,
        TraceId? traceId = null, IReadOnlyDictionary<string,FlowValue>? headers = null,
        MessageId? causationId = null) where T : FlowValue
        => new(FlowMessage.Create(error, correlationId, traceId, headers, causationId));

    internal static FlowMessage Restore(FlowValue value, MessageId messageId, TraceId traceId,
        DateTimeOffset timestamp, CorrelationId? correlationId = null, MessageId? causationId = null,
        IReadOnlyDictionary<string,FlowValue>? headers = null)
        => new(value, messageId, traceId, timestamp, correlationId, causationId, headers);

    internal static FlowMessage<T> Restore<T>(T value, MessageId messageId, TraceId traceId,
        DateTimeOffset timestamp, CorrelationId? correlationId = null, MessageId? causationId = null,
        IReadOnlyDictionary<string,FlowValue>? headers = null) where T : FlowValue
        => new(new FlowMessage(value, messageId, traceId, timestamp, correlationId, causationId, headers));

    internal static FlowMessage RestoreError(FlowError error, MessageId messageId, TraceId traceId,
        DateTimeOffset timestamp, CorrelationId? correlationId = null, MessageId? causationId = null,
        IReadOnlyDictionary<string,FlowValue>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(error, messageId, traceId, timestamp, correlationId, causationId, headers);
    }

    internal static FlowMessage<T> RestoreError<T>(FlowError error, MessageId messageId, TraceId traceId,
        DateTimeOffset timestamp, CorrelationId? correlationId = null, MessageId? causationId = null,
        IReadOnlyDictionary<string,FlowValue>? headers = null) where T : FlowValue
        => new(RestoreError(error, messageId, traceId, timestamp, correlationId, causationId, headers));

    internal static FlowMessage ConvertValue(this FlowMessage message, FlowValue value)
        => new(value, message.MessageId, message.TraceId, message.Timestamp,
            message.CorrelationId, message.CausationId, message.Headers);

    internal static FlowMessage<T> ConvertValue<T>(this FlowMessage message, T value) where T : FlowValue
        => new(message, value);

    internal static FlowMessage ConvertError(this FlowMessage message) => message;
    internal static FlowMessage<T> ConvertError<T>(this FlowMessage message) where T : FlowValue => new(message);
}
