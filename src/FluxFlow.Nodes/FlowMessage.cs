using FluxFlow.Data;
using System.Collections.Immutable;

namespace FluxFlow.Nodes;

/// <summary>
/// The envelope every message travels in between nodes: business correlation,
/// graph trace, per-hop identity, causation, timestamp, immutable headers, and
/// the strongly typed payload.
/// </summary>
public sealed record FlowMessage<T>
{
    private IReadOnlyDictionary<string, FlowValue> _headers = FlowMessage.EmptyHeaders;

    public FlowMessage(CorrelationId correlationId, T payload)
    {
        CorrelationId = correlationId;
        Payload = payload;
    }

    public CorrelationId CorrelationId { get; init; }

    public T Payload { get; init; }

    public TraceId TraceId { get; init; } = global::FluxFlow.Nodes.TraceId.New();

    public MessageId MessageId { get; init; } = global::FluxFlow.Nodes.MessageId.New();

    public MessageId? CausationId { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyDictionary<string, FlowValue> Headers
    {
        get => _headers;
        init => _headers = FlowMessage.CopyHeaders(value);
    }

    /// <summary>
    /// Produce the next message in the same exchange: a new payload (and a fresh
    /// per-hop <see cref="MessageId"/>/<see cref="Timestamp"/>), keeping this
    /// message's correlation id, trace id, and headers, and recording this hop
    /// as the cause.
    /// </summary>
    public FlowMessage<TOut> With<TOut>(TOut payload)
        => new(CorrelationId, payload)
        {
            TraceId = TraceId,
            CausationId = MessageId,
            Headers = Headers
        };
}

public static class FlowMessage
{
    internal static readonly IReadOnlyDictionary<string, FlowValue> EmptyHeaders =
        ImmutableDictionary.Create<string, FlowValue>(StringComparer.Ordinal);

    /// <summary>Mint the first envelope of an exchange (source/trigger nodes).</summary>
    public static FlowMessage<T> Create<T>(
        T payload,
        CorrelationId? correlationId = null,
        TraceId? traceId = null)
        => new(
            correlationId is null || correlationId.Value.IsEmpty
                ? CorrelationId.New()
                : correlationId.Value,
            payload)
        {
            TraceId = traceId is null || traceId.Value.IsEmpty
                ? global::FluxFlow.Nodes.TraceId.New()
                : traceId.Value
        };

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
            builder.Add(
                header.Key,
                header.Value ?? throw new ArgumentException(
                    "FlowMessage headers cannot contain null values; use FlowValue.Null.",
                    nameof(headers)));
        }

        return builder.ToImmutable();
    }
}
