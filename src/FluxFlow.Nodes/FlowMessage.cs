namespace FluxFlow.Nodes;

/// <summary>
/// The envelope every message travels in between nodes: a <see cref="CorrelationId"/>
/// plus the actual <see cref="Payload"/>, with a per-hop <see cref="MessageId"/>,
/// a <see cref="Timestamp"/>, and an extensible <see cref="Headers"/> bag. Immutable
/// so a broadcast can hand the same instance to many consumers safely. Transform
/// the payload with <see cref="With{TOut}"/>, which preserves the correlation id and
/// headers — so correlation flows through a graph without any node copying it by hand.
/// </summary>
public sealed record FlowMessage<T>
{
    public FlowMessage(CorrelationId correlationId, T payload)
    {
        CorrelationId = correlationId;
        Payload = payload;
    }

    public CorrelationId CorrelationId { get; init; }

    public T Payload { get; init; }

    public string MessageId { get; init; } = Guid.NewGuid().ToString("n");

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyDictionary<string, object?> Headers { get; init; } = FlowMessage.EmptyHeaders;

    /// <summary>
    /// Produce the next message in the same exchange: a new payload (and a fresh
    /// per-hop <see cref="MessageId"/>/<see cref="Timestamp"/>), keeping this
    /// message's correlation id and headers.
    /// </summary>
    public FlowMessage<TOut> With<TOut>(TOut payload)
        => new(CorrelationId, payload) { Headers = Headers };
}

public static class FlowMessage
{
    internal static readonly IReadOnlyDictionary<string, object?> EmptyHeaders =
        new Dictionary<string, object?>();

    /// <summary>Mint the first envelope of an exchange (source/trigger nodes).</summary>
    public static FlowMessage<T> Create<T>(T payload, CorrelationId? correlationId = null)
        => new(correlationId ?? CorrelationId.New(), payload);
}
