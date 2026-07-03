using FluxFlow.Components.Mqtt.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pulse.Mqtt.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.PulseMqtt.Tests;

public sealed class PulseMqttRegistrationTests
{
    [Fact]
    public async Task AddFluxFlowMqttClient_RegistersKeyedClientContractsWithoutHostedLifetimeByDefault()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowMqttClient(
            "primary",
            new PulseMqttClientOptions
            {
                Host = "localhost",
                AllowOfflinePublishQueue = true
            });

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredKeyedService<PulseMqttClient>("primary");

        provider.GetRequiredKeyedService<IMqttPublisher>("primary").ShouldBeSameAs(client);
        provider.GetRequiredKeyedService<IMqttTriggerSource>("primary").ShouldBeSameAs(client);
        provider.GetRequiredKeyedService<IMqttClientHealthSource>("primary").ShouldBeSameAs(client);

        provider.GetServices<IHostedService>().Any().ShouldBeFalse();
    }

    [Fact]
    public async Task AddFluxFlowMqttClient_TrimsKeyedClientNames()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowMqttClient(
            " primary ",
            new PulseMqttClientOptions
            {
                Host = "localhost",
                AllowOfflinePublishQueue = true
            });

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredKeyedService<PulseMqttClient>("primary");

        provider.GetRequiredKeyedService<IMqttPublisher>("primary").ShouldBeSameAs(client);
        provider.GetRequiredKeyedService<IMqttTriggerSource>("primary").ShouldBeSameAs(client);
        provider.GetRequiredKeyedService<IMqttClientHealthSource>("primary").ShouldBeSameAs(client);
    }

    [Fact]
    public async Task AddFluxFlowMqttClient_CanStartAndStopWithHostedLifetime()
    {
        await using var broker = new PulseMqttTestBroker();
        var services = new ServiceCollection();
        services.AddFluxFlowMqttClient(
            "primary",
            new PulseMqttClientOptions
            {
                ClientId = "fluxflow-pulse-di",
                ConnectTimeout = TimeSpan.FromSeconds(5)
            },
            new MqttClientRegistrationOptions
            {
                StartWithHost = true,
                WaitForConnectedOnStart = true
            },
            transportFactory: broker);

        await using var provider = services.BuildServiceProvider();
        var lifetimes = provider.GetServices<IHostedService>().ToArray();
        lifetimes.Length.ShouldBe(1);
        var lifetime = lifetimes[0];

        await lifetime.StartAsync(CancellationToken.None);
        var client = provider.GetRequiredKeyedService<PulseMqttClient>("primary");
        client.IsConnected.ShouldBeTrue();

        await lifetime.StopAsync(CancellationToken.None);
        client.IsConnected.ShouldBeFalse();
    }

    [Fact]
    public void AddFluxFlowMqttClient_RejectsWaitForConnectedWithoutHostedStart()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentException>(() => services.AddFluxFlowMqttClient(
            "primary",
            new PulseMqttClientOptions { Host = "localhost" },
            new MqttClientRegistrationOptions
            {
                StartWithHost = false,
                WaitForConnectedOnStart = true
            }));
    }

    [Fact]
    public void AddFluxFlowMqttClient_RejectsInvalidArguments()
    {
        var services = new ServiceCollection();
        var options = new PulseMqttClientOptions { Host = "localhost" };

        Should.Throw<ArgumentNullException>(() =>
            FluxFlowMqttServiceCollectionExtensions.AddFluxFlowMqttClient(
                null!,
                "primary",
                options))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowMqttClient(" ", options))
            .ParamName.ShouldBe("name");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowMqttClient(
                "primary",
                (PulseMqttClientOptions)null!))
            .ParamName.ShouldBe("options");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowMqttClient(
                "primary",
                (Func<IServiceProvider, PulseMqttClientOptions>)null!))
            .ParamName.ShouldBe("optionsFactory");
    }

    [Fact]
    public async Task AddFluxFlowMqttClient_RejectsNullOptionsFactoryResult()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowMqttClient(
            "primary",
            static _ => null!);

        await using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<PulseMqttClient>("primary"));

        exception.Message.ShouldBe("MQTT client options factory returned null.");
    }

    [Fact]
    public void PulseMqttClientOptions_snapshots_user_properties()
    {
        var userProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant"] = "alpha"
        };

        var options = new PulseMqttClientOptions
        {
            Host = "localhost",
            UserProperties = userProperties
        };

        userProperties["tenant"] = "changed";
        userProperties["extra"] = "ignored";

        options.UserProperties.Count.ShouldBe(1);
        options.UserProperties["tenant"].ShouldBe("alpha");
        options.UserProperties.ContainsKey("extra").ShouldBeFalse();
    }

    [Fact]
    public void PulseMqttClientOptions_treats_null_user_properties_as_empty()
    {
        var options = new PulseMqttClientOptions
        {
            Host = "localhost",
            UserProperties = null!
        };

        options.UserProperties.ShouldBeEmpty();
    }
}
