using RabbitMQ.Client;

namespace FluxFlow.Components.RabbitMQ;

public sealed record AmqpProperties
{
    public string? ContentType { get; init; }
    public string? ContentEncoding { get; init; }
    public Dictionary<string, AmqpField>? Headers { get; init; }
    public byte? DeliveryMode { get; init; }
    public byte? Priority { get; init; }
    public string? CorrelationId { get; init; }
    public string? ReplyTo { get; init; }
    public string? Expiration { get; init; }
    public string? MessageId { get; init; }
    public long? Timestamp { get; init; }
    public string? Type { get; init; }
    public string? UserId { get; init; }
    public string? AppId { get; init; }
    public string? ClusterId { get; init; }

    internal BasicProperties ToNative()
    {
        var result = new BasicProperties
        {
            ContentType = ContentType, ContentEncoding = ContentEncoding,
            Headers = Headers?.ToDictionary(pair => pair.Key, pair => pair.Value.ToNative()),
            CorrelationId = CorrelationId, ReplyTo = ReplyTo, Expiration = Expiration,
            MessageId = MessageId, Type = Type, UserId = UserId, AppId = AppId, ClusterId = ClusterId
        };
        if (DeliveryMode is { } mode) result.DeliveryMode = (DeliveryModes)mode;
        if (Priority is { } priority) result.Priority = priority;
        if (Timestamp is { } stamp) result.Timestamp = new(stamp);
        return result;
    }

    internal static AmqpProperties FromNative(IReadOnlyBasicProperties source) => new()
    {
        ContentType = source.ContentType, ContentEncoding = source.ContentEncoding,
        Headers = source.Headers?.ToDictionary(pair => pair.Key, pair => AmqpWireField.FromNative(pair.Value)),
        DeliveryMode = source.IsDeliveryModePresent() ? (byte)source.DeliveryMode : null,
        Priority = source.IsPriorityPresent() ? source.Priority : null,
        CorrelationId = source.CorrelationId, ReplyTo = source.ReplyTo, Expiration = source.Expiration,
        MessageId = source.MessageId, Timestamp = source.IsTimestampPresent() ? source.Timestamp.UnixTime : null,
        Type = source.Type, UserId = source.UserId, AppId = source.AppId, ClusterId = source.ClusterId
    };
}

public sealed record AmqpPublish
{
    public string Exchange { get; init; } = "";
    public string RoutingKey { get; init; } = "";
    public string Body { get; init; } = "";
    public AmqpProperties Properties { get; init; } = new();
    public bool Mandatory { get; init; } = true;
}

public sealed record AmqpDelivery(string SettlementToken, string Exchange, string RoutingKey,
    string Body, AmqpProperties Properties, bool Redelivered, string ConsumerTag);

public sealed record AmqpSettlement
{
    public string SettlementToken { get; init; } = "";
    public string Action { get; init; } = "Ack";
}
