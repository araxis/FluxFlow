namespace FluxFlow.Components.Mqtt.Contracts;

public sealed record MqttPublishRequest
{
    public required string Topic { get; init; }
    public required byte[] Payload { get; init; }
    public string? PayloadPreview { get; init; }
    public string? ContentType { get; init; }
    public MqttQualityOfService QualityOfService { get; init; } = MqttQualityOfService.AtMostOnce;
    public bool Retain { get; init; }
    public MqttPublishProperties? Properties { get; init; }
}

public sealed record MqttPublishProperties
{
    private IReadOnlyDictionary<string, string> _userProperties =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string? CorrelationId { get; init; }
    public string? ResponseTopic { get; init; }
    public IReadOnlyDictionary<string, string> UserProperties
    {
        get => _userProperties;
        init => _userProperties = MqttContractMap.Copy(value);
    }
}
