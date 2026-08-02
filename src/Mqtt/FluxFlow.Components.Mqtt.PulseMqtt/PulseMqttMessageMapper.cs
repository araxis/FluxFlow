using System.Text;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Data;
using Pulse.Mqtt;
using Pulse.Mqtt.Packets;
using PulseMqttQualityOfService = Pulse.Mqtt.MqttQualityOfService;

namespace FluxFlow.Components.Mqtt.PulseMqtt;

internal static class PulseMqttMessageMapper
{
    internal static MqttPublishPacket ToPublishPacket(MqttPublishMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new MqttPublishPacket
        {
            Topic = message.Topic,
            Payload = message.Content.Bytes.ToArray(),
            ContentType = string.IsNullOrWhiteSpace(message.Content.ContentType)
                ? null
                : message.Content.ContentType,
            QualityOfService = ToPulseQualityOfService(message.Qos),
            Retain = message.Retain,
            CorrelationData = ToCorrelationData(message.CorrelationData),
            ResponseTopic = string.IsNullOrWhiteSpace(message.ResponseTopic)
                ? null
                : message.ResponseTopic,
            UserProperties = ToUserProperties(message.UserProperties)
        };
    }

    internal static MqttWillMessage ToWillMessage(MqttPublishMessage lastWill)
    {
        ArgumentNullException.ThrowIfNull(lastWill);
        return new MqttWillMessage(lastWill.Topic)
        {
            Payload = lastWill.Content.Bytes.ToArray(),
            ContentType = string.IsNullOrWhiteSpace(lastWill.Content.ContentType)
                ? null
                : lastWill.Content.ContentType,
            QualityOfService = ToPulseQualityOfService(lastWill.Qos),
            Retain = lastWill.Retain,
            CorrelationData = ToCorrelationData(lastWill.CorrelationData),
            ResponseTopic = string.IsNullOrWhiteSpace(lastWill.ResponseTopic)
                ? null
                : lastWill.ResponseTopic,
            UserProperties = ToUserProperties(lastWill.UserProperties)
        };
    }

    internal static MqttReceivedApplicationMessage ToReceivedApplicationMessage(
        MqttPublishPacket packet,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return new MqttReceivedApplicationMessage
        {
            Timestamp = timestamp,
            Topic = packet.Topic,
            Content = FlowContent.FromBytes(packet.Payload, packet.ContentType),
            Qos = FromPulseQos(packet.QualityOfService),
            Retain = packet.Retain,
            CorrelationData = DecodeCorrelationId(packet.CorrelationData),
            ResponseTopic = string.IsNullOrWhiteSpace(packet.ResponseTopic)
                ? null
                : packet.ResponseTopic,
            UserProperties = ToDictionary(packet.UserProperties)
        };
    }

    internal static PulseMqttQualityOfService ToPulseQualityOfService(MqttQos qos)
        => qos switch
        {
            MqttQos.AtMostOnce => PulseMqttQualityOfService.AtMostOnce,
            MqttQos.AtLeastOnce => PulseMqttQualityOfService.AtLeastOnce,
            MqttQos.ExactlyOnce => PulseMqttQualityOfService.ExactlyOnce,
            _ => throw new ArgumentOutOfRangeException(nameof(qos), qos, "MQTT QoS is not supported.")
        };

    internal static MqttQos FromPulseQos(PulseMqttQualityOfService qos)
        => qos switch
        {
            PulseMqttQualityOfService.AtMostOnce => MqttQos.AtMostOnce,
            PulseMqttQualityOfService.AtLeastOnce => MqttQos.AtLeastOnce,
            PulseMqttQualityOfService.ExactlyOnce => MqttQos.ExactlyOnce,
            _ => throw new ArgumentOutOfRangeException(nameof(qos), qos, "MQTT QoS is not supported.")
        };

    internal static IReadOnlyList<MqttUserProperty> ToUserProperties(
        IReadOnlyDictionary<string, string>? userProperties)
    {
        if (userProperties is null || userProperties.Count == 0)
            return [];

        var values = new List<MqttUserProperty>(userProperties.Count);
        foreach (var (name, value) in userProperties)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                ArgumentNullException.ThrowIfNull(value);
                values.Add(new MqttUserProperty(name, value));
            }
        }

        return values;
    }

    internal static ReadOnlyMemory<byte> ToUtf8Memory(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Encoding.UTF8.GetBytes(value);
    }

    private static ReadOnlyMemory<byte>? ToCorrelationData(string? correlationId)
        => string.IsNullOrWhiteSpace(correlationId)
            ? null
            : ToUtf8Memory(correlationId);

    private static string? DecodeCorrelationId(ReadOnlyMemory<byte>? correlationData)
        => correlationData is { IsEmpty: false }
            ? Encoding.UTF8.GetString(correlationData.Value.Span)
            : null;

    private static Dictionary<string, string> ToDictionary(
        IReadOnlyList<MqttUserProperty>? userProperties)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (userProperties is null)
            return values;

        foreach (var property in userProperties)
        {
            if (!string.IsNullOrWhiteSpace(property.Name))
                values[property.Name] = property.Value;
        }

        return values;
    }
}
