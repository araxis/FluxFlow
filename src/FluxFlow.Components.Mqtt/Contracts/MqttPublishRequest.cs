namespace FluxFlow.Components.Mqtt.Contracts;

public sealed record MqttPublishRequest
{
    public string? Topic { get; init; }
    public required byte[] Payload { get; init; }
    public string? ContentType { get; init; }
    public MqttQualityOfService? QualityOfService { get; init; }
    public bool? Retain { get; init; }
    public string? CorrelationId { get; init; }
    public Dictionary<string, string> UserProperties { get; init; } = [];
}
