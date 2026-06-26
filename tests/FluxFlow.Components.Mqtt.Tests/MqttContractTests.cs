using FluxFlow.Components.Mqtt.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttContractTests
{
    [Fact]
    public void PublishProperties_snapshots_user_properties()
    {
        var userProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant"] = "alpha"
        };

        var properties = new MqttPublishProperties
        {
            UserProperties = userProperties
        };

        userProperties["tenant"] = "changed";
        userProperties["extra"] = "ignored";

        properties.UserProperties.Count.ShouldBe(1);
        properties.UserProperties["tenant"].ShouldBe("alpha");
        properties.UserProperties.ContainsKey("extra").ShouldBeFalse();
    }

    [Fact]
    public void PublishProperties_treats_null_user_properties_as_empty()
    {
        var properties = new MqttPublishProperties
        {
            UserProperties = null!
        };

        properties.UserProperties.ShouldBeEmpty();
    }

    [Fact]
    public void ReceivedMessage_snapshots_user_properties()
    {
        var userProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = "sensor"
        };

        var message = new MqttReceivedMessage
        {
            Timestamp = DateTimeOffset.Parse("2026-06-27T00:00:00+00:00"),
            Topic = "devices/a",
            Payload = [1, 2, 3],
            UserProperties = userProperties
        };

        userProperties["source"] = "changed";
        userProperties["extra"] = "ignored";

        message.UserProperties.Count.ShouldBe(1);
        message.UserProperties["source"].ShouldBe("sensor");
        message.UserProperties.ContainsKey("extra").ShouldBeFalse();
    }

    [Fact]
    public void ReceivedMessage_treats_null_user_properties_as_empty()
    {
        var message = new MqttReceivedMessage
        {
            Timestamp = DateTimeOffset.Parse("2026-06-27T00:00:00+00:00"),
            Topic = "devices/a",
            Payload = [1, 2, 3],
            UserProperties = null!
        };

        message.UserProperties.ShouldBeEmpty();
    }

    [Fact]
    public void HealthEvent_snapshots_attributes()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["clientId"] = "client-a"
        };

        var health = new MqttClientHealthEvent
        {
            Attributes = attributes
        };

        attributes["clientId"] = "changed";
        attributes["extra"] = "ignored";

        health.Attributes.Count.ShouldBe(1);
        health.Attributes["clientId"].ShouldBe("client-a");
        health.Attributes.ContainsKey("extra").ShouldBeFalse();
    }

    [Fact]
    public void HealthEvent_treats_null_attributes_as_empty()
    {
        var health = new MqttClientHealthEvent
        {
            Attributes = null!
        };

        health.Attributes.ShouldBeEmpty();
    }
}
