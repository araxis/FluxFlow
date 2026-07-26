using System.Text;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Data;
using Pulse.Mqtt;
using Pulse.Mqtt.Packets;
using Shouldly;
using Xunit;
using PulseMqttQualityOfService = Pulse.Mqtt.MqttQualityOfService;

namespace FluxFlow.Components.Mqtt.PulseMqtt.Tests;

public sealed class PulseMqttMessageMapperTests
{
    [Fact]
    public void ToPublishPacketMapsCanonicalContentAndMetadata()
    {
        var packet = PulseMqttMessageMapper.ToPublishPacket(new MqttPublishMessage
        {
            Topic = "devices/a",
            Content = FlowContent.FromBytes(new byte[] { 1, 2, 3 }, "application/json"),
            Qos = MqttQos.AtLeastOnce,
            Retain = true,
            CorrelationData = "corr-1",
            ResponseTopic = "devices/a/reply",
            UserProperties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tenant"] = "alpha"
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
    public void ToPublishPacketRejectsNullNamedUserPropertyValues()
    {
        var userProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant"] = null!
        };

        Should.Throw<ArgumentNullException>(() =>
            PulseMqttMessageMapper.ToPublishPacket(new MqttPublishMessage
            {
                Topic = "devices/a",
                Content = FlowContent.FromBytes(new byte[] { 1 }),
                UserProperties = userProperties
            }))
            .ParamName.ShouldBe("value");
    }

    [Fact]
    public void ToReceivedApplicationMessageMapsCanonicalContentAndMetadata()
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

        var received = PulseMqttMessageMapper.ToReceivedApplicationMessage(packet, timestamp);

        received.Timestamp.ShouldBe(timestamp);
        received.Topic.ShouldBe("devices/a");
        received.Content.Bytes.ToArray().ShouldBe([4, 5, 6]);
        received.Content.ContentType.ShouldBe("text/plain");
        received.Qos.ShouldBe(MqttQos.ExactlyOnce);
        received.Retain.ShouldBeTrue();
        received.CorrelationData.ShouldBe("corr-2");
        received.ResponseTopic.ShouldBe("devices/a/result");
        received.UserProperties["source"].ShouldBe("sensor");
    }

    [Fact]
    public void ToUtf8MemoryRejectsNullValues()
        => Should.Throw<ArgumentNullException>(() =>
            PulseMqttMessageMapper.ToUtf8Memory(null!))
            .ParamName.ShouldBe("value");
}
