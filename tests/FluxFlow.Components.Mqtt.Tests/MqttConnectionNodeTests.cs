using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttConnectionNodeTests
{
    [Fact]
    public void ConnectionNode_ExposesConfiguredProfileAndReconnect()
    {
        var profile = new MqttConnectionProfile
        {
            Name = "sample-bus",
            Host = "broker.example",
            Port = 8883,
            ClientId = "publisher-1",
            UseTls = true
        };
        var reconnect = new MqttReconnectPolicy
        {
            Enabled = true,
            MaxAttempts = 5,
            InitialDelayMilliseconds = 100,
            MaxDelayMilliseconds = 5000,
            BackoffMultiplier = 1.5,
            UseJitter = false,
            Attributes = new Dictionary<string, string> { ["policy"] = "shared" }
        };

        var node = new Nodes.MqttConnectionNode(
            "sample-bus",
            profile,
            reconnect,
            new ThrowingMqttClientFactory());

        node.ConnectionName.ShouldBe("sample-bus");
        node.Profile.Host.ShouldBe("broker.example");
        node.Profile.Port.ShouldBe(8883);
        node.Profile.ClientId.ShouldBe("publisher-1");
        node.Profile.UseTls.ShouldBeTrue();

        var resolved = node.Reconnect.ShouldNotBeNull();
        resolved.Enabled.ShouldBeTrue();
        resolved.MaxAttempts.ShouldBe(5);
        resolved.InitialDelayMilliseconds.ShouldBe(100);
        resolved.MaxDelayMilliseconds.ShouldBe(5000);
        resolved.BackoffMultiplier.ShouldBe(1.5);
        resolved.UseJitter.ShouldBe(false);
        resolved.Attributes["policy"].ShouldBe("shared");
    }

    [Fact]
    public void ConnectionNode_DefaultsReconnectToNullWhenOmitted()
    {
        var node = MqttTestContext.CreateConnection(new ThrowingMqttClientFactory());

        node.ConnectionName.ShouldBe(MqttTestContext.ConnectionName);
        node.Reconnect.ShouldBeNull();
        node.Profile.Host.ShouldBe("localhost");
    }

    [Fact]
    public void ConnectionNode_RejectsBlankConnectionName()
    {
        Should.Throw<ArgumentException>(
            () => new Nodes.MqttConnectionNode(
                "  ",
                new MqttConnectionProfile(),
                reconnect: null,
                new ThrowingMqttClientFactory()));
    }

    [Fact]
    public void ConnectionNode_RejectsNullProfile()
    {
        Should.Throw<ArgumentNullException>(
            () => new Nodes.MqttConnectionNode(
                "broker",
                profile: null!,
                reconnect: null,
                new ThrowingMqttClientFactory()));
    }

    [Fact]
    public void ConnectionNode_RejectsNullFactory()
    {
        Should.Throw<ArgumentNullException>(
            () => new Nodes.MqttConnectionNode(
                "broker",
                new MqttConnectionProfile(),
                reconnect: null,
                clientFactory: null!));
    }
}
