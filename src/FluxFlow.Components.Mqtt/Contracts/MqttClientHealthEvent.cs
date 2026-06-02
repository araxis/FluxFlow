namespace FluxFlow.Components.Mqtt.Contracts;

public sealed record MqttClientHealthEvent
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public MqttClientHealthState State { get; init; } = MqttClientHealthState.Unknown;

    public string? Message { get; init; }

    public string? Reason { get; init; }

    public string? ConnectionName { get; init; }

    public string? ClientId { get; init; }

    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
