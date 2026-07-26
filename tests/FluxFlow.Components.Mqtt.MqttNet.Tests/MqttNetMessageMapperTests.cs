using System.Buffers;
using System.Text;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Data;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.MqttNet.Tests;

public sealed class MqttNetMessageMapperTests
{
    [Fact]
    public void ToApplicationMessageMapsCanonicalContentAndMetadata()
    {
        var message = MqttNetMessageMapper.ToApplicationMessage(new MqttPublishMessage
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
    public void ToApplicationMessageRejectsNullNamedUserPropertyValues()
    {
        var userProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant"] = null!
        };

        Should.Throw<ArgumentNullException>(() =>
            MqttNetMessageMapper.ToApplicationMessage(new MqttPublishMessage
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

        var received = MqttNetMessageMapper.ToReceivedApplicationMessage(
            applicationMessage,
            timestamp);

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
            MqttNetMessageMapper.ToUtf8Memory(null!))
            .ParamName.ShouldBe("value");

    private static byte[] ToArray(ReadOnlySequence<byte> payload)
    {
        if (payload.IsSingleSegment)
            return payload.First.ToArray();

        var buffer = new byte[checked((int)payload.Length)];
        payload.CopyTo(buffer);
        return buffer;
    }
}
