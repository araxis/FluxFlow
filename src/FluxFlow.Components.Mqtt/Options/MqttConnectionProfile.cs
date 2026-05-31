namespace FluxFlow.Components.Mqtt.Options;

public sealed record MqttConnectionProfile
{
    public string? Name { get; init; }
    public string? Host { get; init; }
    public int Port { get; init; } = 1883;
    public string? ClientId { get; init; }
    public bool UseTls { get; init; }
    public Dictionary<string, string> Properties { get; init; } = [];
}
