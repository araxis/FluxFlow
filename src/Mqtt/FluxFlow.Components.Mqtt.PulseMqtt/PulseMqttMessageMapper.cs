using System.Text;
using FluxFlow.Components.Mqtt.Contracts;
using Pulse.Mqtt;
using Pulse.Mqtt.Packets;
using FluxMqttQualityOfService = FluxFlow.Components.Mqtt.Contracts.MqttQualityOfService;
using PulseMqttQualityOfService = Pulse.Mqtt.MqttQualityOfService;

namespace FluxFlow.Components.Mqtt.PulseMqtt;

internal static class PulseMqttMessageMapper
{
    public static MqttPublishPacket ToPublishPacket(MqttPublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Payload);

        return new MqttPublishPacket
        {
            Topic = request.Topic,
            Payload = request.Payload,
            ContentType = string.IsNullOrWhiteSpace(request.ContentType)
                ? null
                : request.ContentType,
            QualityOfService = ToPulseQualityOfService(request.QualityOfService),
            Retain = request.Retain,
            CorrelationData = ToCorrelationData(request.Properties?.CorrelationId),
            ResponseTopic = string.IsNullOrWhiteSpace(request.Properties?.ResponseTopic)
                ? null
                : request.Properties.ResponseTopic,
            UserProperties = ToUserProperties(request.Properties?.UserProperties)
        };
    }

    public static MqttWillMessage ToWillMessage(PulseMqttLastWillOptions lastWill)
    {
        ArgumentNullException.ThrowIfNull(lastWill);
        ArgumentNullException.ThrowIfNull(lastWill.Payload);

        return new MqttWillMessage(lastWill.Topic)
        {
            Payload = lastWill.Payload,
            ContentType = string.IsNullOrWhiteSpace(lastWill.ContentType)
                ? null
                : lastWill.ContentType,
            QualityOfService = ToPulseQualityOfService(lastWill.QualityOfService),
            Retain = lastWill.Retain,
            CorrelationData = ToCorrelationData(lastWill.Properties?.CorrelationId),
            ResponseTopic = string.IsNullOrWhiteSpace(lastWill.Properties?.ResponseTopic)
                ? null
                : lastWill.Properties.ResponseTopic,
            UserProperties = ToUserProperties(lastWill.Properties?.UserProperties)
        };
    }

    public static MqttReceivedMessage ToReceivedMessage(
        MqttPublishPacket packet,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(packet);

        return new MqttReceivedMessage
        {
            Timestamp = timestamp,
            Topic = packet.Topic,
            Payload = packet.Payload.ToArray(),
            ContentType = string.IsNullOrWhiteSpace(packet.ContentType)
                ? null
                : packet.ContentType,
            QualityOfService = FromPulseQualityOfService(packet.QualityOfService),
            Retain = packet.Retain,
            CorrelationId = DecodeCorrelationId(packet.CorrelationData),
            ResponseTopic = string.IsNullOrWhiteSpace(packet.ResponseTopic)
                ? null
                : packet.ResponseTopic,
            CorrelationData = packet.CorrelationData.HasValue
                ? packet.CorrelationData.Value.ToArray()
                : null,
            UserProperties = ToDictionary(packet.UserProperties)
        };
    }

    public static PulseMqttQualityOfService ToPulseQualityOfService(
        FluxMqttQualityOfService qualityOfService)
        => qualityOfService switch
        {
            FluxMqttQualityOfService.AtMostOnce => PulseMqttQualityOfService.AtMostOnce,
            FluxMqttQualityOfService.AtLeastOnce => PulseMqttQualityOfService.AtLeastOnce,
            FluxMqttQualityOfService.ExactlyOnce => PulseMqttQualityOfService.ExactlyOnce,
            _ => throw new ArgumentOutOfRangeException(
                nameof(qualityOfService),
                qualityOfService,
                "MQTT quality-of-service value is not supported.")
        };

    public static FluxMqttQualityOfService FromPulseQualityOfService(
        PulseMqttQualityOfService qualityOfService)
        => qualityOfService switch
        {
            PulseMqttQualityOfService.AtMostOnce => FluxMqttQualityOfService.AtMostOnce,
            PulseMqttQualityOfService.AtLeastOnce => FluxMqttQualityOfService.AtLeastOnce,
            PulseMqttQualityOfService.ExactlyOnce => FluxMqttQualityOfService.ExactlyOnce,
            _ => FluxMqttQualityOfService.AtMostOnce
        };

    public static IReadOnlyList<MqttUserProperty> ToUserProperties(
        IReadOnlyDictionary<string, string>? userProperties)
    {
        if (userProperties is null || userProperties.Count == 0)
        {
            return [];
        }

        var values = new List<MqttUserProperty>(userProperties.Count);
        foreach (var (name, value) in userProperties)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                values.Add(new MqttUserProperty(name, value));
            }
        }

        return values;
    }

    public static ReadOnlyMemory<byte> ToUtf8Memory(string value)
        => Encoding.UTF8.GetBytes(value);

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
        {
            return values;
        }

        foreach (var property in userProperties)
        {
            if (!string.IsNullOrWhiteSpace(property.Name))
            {
                values[property.Name] = property.Value;
            }
        }

        return values;
    }
}
