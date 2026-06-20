using System.Buffers;
using System.Text;
using FluxFlow.Components.Mqtt.Contracts;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.MqttNet.Tests;

public sealed class MqttNetMessageMapperTests
{
    [Fact]
    public void ToApplicationMessage_MapsPublishMetadata()
    {
        var message = MqttNetMessageMapper.ToApplicationMessage(new MqttPublishRequest
        {
            Topic = "devices/a",
            Payload = [1, 2, 3],
            ContentType = "application/json",
            QualityOfService = MqttQualityOfService.AtLeastOnce,
            Retain = true,
            Properties = new MqttPublishProperties
            {
                CorrelationId = "corr-1",
                ResponseTopic = "devices/a/reply",
                UserProperties =
                {
                    ["tenant"] = "alpha"
                }
            }
        });

        message.Topic.ShouldBe("devices/a");
        ToArray(message.Payload).ShouldBe([1, 2, 3]);
        message.ContentType.ShouldBe("application/json");
        message.QualityOfServiceLevel.ShouldBe(MqttQualityOfServiceLevel.AtLeastOnce);
        message.Retain.ShouldBeTrue();
        Encoding.UTF8.GetString(message.CorrelationData).ShouldBe("corr-1");
        message.ResponseTopic.ShouldBe("devices/a/reply");
        message.UserProperties.Single().Name.ShouldBe("tenant");
        message.UserProperties.Single().ReadValueAsString().ShouldBe("alpha");
    }

    [Fact]
    public void ToReceivedMessage_MapsApplicationMessageMetadata()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-20T12:00:00+00:00");
        var applicationMessage = new MqttApplicationMessageBuilder()
            .WithTopic("devices/a")
            .WithPayload([4, 5, 6])
            .WithContentType("text/plain")
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
            .WithRetainFlag(true)
            .WithCorrelationData(Encoding.UTF8.GetBytes("corr-2"))
            .WithResponseTopic("devices/a/result")
            .WithUserProperty("source", MqttNetMessageMapper.ToUtf8Memory("sensor"))
            .Build();

        var received = MqttNetMessageMapper.ToReceivedMessage(
            applicationMessage,
            timestamp);

        received.Timestamp.ShouldBe(timestamp);
        received.Topic.ShouldBe("devices/a");
        received.Payload.ShouldBe([4, 5, 6]);
        received.ContentType.ShouldBe("text/plain");
        received.QualityOfService.ShouldBe(MqttQualityOfService.ExactlyOnce);
        received.Retain.ShouldBeTrue();
        received.CorrelationId.ShouldBe("corr-2");
        received.ResponseTopic.ShouldBe("devices/a/result");
        received.CorrelationData.ShouldBe(Encoding.UTF8.GetBytes("corr-2"));
        received.UserProperties["source"].ShouldBe("sensor");
    }

    private static byte[] ToArray(ReadOnlySequence<byte> payload)
    {
        if (payload.IsSingleSegment)
        {
            return payload.First.ToArray();
        }

        var buffer = new byte[checked((int)payload.Length)];
        payload.CopyTo(buffer);
        return buffer;
    }
}
