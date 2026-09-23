using System.Collections.Immutable;
using FluxFlow.Core.Nodes;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace FluxFlow.Core.Messaging;

/// <summary>A payload and its identity, lineage and accompanying headers.</summary>
[JsonConverter(typeof(FlowMessageJsonConverter))]
public record FlowMessage
{
    // Used by serialization and runtime adapters to preserve an existing identity.
    internal FlowMessage(FlowValue value, MessageId messageId, TraceId traceId,
        DateTimeOffset timestamp, CorrelationId? correlationId = null,
        MessageId? causationId = null, IReadOnlyDictionary<string, FlowValue>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (messageId.IsEmpty) throw new ArgumentException("Message id cannot be empty.", nameof(messageId));
        if (traceId.IsEmpty) throw new ArgumentException("Trace id cannot be empty.", nameof(traceId));
        Value = value;
        MessageId = messageId;
        TraceId = traceId;
        Timestamp = timestamp;
        CorrelationId = correlationId is { IsEmpty: false } ? correlationId : null;
        CausationId = causationId is { IsEmpty: false } ? causationId : null;
        Headers = CopyHeaders(headers);
    }

    public FlowValue Value { get; }
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsError => Value is FlowError;
    public FlowError? Error => Value as FlowError;
    public MessageId MessageId { get; }
    public TraceId TraceId { get; }
    public CorrelationId? CorrelationId { get; }
    public MessageId? CausationId { get; }
    public DateTimeOffset Timestamp { get; }
    public IReadOnlyDictionary<string, FlowValue> Headers { get; }

    /// <summary>Creates an initial message. A FlowError payload represents a processing error.</summary>
    public static FlowMessage Create(FlowValue value, CorrelationId? correlationId = null,
        TraceId? traceId = null, IReadOnlyDictionary<string, FlowValue>? headers = null,
        MessageId? causationId = null)
        => new(value, MessageId.New(), traceId is { IsEmpty: false } ? traceId.Value : TraceId.New(),
            DateTimeOffset.UtcNow, correlationId, causationId, FlowTracing.CaptureHeaders(headers));

    public static FlowMessage<T> Create<T>(T value, CorrelationId? correlationId = null,
        TraceId? traceId = null, IReadOnlyDictionary<string, FlowValue>? headers = null,
        MessageId? causationId = null) where T : FlowValue
        => new(Create((FlowValue)value, correlationId, traceId, headers, causationId));

    /// <summary>Produces a new message linked to this input, retaining trace, correlation and headers.</summary>
    public FlowMessage WithValue(FlowValue value, IReadOnlyDictionary<string, FlowValue>? headers = null,
        MessageId? causationId = null)
        => new(value, MessageId.New(), TraceId, DateTimeOffset.UtcNow, CorrelationId,
            causationId is { IsEmpty: false } ? causationId : MessageId,
            FlowTracing.CaptureHeaders(headers ?? Headers, replaceContext: headers is null));

    public FlowMessage<T> WithValue<T>(T value, IReadOnlyDictionary<string, FlowValue>? headers = null,
        MessageId? causationId = null) where T : FlowValue
        => new(WithValue((FlowValue)value, headers, causationId));

    internal static readonly IReadOnlyDictionary<string, FlowValue> EmptyHeaders =
        ImmutableDictionary.Create<string, FlowValue>(StringComparer.Ordinal);

    internal static IReadOnlyDictionary<string, FlowValue> CopyHeaders(
        IReadOnlyDictionary<string, FlowValue>? headers)
    {
        if (headers is null || headers.Count == 0)
            return EmptyHeaders;
        if (headers is ImmutableDictionary<string, FlowValue> immutable &&
            immutable.KeyComparer == StringComparer.Ordinal)
        {
            return immutable;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, FlowValue>(StringComparer.Ordinal);
        foreach (var header in headers)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(header.Key);
            builder.Add(
                header.Key,
                header.Value ?? throw new ArgumentException(
                    "FlowMessage headers cannot contain null values.",
                    nameof(headers)));
        }

        return builder.ToImmutable();
    }
}
