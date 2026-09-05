using System.Buffers;
using System.Text;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Data;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;

namespace FluxFlow.Components.Mqtt.MqttNet;

internal static class MqttNetMessageMapper
{
    internal static MqttApplicationMessage ToApplicationMessage(MqttPublishMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var builder = new MqttApplicationMessageBuilder()
            .WithTopic(message.Topic)
            .WithPayload(message.Content.Bytes.AsSpan().ToArray())
            .WithQualityOfServiceLevel(ToMqttNetQualityOfService(message.Qos))
            .WithRetainFlag(message.Retain);
        if (!string.IsNullOrWhiteSpace(message.Content.ContentType))
            builder.WithContentType(message.Content.ContentType);
        if (!string.IsNullOrWhiteSpace(message.ResponseTopic))
            builder.WithResponseTopic(message.ResponseTopic);
        if (!string.IsNullOrWhiteSpace(message.CorrelationData))
            builder.WithCorrelationData(Encoding.UTF8.GetBytes(message.CorrelationData));
        foreach (var property in message.UserProperties)
            builder.WithUserProperty(property.Key, ToUtf8Memory(property.Value));
        return builder.Build();
    }

    internal static MqttReceivedApplicationMessage ToReceivedApplicationMessage(
        MqttApplicationMessage message,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(message);
        var correlationData = message.CorrelationData is { Length: > 0 }
            ? Encoding.UTF8.GetString(message.CorrelationData)
            : null;
        var contentType = string.IsNullOrWhiteSpace(message.ContentType)
            ? null
            : message.ContentType;

        return new MqttReceivedApplicationMessage
        {
            Timestamp = timestamp,
            Topic = message.Topic,
            Content = FlowContent.FromBytes(ToArray(message.Payload), contentType),
            Qos = FromMqttNetQos(message.QualityOfServiceLevel),
            Retain = message.Retain,
            ResponseTopic = string.IsNullOrWhiteSpace(message.ResponseTopic)
                ? null
                : message.ResponseTopic,
            CorrelationData = correlationData,
            UserProperties = ToDictionary(message.UserProperties)
        };
    }

    internal static MqttQos FromMqttNetQos(MqttQualityOfServiceLevel qualityOfService)
        => qualityOfService switch
        {
            MqttQualityOfServiceLevel.AtMostOnce => MqttQos.AtMostOnce,
            MqttQualityOfServiceLevel.AtLeastOnce => MqttQos.AtLeastOnce,
            MqttQualityOfServiceLevel.ExactlyOnce => MqttQos.ExactlyOnce,
            _ => MqttQos.AtMostOnce
        };

    internal static MqttQualityOfServiceLevel ToMqttNetQualityOfService(MqttQos qos)
        => qos switch
        {
            MqttQos.AtMostOnce => MqttQualityOfServiceLevel.AtMostOnce,
            MqttQos.AtLeastOnce => MqttQualityOfServiceLevel.AtLeastOnce,
            MqttQos.ExactlyOnce => MqttQualityOfServiceLevel.ExactlyOnce,
            _ => throw new ArgumentOutOfRangeException(nameof(qos))
        };

    internal static ReadOnlyMemory<byte> ToUtf8Memory(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Encoding.UTF8.GetBytes(value);
    }

    private static Dictionary<string, string> ToDictionary(
        IEnumerable<MqttUserProperty>? userProperties)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (userProperties is null)
            return values;

        foreach (var property in userProperties)
        {
            if (!string.IsNullOrWhiteSpace(property.Name))
                values[property.Name] = property.ReadValueAsString();
        }

        return values;
    }

    private static byte[] ToArray(ReadOnlySequence<byte> payload)
    {
        if (payload.IsEmpty)
            return [];
        if (payload.IsSingleSegment)
            return payload.First.ToArray();

        var buffer = new byte[checked((int)payload.Length)];
        payload.CopyTo(buffer);
        return buffer;
    }
}
