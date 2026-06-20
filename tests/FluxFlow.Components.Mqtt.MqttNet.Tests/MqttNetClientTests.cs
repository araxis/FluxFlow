using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.MqttNet.Tests;

public sealed class MqttNetClientTests
{
    [Fact]
    public void Constructor_RejectsMissingHost()
        => Should.Throw<ArgumentException>(() => new MqttNetClient(
            new MqttNetClientOptions { Host = "" }));

    [Fact]
    public void Constructor_RejectsInvalidLastWillTopic()
        => Should.Throw<ArgumentException>(() => new MqttNetClient(
            new MqttNetClientOptions
            {
                Host = "localhost",
                LastWill = new MqttNetLastWillOptions
                {
                    Topic = "devices/#",
                    Payload = [1]
                }
            }));

    [Fact]
    public async Task PublishAsync_ThrowsUnavailableWhenDisconnected()
    {
        await using var client = new MqttNetClient(
            new MqttNetClientOptions { Host = "localhost" });

        var exception = await Should.ThrowAsync<MqttClientUnavailableException>(
            async () => await client.PublishAsync(new MqttPublishRequest
            {
                Topic = "devices/a",
                Payload = [1]
            }));

        exception.Message.ShouldContain("not connected");
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsUnavailableWhenDisconnected()
    {
        await using var client = new MqttNetClient(
            new MqttNetClientOptions { Host = "localhost" });

        var exception = await Should.ThrowAsync<MqttClientUnavailableException>(
            async () => await client.SubscribeAsync(new MqttTriggerOptions
            {
                TopicFilter = "devices/+"
            }));

        exception.Message.ShouldContain("not connected");
    }
}
