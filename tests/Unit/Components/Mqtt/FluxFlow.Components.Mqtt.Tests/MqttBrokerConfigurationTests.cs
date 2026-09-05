using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Options;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttBrokerConfigurationTests
{
    [Fact]
    public void Broker_transport_contract_is_provider_neutral_and_has_stable_defaults()
    {
        Enum.GetNames<MqttBrokerTransport>().ShouldBe(["Tcp", "WebSocket"]);
        Enum.GetValues<MqttBrokerTransport>()
            .Select(static value => (int)value)
            .ShouldBe([0, 1]);

        var broker = new MqttBrokerConfiguration { Host = "broker.internal" };

        broker.Transport.ShouldBe(MqttBrokerTransport.Tcp);
        broker.UseTls.ShouldBeFalse();
        broker.WebSocketPath.ShouldBe("/mqtt");
        broker.ServerName.ShouldBeNull();
    }

    [Theory]
    [InlineData(MqttBrokerTransport.Tcp, false)]
    [InlineData(MqttBrokerTransport.Tcp, true)]
    [InlineData(MqttBrokerTransport.WebSocket, false)]
    [InlineData(MqttBrokerTransport.WebSocket, true)]
    public async Task Configuration_accepts_all_four_portable_transport_modes_without_host_capability_checks(
        MqttBrokerTransport transport,
        bool useTls)
    {
        var broker = new MqttBrokerConfiguration
        {
            Host = "broker.internal",
            Port = 9443,
            Transport = transport,
            UseTls = useTls,
            WebSocketPath = transport == MqttBrokerTransport.WebSocket
                ? "/tenant/mqtt"
                : "/mqtt"
        };
        var configuration = Configuration(broker);
        var factory = new VNextRecordingMqttTransportFactory();
        await using var controller = new MqttClientController(configuration, factory);

        await controller.StartAsync();

        factory.Configurations.ShouldHaveSingleItem().ShouldBeSameAs(configuration);
        factory.Sessions.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(99, "/mqtt", null, typeof(ArgumentOutOfRangeException))]
    [InlineData(1, null, null, typeof(ArgumentException))]
    [InlineData(1, "", null, typeof(ArgumentException))]
    [InlineData(1, " ", null, typeof(ArgumentException))]
    [InlineData(1, "mqtt", null, typeof(ArgumentException))]
    [InlineData(1, "/mqtt?token=value", null, typeof(ArgumentException))]
    [InlineData(1, "/mqtt#fragment", null, typeof(ArgumentException))]
    [InlineData(1, "/mqtt", "sni.internal", typeof(NotSupportedException))]
    [InlineData(0, "/custom", null, typeof(ArgumentException))]
    public void Configuration_rejects_invalid_transport_path_and_websocket_server_name_before_transport_creation(
        int transport,
        string? webSocketPath,
        string? serverName,
        Type expectedException)
    {
        var factory = new VNextRecordingMqttTransportFactory();
        var configuration = Configuration(new MqttBrokerConfiguration
        {
            Host = "broker.internal",
            Transport = (MqttBrokerTransport)transport,
            WebSocketPath = webSocketPath!,
            ServerName = serverName
        });

        var exception = Record.Exception(() =>
            new MqttClientController(configuration, factory));

        exception.ShouldNotBeNull().GetType().ShouldBe(expectedException);
        factory.Configurations.ShouldBeEmpty();
        factory.Sessions.ShouldBeEmpty();
    }

    private static MqttClientConfiguration Configuration(MqttBrokerConfiguration broker)
        => new()
        {
            Name = "client-1",
            ClientId = "client-1",
            Broker = broker,
            AutoConnect = MqttAutoConnectMode.Disabled,
            Reconnect = new MqttReconnectConfiguration { Enabled = false }
        };
}
