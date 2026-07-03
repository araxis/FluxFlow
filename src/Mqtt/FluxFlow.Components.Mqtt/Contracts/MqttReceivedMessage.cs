namespace FluxFlow.Components.Mqtt.Contracts;

public sealed record MqttReceivedMessage
{
    private byte[]? _payload;
    private byte[]? _correlationData;
    private IReadOnlyDictionary<string, string> _userProperties =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public required DateTimeOffset Timestamp { get; init; }
    public required string Topic { get; init; }
    public required byte[] Payload
    {
        get => _payload!;
        init => _payload = value?.ToArray();
    }

    public string? PayloadPreview { get; init; }
    public string? ContentType { get; init; }
    public MqttQualityOfService QualityOfService { get; init; } = MqttQualityOfService.AtMostOnce;
    public bool Retain { get; init; }
    public string? CorrelationId { get; init; }
    public string? ResponseTopic { get; init; }
    public byte[]? CorrelationData
    {
        get => _correlationData;
        init => _correlationData = value?.ToArray();
    }

    public IReadOnlyDictionary<string, string> UserProperties
    {
        get => _userProperties;
        init => _userProperties = MqttContractMap.Copy(value);
    }
}
