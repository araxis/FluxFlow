namespace FluxFlow.Components.Mqtt.Contracts;

public sealed record MqttClientHealthEvent
{
    private IReadOnlyDictionary<string, string> _attributes =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public DateTimeOffset Timestamp { get; init; } = TimeProvider.System.GetUtcNow();

    public MqttClientHealthState State { get; init; } = MqttClientHealthState.Unknown;

    public string? Message { get; init; }

    public string? Reason { get; init; }

    public string? ConnectionName { get; init; }

    public string? ClientId { get; init; }

    public IReadOnlyDictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = MqttContractMap.Copy(value);
    }
}
