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

    // Initial construction captures context once, including when called by the typed record.
    internal FlowMessage(FlowValue value, CorrelationId? correlationId, TraceId? traceId,
        IReadOnlyDictionary<string, FlowValue>? headers, MessageId? causationId,
        DateTimeOffset? timestamp = null)
        : this(value, MessageId.New(), traceId is { IsEmpty: false } ? traceId.Value : TraceId.New(),
            timestamp ?? DateTimeOffset.UtcNow, correlationId, causationId, FlowTracing.CaptureHeaders(headers)) { }

    // The record copy constructor preserves identity and context; this is not a new processing result.
    internal FlowMessage(FlowMessage source, FlowValue value,
        IReadOnlyDictionary<string, FlowValue>? headers = null, CorrelationId? correlationId = null)
        : this(source)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
        Headers = headers is null ? source.Headers : CopyHeaders(headers);
        if (correlationId is { } correlation)
            CorrelationId = correlation.IsEmpty ? null : correlation;
    }

    // A derived result retains the input's lineage but gets a new identity and creation context.
    internal FlowMessage(FlowValue value, FlowMessage source,
        IReadOnlyDictionary<string, FlowValue>? headers, MessageId? causationId)
        : this(source, value, FlowTracing.CaptureHeaders(headers ?? source.Headers, replaceContext: headers is null))
    {
        MessageId = MessageId.New();
        Timestamp = DateTimeOffset.UtcNow;
        CausationId = causationId is { IsEmpty: false } ? causationId : source.MessageId;
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
        => new(value, correlationId, traceId, headers, causationId);

    public static FlowMessage<T> Create<T>(T value, CorrelationId? correlationId = null,
        TraceId? traceId = null, IReadOnlyDictionary<string, FlowValue>? headers = null,
        MessageId? causationId = null) where T : FlowValue
        => new(value, correlationId, traceId, headers, causationId);

    /// <summary>Produces a new message linked to this input, retaining trace, correlation and headers.</summary>
    public FlowMessage WithValue(FlowValue value, IReadOnlyDictionary<string, FlowValue>? headers = null,
        MessageId? causationId = null)
        => new(value, this, headers, causationId);

    public FlowMessage<T> WithValue<T>(T value, IReadOnlyDictionary<string, FlowValue>? headers = null,
        MessageId? causationId = null) where T : FlowValue
        => new(value, this, headers, causationId);

    /// <summary>Replaces headers with a snapshot, preserving payload, identity, lineage and timestamp.</summary>
    public FlowMessage WithHeaders(IReadOnlyDictionary<string, FlowValue> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        return new(this, Value, headers);
    }

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
