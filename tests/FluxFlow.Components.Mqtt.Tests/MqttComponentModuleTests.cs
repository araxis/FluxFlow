using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
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
            .RegisterMqttComponents(options => options.UseClientFactory(new TestMqttClientFactory()));

        registry.TryGetFactory(MqttComponentTypes.Publish, out _).ShouldBeTrue();
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out _).ShouldBeTrue();
    }

    [Fact]
    public void RegisterMqttComponents_AcceptsClientFactoryDirectly()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMqttComponents(new TestMqttClientFactory());

        registry.TryGetFactory(MqttComponentTypes.Publish, out _).ShouldBeTrue();
        registry.TryGetFactory(MqttComponentTypes.Subscribe, out _).ShouldBeTrue();
    }

    [Fact]
    public void RegisterMqttComponents_RequiresClientFactory()
    {
        var registry = new RuntimeNodeFactoryRegistry();

        var exception = Should.Throw<InvalidOperationException>(
            () => registry.RegisterMqttComponents(_ => { }));

        exception.Message.ShouldContain("client factory");
    }

    private sealed class TestMqttClientFactory : IMqttClientFactory
    {
        public ValueTask<IMqttClientAdapter> CreateAsync(
            MqttConnectionProfile connection,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
