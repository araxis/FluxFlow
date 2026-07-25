using FluxFlow.Components.Mqtt.Transport;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using AdapterA = FluxFlow.Components.Mqtt.MqttNet;
using AdapterB = FluxFlow.Components.Mqtt.PulseMqtt;

namespace FluxFlow.Components.Mqtt.Adapters.Tests;

public sealed class MqttAdapterRegistrationTests
{
    [Fact]
    public async Task TransportFactoriesCanBeRegisteredSideBySideByClientAddress()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IMqttTransportFactory>(
            "Resources.Messaging.ClientA",
            new AdapterA.MqttNetTransportFactory());
        services.AddKeyedSingleton<IMqttTransportFactory>(
            "Resources.Messaging.ClientB",
            new AdapterB.PulseMqttTransportFactory());

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IMqttTransportFactory>(
                "Resources.Messaging.ClientA")
            .ShouldBeOfType<AdapterA.MqttNetTransportFactory>();
        provider.GetRequiredKeyedService<IMqttTransportFactory>(
                "Resources.Messaging.ClientB")
            .ShouldBeOfType<AdapterB.PulseMqttTransportFactory>();
        provider.GetServices<IMqttTransportFactory>().ShouldBeEmpty();
    }
}
