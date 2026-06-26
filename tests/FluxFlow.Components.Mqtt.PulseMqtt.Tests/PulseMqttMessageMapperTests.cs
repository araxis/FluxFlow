using System.Text;
using FluxFlow.Components.Mqtt.Contracts;
using Pulse.Mqtt;
using Pulse.Mqtt.Packets;
using Shouldly;
using Xunit;
using FluxMqttQualityOfService = FluxFlow.Components.Mqtt.Contracts.MqttQualityOfService;
using PulseMqttQualityOfService = Pulse.Mqtt.MqttQualityOfService;

namespace FluxFlow.Components.Mqtt.PulseMqtt.Tests;

public sealed class PulseMqttMessageMapperTests
{
    [Fact]
    public void ToPublishPacket_MapsPublishMetadata()
    {
        var packet = PulseMqttMessageMapper.ToPublishPacket(new MqttPublishRequest
        {
            Topic = "devices/a",
            Payload = [1, 2, 3],
            ContentType = "application/json",
            QualityOfService = FluxMqttQualityOfService.AtLeastOnce,
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

        packet.Topic.ShouldBe("devices/a");
        packet.Payload.ToArray().ShouldBe([1, 2, 3]);
        packet.ContentType.ShouldBe("application/json");
        packet.QualityOfService.ShouldBe(PulseMqttQualityOfService.AtLeastOnce);
        packet.Retain.ShouldBeTrue();
        Encoding.UTF8.GetString(packet.CorrelationData!.Value.Span).ShouldBe("corr-1");
        packet.ResponseTopic.ShouldBe("devices/a/reply");
        packet.UserProperties.Single().Name.ShouldBe("tenant");
        packet.UserProperties.Single().Value.ShouldBe("alpha");
    }

    [Fact]
    public void ToReceivedMessage_MapsPacketMetadata()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-20T12:00:00+00:00");
        var packet = new MqttPublishPacket
        {
            Topic = "devices/a",
            Payload = new byte[] { 4, 5, 6 },
            ContentType = "text/plain",
            QualityOfService = PulseMqttQualityOfService.ExactlyOnce,
            Retain = true,
            CorrelationData = Encoding.UTF8.GetBytes("corr-2"),
            ResponseTopic = "devices/a/result",
            UserProperties = [new MqttUserProperty("source", "sensor")]
        };

        var received = PulseMqttMessageMapper.ToReceivedMessage(packet, timestamp);

        received.Timestamp.ShouldBe(timestamp);
        received.Topic.ShouldBe("devices/a");
        received.Payload.ShouldBe([4, 5, 6]);
        received.ContentType.ShouldBe("text/plain");
        received.QualityOfService.ShouldBe(FluxMqttQualityOfService.ExactlyOnce);
        received.Retain.ShouldBeTrue();
        received.CorrelationId.ShouldBe("corr-2");
        received.ResponseTopic.ShouldBe("devices/a/result");
        received.CorrelationData.ShouldBe(Encoding.UTF8.GetBytes("corr-2"));
        received.UserProperties["source"].ShouldBe("sensor");
    }

    [Fact]
    public void ToUtf8Memory_rejects_null_values()
        => Should.Throw<ArgumentNullException>(() =>
            PulseMqttMessageMapper.ToUtf8Memory(null!))
            .ParamName.ShouldBe("value");
}
