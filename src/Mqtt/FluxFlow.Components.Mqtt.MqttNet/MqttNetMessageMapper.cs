using System.Buffers;
using System.Text;
using FluxFlow.Components.Mqtt.Contracts;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;

namespace FluxFlow.Components.Mqtt.MqttNet;

internal static class MqttNetMessageMapper
{
    public static MqttApplicationMessage ToApplicationMessage(MqttPublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Payload);

        var builder = new MqttApplicationMessageBuilder()
            .WithTopic(request.Topic)
            .WithPayload(request.Payload)
            .WithQualityOfServiceLevel(ToMqttNetQualityOfService(request.QualityOfService))
            .WithRetainFlag(request.Retain);

        if (!string.IsNullOrWhiteSpace(request.ContentType))
        {
            builder.WithContentType(request.ContentType);
        }

        ApplyPublishProperties(builder, request.Properties);
        return builder.Build();
    }

    public static MqttReceivedMessage ToReceivedMessage(
        MqttApplicationMessage message,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(message);

        var correlationData = message.CorrelationData is { Length: > 0 }
            ? message.CorrelationData
            : null;

        return new MqttReceivedMessage
        {
            Timestamp = timestamp,
            Topic = message.Topic,
            Payload = ToArray(message.Payload),
            ContentType = string.IsNullOrWhiteSpace(message.ContentType)
                ? null
                : message.ContentType,
            QualityOfService = FromMqttNetQualityOfService(message.QualityOfServiceLevel),
            Retain = message.Retain,
            CorrelationId = DecodeCorrelationId(correlationData),
            ResponseTopic = string.IsNullOrWhiteSpace(message.ResponseTopic)
                ? null
                : message.ResponseTopic,
            CorrelationData = correlationData,
            UserProperties = ToDictionary(message.UserProperties)
        };
    }

    public static MqttQualityOfServiceLevel ToMqttNetQualityOfService(
        MqttQualityOfService qualityOfService)
        => qualityOfService switch
        {
            MqttQualityOfService.AtMostOnce => MqttQualityOfServiceLevel.AtMostOnce,
            MqttQualityOfService.AtLeastOnce => MqttQualityOfServiceLevel.AtLeastOnce,
            MqttQualityOfService.ExactlyOnce => MqttQualityOfServiceLevel.ExactlyOnce,
            _ => throw new ArgumentOutOfRangeException(
                nameof(qualityOfService),
                qualityOfService,
                "MQTT quality-of-service value is not supported.")
        };

    public static MqttQualityOfService FromMqttNetQualityOfService(
        MqttQualityOfServiceLevel qualityOfService)
        => qualityOfService switch
        {
            MqttQualityOfServiceLevel.AtMostOnce => MqttQualityOfService.AtMostOnce,
            MqttQualityOfServiceLevel.AtLeastOnce => MqttQualityOfService.AtLeastOnce,
            MqttQualityOfServiceLevel.ExactlyOnce => MqttQualityOfService.ExactlyOnce,
            _ => MqttQualityOfService.AtMostOnce
        };

    private static void ApplyPublishProperties(
        MqttApplicationMessageBuilder builder,
        MqttPublishProperties? properties)
    {
        if (properties is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(properties.CorrelationId))
        {
            builder.WithCorrelationData(Encoding.UTF8.GetBytes(properties.CorrelationId));
        }

        if (!string.IsNullOrWhiteSpace(properties.ResponseTopic))
        {
            builder.WithResponseTopic(properties.ResponseTopic);
        }

        foreach (var (name, value) in properties.UserProperties)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                builder.WithUserProperty(name, ToUtf8Memory(value));
            }
        }
    }

    private static string? DecodeCorrelationId(byte[]? correlationData)
        => correlationData is { Length: > 0 }
            ? Encoding.UTF8.GetString(correlationData)
            : null;

    private static Dictionary<string, string> ToDictionary(
        IEnumerable<MqttUserProperty>? userProperties)
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
                values[property.Name] = property.ReadValueAsString();
            }
        }

        return values;
    }

    public static ReadOnlyMemory<byte> ToUtf8Memory(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Encoding.UTF8.GetBytes(value);
    }

    private static byte[] ToArray(ReadOnlySequence<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return [];
        }

        if (payload.IsSingleSegment)
        {
            return payload.First.ToArray();
        }

        var buffer = new byte[checked((int)payload.Length)];
        payload.CopyTo(buffer);
        return buffer;
    }
}
