using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using Pulse.Mqtt.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.PulseMqtt.Tests;

public sealed class PulseMqttClientTests
{
    [Fact]
    public void Constructor_RejectsMissingHostWithoutCustomTransport()
        => Should.Throw<ArgumentException>(() => new PulseMqttClient(
            new PulseMqttClientOptions()));

    [Fact]
    public void Constructor_RejectsInvalidLastWillTopic()
        => Should.Throw<ArgumentException>(() => new PulseMqttClient(
            new PulseMqttClientOptions
            {
                Host = "localhost",
                LastWill = new PulseMqttLastWillOptions
                {
                    Topic = "devices/#",
                    Payload = [1]
                }
            }));

    [Fact]
    public async Task PublishAsync_ThrowsUnavailableWhenDisconnected()
    {
        await using var client = new PulseMqttClient(
            new PulseMqttClientOptions { Host = "localhost" });

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
        await using var client = new PulseMqttClient(
            new PulseMqttClientOptions { Host = "localhost" });

        var exception = await Should.ThrowAsync<MqttClientUnavailableException>(
            async () => await client.SubscribeAsync(new MqttTriggerOptions
            {
                TopicFilter = "devices/+"
            }));

        exception.Message.ShouldContain("not connected");
    }

    [Fact]
    public async Task SubscribeAsync_RejectsManualAcknowledgement()
    {
        await using var broker = new PulseMqttTestBroker();
        await using var client = new PulseMqttClient(
            new PulseMqttClientOptions
            {
                ClientId = "fluxflow-pulse-manual-ack",
                ConnectTimeout = TimeSpan.FromSeconds(5)
            },
            transportFactory: broker);
        await client.ConnectAsync();

        await Should.ThrowAsync<NotSupportedException>(
            async () => await client.SubscribeAsync(new MqttTriggerOptions
            {
                TopicFilter = "devices/+",
                Acknowledgement = MqttTriggerAcknowledgement.OnEmit
            }));
    }

    [Fact]
    public async Task PublishAsync_DeliversToSubscriptionThroughTestBroker()
    {
        await using var broker = new PulseMqttTestBroker();
        await using var client = new PulseMqttClient(
            new PulseMqttClientOptions
            {
                ClientId = "fluxflow-pulse-loopback",
                ConnectTimeout = TimeSpan.FromSeconds(5)
            },
            transportFactory: broker);
        await client.ConnectAsync();

        await using var subscription = await client.SubscribeAsync(new MqttTriggerOptions
        {
            TopicFilter = "devices/+",
            QualityOfService = MqttQualityOfService.AtLeastOnce
        });

        await client.PublishAsync(new MqttPublishRequest
        {
            Topic = "devices/a",
            Payload = [7, 8, 9],
            QualityOfService = MqttQualityOfService.AtLeastOnce,
            Properties = new MqttPublishProperties
            {
                CorrelationId = "corr-live"
            }
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var context = await ReadNextAsync(subscription, timeout.Token);

        context.Message.Topic.ShouldBe("devices/a");
        context.Message.Payload.ShouldBe([7, 8, 9]);
        context.Message.QualityOfService.ShouldBe(MqttQualityOfService.AtLeastOnce);
        context.Message.CorrelationId.ShouldBe("corr-live");
    }

    private static async Task<IMqttReceivedContext> ReadNextAsync(
        IMqttSubscription subscription,
        CancellationToken cancellationToken)
    {
        await foreach (var context in subscription.Messages
            .WithCancellation(cancellationToken))
        {
            return context;
        }

        throw new InvalidOperationException("The subscription completed without a message.");
    }
}
