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
            .RegisterMqttComponents(_ => { });

        registry.TryGetFactory(MqttComponentTypes.Connection, out _).ShouldBeTrue();
        registry.TryGetFactory(MqttComponentTypes.Publish, out _).ShouldBeTrue();
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out _).ShouldBeTrue();
    }

    [Fact]
    public void RegisterMqttComponents_DoesNotRequireClientFactory()
    {
        // The connection component holds configuration only and no node creates a
        // client this step, so registration no longer requires a client factory.
        var registry = new RuntimeNodeFactoryRegistry();

        Should.NotThrow(() => registry.RegisterMqttComponents(_ => { }));

        registry.TryGetFactory(MqttComponentTypes.Connection, out _).ShouldBeTrue();
    }
}
