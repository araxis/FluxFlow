using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;
using MQTTnet.Protocol;

namespace FluxFlow.Components.Mqtt;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MqttConnectionOperation { Connect, Disconnect }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MqttSubscriptionOperation { Subscribe, Unsubscribe }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MqttPayloadEncoding { Utf8, Base64 }

public sealed class MqttAcknowledgementCommand : FlowValue
{
    public required string AcknowledgementId { get; init; }
    protected override JsonElement CreateJson() => JsonSerializer.SerializeToElement(new { AcknowledgementId }, MqttJson.Options);
}

public sealed class MqttConnectionCommand : FlowValue
{
    public required MqttConnectionOperation Command { get; init; }
    /// <summary>Suppresses automatic reconnection after Disconnect. A later Connect enables it again.</summary>
    public bool DisableReconnect { get; init; }
    protected override JsonElement CreateJson() => JsonSerializer.SerializeToElement(new { Command, DisableReconnect }, MqttJson.Options);
}

public sealed class MqttSubscriptionCommand : FlowValue
{
    private IReadOnlyList<MqttTopic> _topics = Array.Empty<MqttTopic>();
    public required MqttSubscriptionOperation Command { get; init; }
    public required IReadOnlyList<MqttTopic> Topics
    {
        get => _topics;
        init => _topics = Array.AsReadOnly(value.ToArray());
    }
    protected override JsonElement CreateJson() => JsonSerializer.SerializeToElement(new { Command, Topics }, MqttJson.Options);
}

public sealed class MqttPublishRequest : FlowValue
{
    public required string Topic { get; init; }
    public required string Payload { get; init; }
    public MqttPayloadEncoding Encoding { get; init; }
    public int QoS { get; init; }
    public bool Retain { get; init; }
    protected override JsonElement CreateJson() => JsonSerializer.SerializeToElement(new { Topic, Payload, Encoding, QoS, Retain }, MqttJson.Options);
}

internal static class MqttJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = null };
    internal static T Read<T>(FlowMessage message) where T : FlowValue
        => message.Value is T value ? value : message.Value.Json.Deserialize<T>(Options)
            ?? throw new JsonException($"Expected {typeof(T).Name}.");
    internal static JsonFlowValue Value(object value) => new(JsonSerializer.SerializeToElement(value, Options));
}
