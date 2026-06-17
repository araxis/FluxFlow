using FluxFlow.Engine.Runtime;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttComponentModuleTests
{
    [Fact]
    public void RegisterMqttComponents_AddsPackageNodeFactories()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(options =>
                options.UseClientFactory(new ThrowingMqttClientFactory()));

        registry.TryGetFactory(MqttComponentTypes.Connection, out _).ShouldBeTrue();
        registry.TryGetFactory(MqttComponentTypes.Publish, out _).ShouldBeTrue();
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out _).ShouldBeTrue();
    }

    [Fact]
    public void RegisterMqttComponents_RequiresClientFactory()
    {
        // The connection component now owns the MQTT client, so registration
        // requires a client factory to establish it on host ConnectAsync.
        var registry = new RuntimeNodeFactoryRegistry();

        Should.Throw<InvalidOperationException>(
            () => registry.RegisterMqttComponents(_ => { }));
    }
}
