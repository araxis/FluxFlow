namespace FluxFlow.Components.Mqtt.Contracts;

public sealed record MqttReconnectPolicy
{
    public bool Enabled { get; init; } = true;
    public int? MaxAttempts { get; init; }
    public double? InitialDelayMilliseconds { get; init; }
    public double? MaxDelayMilliseconds { get; init; }
    public double? BackoffMultiplier { get; init; }
    public bool? UseJitter { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = [];
}
