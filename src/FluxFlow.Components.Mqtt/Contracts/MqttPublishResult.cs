namespace FluxFlow.Components.Mqtt.Contracts;

public sealed record MqttPublishResult
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Topic { get; init; }
    public required int PayloadBytes { get; init; }
    public string? PayloadPreview { get; init; }
    public required MqttQualityOfService QualityOfService { get; init; }
    public required bool Retain { get; init; }
    public string? CorrelationId { get; init; }
}
