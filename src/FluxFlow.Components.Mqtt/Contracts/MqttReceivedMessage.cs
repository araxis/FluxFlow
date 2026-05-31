namespace FluxFlow.Components.Mqtt.Contracts;

public sealed record MqttReceivedMessage
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Topic { get; init; }
    public required byte[] Payload { get; init; }
    public string? PayloadPreview { get; init; }
    public string? ContentType { get; init; }
    public MqttQualityOfService QualityOfService { get; init; } = MqttQualityOfService.AtMostOnce;
    public bool Retain { get; init; }
    public string? CorrelationId { get; init; }
    public Dictionary<string, string> UserProperties { get; init; } = [];
}
