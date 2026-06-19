namespace FluxFlow.Components.Mqtt.RequestReply;

/// <summary>
/// An inbound MQTT request message. The host maps its MQTT-library message onto this
/// (MQTT5 request/response: <see cref="ResponseTopic"/> + <see cref="CorrelationData"/>).
/// </summary>
public sealed record MqttRequest
{
    public required string Topic { get; init; }
    public byte[] Payload { get; init; } = [];
    public string? ResponseTopic { get; init; }
    public byte[]? CorrelationData { get; init; }
    public string? ContentType { get; init; }
}

/// <summary>The reply a graph produces for an <see cref="MqttRequest"/>.</summary>
public sealed record MqttReply
{
    public byte[] Payload { get; init; } = [];
    public string? ContentType { get; init; }
}

/// <summary>A response message to publish back to the requester's response topic.</summary>
public sealed record MqttResponseMessage(
    string Topic,
    byte[] Payload,
    byte[]? CorrelationData,
    string? ContentType);
