using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttConnectionNodeTests
{
    [Fact]
    public void ConnectionNode_ExposesConfiguredProfileAndReconnect()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        var resources = MqttResourceTestContext.CreateResources(
            registry,
            new
            {
                profile = new
                {
                    name = "sample-bus",
                    host = "broker.example",
                    port = 8883,
                    clientId = "publisher-1",
                    useTls = true
                },
                reconnect = new
                {
                    enabled = true,
                    maxAttempts = 5,
                    initialDelayMilliseconds = 100,
                    maxDelayMilliseconds = 5000,
                    backoffMultiplier = 1.5,
                    useJitter = false,
                    attributes = new Dictionary<string, string>
                    {
                        ["policy"] = "shared"
                    }
                }
            },
            connectionName: "sample-bus");

        var node = resources[new NodeName("sample-bus")].Node;
        var handle = node.ShouldBeAssignableTo<IMqttConnectionHandle>()!;

        handle.ConnectionName.ShouldBe("sample-bus");
        handle.Profile.Host.ShouldBe("broker.example");
        handle.Profile.Port.ShouldBe(8883);
        handle.Profile.ClientId.ShouldBe("publisher-1");
        handle.Profile.UseTls.ShouldBeTrue();

        var reconnect = handle.Reconnect.ShouldNotBeNull();
        reconnect.Enabled.ShouldBeTrue();
        reconnect.MaxAttempts.ShouldBe(5);
        reconnect.InitialDelayMilliseconds.ShouldBe(100);
        reconnect.MaxDelayMilliseconds.ShouldBe(5000);
        reconnect.BackoffMultiplier.ShouldBe(1.5);
        reconnect.UseJitter.ShouldBe(false);
        reconnect.Attributes["policy"].ShouldBe("shared");
    }

    [Fact]
    public void ConnectionNode_DefaultsReconnectToNullWhenOmitted()
    {
        var registry = MqttResourceTestContext.CreateRegistry();
        var resources = MqttResourceTestContext.CreateResources(registry);

        var handle = resources[new NodeName(MqttResourceTestContext.ConnectionName)].Node
            .ShouldBeAssignableTo<IMqttConnectionHandle>()!;

        handle.ConnectionName.ShouldBe(MqttResourceTestContext.ConnectionName);
        handle.Reconnect.ShouldBeNull();
        handle.Profile.Host.ShouldBe("localhost");
    }

    [Fact]
    public void ConnectionNode_RejectsInvalidReconnectPolicy()
    {
        var registry = MqttResourceTestContext.CreateRegistry();

        var exception = Should.Throw<InvalidOperationException>(
            () => MqttResourceTestContext.CreateResources(
                registry,
                new
                {
                    profile = new { name = "sample-bus" },
                    reconnect = new { maxAttempts = -1 }
                },
                connectionName: "sample-bus"));

        exception.Message.ShouldContain("reconnect.maxAttempts");
    }
}
