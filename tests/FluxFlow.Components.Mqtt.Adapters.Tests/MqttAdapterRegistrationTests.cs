using FluxFlow.Components.Mqtt.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;
using AdapterA = FluxFlow.Components.Mqtt.MqttNet;
using AdapterB = FluxFlow.Components.Mqtt.PulseMqtt;

namespace FluxFlow.Components.Mqtt.Adapters.Tests;

public sealed class MqttAdapterRegistrationTests
{
    [Fact]
    public async Task TwoMqttClientAdaptersCanBeRegisteredSideBySide()
    {
        var services = new ServiceCollection();

        AdapterA.FluxFlowMqttServiceCollectionExtensions.AddFluxFlowMqttClient(
            services,
            "adapter-a",
            new AdapterA.MqttNetClientOptions
            {
                Host = "localhost",
                ClientId = "adapter-a-client"
            });
        AdapterB.FluxFlowMqttServiceCollectionExtensions.AddFluxFlowMqttClient(
            services,
            "adapter-b",
            new AdapterB.PulseMqttClientOptions
            {
                Host = "localhost",
                ClientId = "adapter-b-client"
            },
            new AdapterB.MqttClientRegistrationOptions
            {
                StartWithHost = false
            });

        await using var provider = services.BuildServiceProvider();

        var firstClient = provider.GetRequiredKeyedService<AdapterA.MqttNetClient>("adapter-a");
        var secondClient = provider.GetRequiredKeyedService<AdapterB.PulseMqttClient>("adapter-b");

        provider.GetRequiredKeyedService<IMqttPublisher>("adapter-a").ShouldBeSameAs(firstClient);
        provider.GetRequiredKeyedService<IMqttTriggerSource>("adapter-a").ShouldBeSameAs(firstClient);
        provider.GetRequiredKeyedService<IMqttClientHealthSource>("adapter-a").ShouldBeSameAs(firstClient);

        provider.GetRequiredKeyedService<IMqttPublisher>("adapter-b").ShouldBeSameAs(secondClient);
        provider.GetRequiredKeyedService<IMqttTriggerSource>("adapter-b").ShouldBeSameAs(secondClient);
        provider.GetRequiredKeyedService<IMqttClientHealthSource>("adapter-b").ShouldBeSameAs(secondClient);

        firstClient.ShouldNotBeSameAs(secondClient);
        provider.GetServices<IMqttPublisher>().ShouldBeEmpty();
        provider.GetServices<IMqttTriggerSource>().ShouldBeEmpty();
        provider.GetServices<IMqttClientHealthSource>().ShouldBeEmpty();
        provider.GetServices<IHostedService>().ShouldBeEmpty();
    }
}
