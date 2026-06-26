using FluxFlow.Components.Mqtt.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.MqttNet.Tests;

public sealed class MqttNetRegistrationTests
{
    [Fact]
    public async Task AddFluxFlowMqttClient_RegistersKeyedClientContracts()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowMqttClient(
            "primary",
            new MqttNetClientOptions { Host = "localhost" });

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredKeyedService<MqttNetClient>("primary");

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
            new MqttNetClientOptions { Host = "localhost" });

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredKeyedService<MqttNetClient>("primary");

        provider.GetRequiredKeyedService<IMqttPublisher>("primary").ShouldBeSameAs(client);
        provider.GetRequiredKeyedService<IMqttTriggerSource>("primary").ShouldBeSameAs(client);
        provider.GetRequiredKeyedService<IMqttClientHealthSource>("primary").ShouldBeSameAs(client);
    }

    [Fact]
    public async Task AddFluxFlowMqttClient_DoesNotRegisterHostedLifetimeByDefault()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowMqttClient(
            "primary",
            new MqttNetClientOptions { Host = "localhost" });

        await using var provider = services.BuildServiceProvider();
        var lifetimes = provider.GetServices<IHostedService>().ToArray();

        lifetimes.Length.ShouldBe(0);
    }

    [Fact]
    public async Task AddFluxFlowMqttClient_CanRegisterHostedLifetime()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowMqttClient(
            "primary",
            new MqttNetClientOptions { Host = "localhost" },
            new MqttClientRegistrationOptions { ConnectWithHost = true });

        await using var provider = services.BuildServiceProvider();
        var lifetimes = provider.GetServices<IHostedService>().ToArray();

        lifetimes.Length.ShouldBe(1);
    }

    [Fact]
    public void AddFluxFlowMqttClient_RejectsInvalidArguments()
    {
        var services = new ServiceCollection();
        var options = new MqttNetClientOptions { Host = "localhost" };

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
                (MqttNetClientOptions)null!))
            .ParamName.ShouldBe("options");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowMqttClient(
                "primary",
                (Func<IServiceProvider, MqttNetClientOptions>)null!))
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
            provider.GetRequiredKeyedService<MqttNetClient>("primary"));

        exception.Message.ShouldBe("MQTT client options factory returned null.");
    }

    [Fact]
    public async Task AddFluxFlowMqttClient_UsesServiceProviderFactories()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddFluxFlowMqttClient(
            "primary",
            provider => new MqttNetClientOptions
            {
                Host = provider.GetRequiredService<TimeProvider>() == TimeProvider.System
                    ? "localhost"
                    : "invalid"
            },
            registrationOptions: null,
            provider => provider.GetRequiredService<TimeProvider>());

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredKeyedService<MqttNetClient>("primary");

        client.IsConnected.ShouldBeFalse();
    }

    [Fact]
    public void MqttNetClientOptions_snapshots_user_properties()
    {
        var userProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant"] = "alpha"
        };

        var options = new MqttNetClientOptions
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
    public void MqttNetClientOptions_treats_null_user_properties_as_empty()
    {
        var options = new MqttNetClientOptions
        {
            Host = "localhost",
            UserProperties = null!
        };

        options.UserProperties.ShouldBeEmpty();
    }
}
