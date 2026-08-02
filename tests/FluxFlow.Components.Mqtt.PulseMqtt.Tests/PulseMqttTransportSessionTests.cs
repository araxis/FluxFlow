using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Data;
using Pulse.Mqtt.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.PulseMqtt.Tests;

public sealed class PulseMqttTransportSessionTests
{
    [Fact]
    public async Task SessionMapsExactContentAndSupportsReconnectWithoutOwningPolicy()
    {
        await using var broker = new PulseMqttTestBroker();
        var factory = new PulseMqttTransportFactory(broker);
        await using var session = await factory.CreateAsync(Configuration());

        await session.ConnectAsync();
        await session.SubscribeAsync("name:events", new MqttSubscriptionDefinition
        {
            TopicFilter = "events/+",
            Qos = MqttQos.AtLeastOnce,
            NoLocal = false,
            RetainAsPublished = true,
            RetainHandling = MqttRetainHandling.SendOnNewSubscription
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var next = ReadNextAsync(session.Messages, timeout.Token);

        await session.PublishAsync(new MqttPublishMessage
        {
            Topic = "events/one",
            Content = FlowContent.FromBytes(new byte[] { 1, 2, 3 }, "application/octet-stream"),
            Qos = MqttQos.AtLeastOnce,
            CorrelationData = "correlation-1",
            ResponseTopic = "responses/one",
            UserProperties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tenant"] = "north"
            }
        }, timeout.Token);
        var received = await next;

        received.Message.Topic.ShouldBe("events/one");
        received.Message.Content.Bytes.ToArray().ShouldBe([1, 2, 3]);
        received.Message.Content.ContentType.ShouldBe("application/octet-stream");
        received.Message.Qos.ShouldBe(MqttQos.AtLeastOnce);
        received.Message.CorrelationData.ShouldBe("correlation-1");
        received.Message.ResponseTopic.ShouldBe("responses/one");
        received.Message.UserProperties["tenant"].ShouldBe("north");
        received.Delivery.IsEmpty.ShouldBeFalse();

        await session.AcknowledgeAsync(received.Delivery, MqttWorkflowOutcome.Ack, timeout.Token);
        await session.AcknowledgeAsync(received.Delivery, MqttWorkflowOutcome.Nak, timeout.Token);
        await session.UnsubscribeAsync("name:events", timeout.Token);
        await session.DisconnectAsync(cancellationToken: timeout.Token);
        session.IsConnected.ShouldBeFalse();

        await session.ConnectAsync(timeout.Token);
        session.IsConnected.ShouldBeTrue();
    }

    [Fact]
    public async Task FactoryHonorsCancellationBeforeCreatingSession()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new PulseMqttTransportFactory();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await factory.CreateAsync(Configuration(), cancellation.Token));
    }

    private static MqttClientConfiguration Configuration()
        => new()
        {
            Name = "client-1",
            ClientId = "client-1",
            Broker = new MqttBrokerConfiguration { Host = "broker.internal" },
            AutoConnect = MqttAutoConnectMode.Disabled
        };

    private static async Task<T> ReadNextAsync<T>(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken)
    {
        await foreach (var value in source.WithCancellation(cancellationToken))
            return value;
        throw new InvalidOperationException("The MQTT transport stream completed without a value.");
    }
}
