using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttContractTests
{
    [Fact]
    public void PublishMessageSnapshotsContentAndUserProperties()
    {
        var payload = new byte[] { 1, 2, 3 };
        var userProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant"] = "alpha"
        };

        var message = new MqttPublishMessage
        {
            Topic = "devices/a",
            Content = FlowContent.FromBytes(payload),
            UserProperties = userProperties
        };

        payload[0] = 9;
        userProperties["tenant"] = "changed";
        userProperties["extra"] = "ignored";

        message.Content.Bytes.ToArray().ShouldBe([1, 2, 3]);
        message.UserProperties.Count.ShouldBe(1);
        message.UserProperties["tenant"].ShouldBe("alpha");
    }

    [Fact]
    public void PublishMessageTreatsNullUserPropertiesAsEmpty()
    {
        var message = new MqttPublishMessage
        {
            Topic = "devices/a",
            Content = FlowContent.FromBytes(new byte[] { 1 }),
            UserProperties = null!
        };

        message.UserProperties.ShouldBeEmpty();
    }

    [Fact]
    public void ReceivedMessageSnapshotsContentPropertiesAndMatches()
    {
        var payload = new byte[] { 1, 2, 3 };
        var userProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = "sensor"
        };
        var matches = new[] { "secondary", "primary", "primary" };

        var message = new MqttReceivedApplicationMessage
        {
            Timestamp = DateTimeOffset.Parse("2026-06-27T00:00:00+00:00"),
            Topic = "devices/a",
            Content = FlowContent.FromBytes(payload),
            UserProperties = userProperties,
            MatchedSubscriptions = matches
        };

        payload[0] = 9;
        userProperties["source"] = "changed";
        matches[0] = "changed";

        message.Content.Bytes.ToArray().ShouldBe([1, 2, 3]);
        message.UserProperties.Count.ShouldBe(1);
        message.UserProperties["source"].ShouldBe("sensor");
        message.MatchedSubscriptions.ShouldBe(["primary", "secondary"]);
    }

    [Fact]
    public void ReceivedMessageTreatsNullCollectionsAsEmpty()
    {
        var message = new MqttReceivedApplicationMessage
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Topic = "devices/a",
            Content = FlowContent.FromBytes(new byte[] { 1 }),
            UserProperties = null!,
            MatchedSubscriptions = null!
        };

        message.UserProperties.ShouldBeEmpty();
        message.MatchedSubscriptions.ShouldBeEmpty();
    }
}
