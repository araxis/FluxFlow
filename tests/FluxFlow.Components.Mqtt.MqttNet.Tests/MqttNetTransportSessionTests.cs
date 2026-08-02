using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Data;
using MQTTnet;
using MQTTnet.Protocol;
using Shouldly;
using System.Net;
using Xunit;

namespace FluxFlow.Components.Mqtt.MqttNet.Tests;

public sealed class MqttNetTransportSessionTests
{
    [Fact]
    public async Task SessionMapsConfigurationPublishAndSubscriptionsWithoutOwningPolicy()
    {
        var provider = new VNextRecordingMqttNetClient();
        var factory = new MqttNetTransportFactory(
            new MqttClientFactory(),
            () => provider);
        await using var session = await factory.CreateAsync(Configuration());

        await session.ConnectAsync();
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
        });
        await session.SubscribeAsync("name:events", new MqttSubscriptionDefinition
        {
            TopicFilter = "events/+",
            Qos = MqttQos.AtLeastOnce,
            NoLocal = true,
            RetainAsPublished = true,
            RetainHandling =
                FluxFlow.Components.Mqtt.Subscriptions.MqttRetainHandling.SendOnNewSubscription
        });
        await session.UnsubscribeAsync("name:events");

        provider.Options.ClientId.ShouldBe("client-1");
        provider.Options.CleanSession.ShouldBeFalse();
        provider.Options.KeepAlivePeriod.ShouldBe(TimeSpan.FromSeconds(20));
        ((DnsEndPoint)((MqttClientTcpOptions)provider.Options.ChannelOptions).RemoteEndpoint)
            .Host.ShouldBe("broker.internal");
        provider.Published!.Topic.ShouldBe("events/one");
        provider.Published.Payload.FirstSpan.ToArray().ShouldBe([1, 2, 3]);
        provider.Published.QualityOfServiceLevel.ShouldBe(MqttQualityOfServiceLevel.AtLeastOnce);
        provider.Published.ResponseTopic.ShouldBe("responses/one");
        provider.Subscribed!.TopicFilters.Single().Topic.ShouldBe("events/+");
        provider.Unsubscribed!.TopicFilters.ShouldBe(["events/+"], ignoreOrder: false);
    }

    [Fact]
    public async Task SessionDefersQosDeliveryAndCompletesProviderAcknowledgementOnce()
    {
        var provider = new VNextRecordingMqttNetClient();
        var factory = new MqttNetTransportFactory(
            new MqttClientFactory(),
            () => provider);
        await using var session = await factory.CreateAsync(Configuration());
        await session.ConnectAsync();
        var next = ReadNextAsync(session.Messages);

        await provider.ReceiveAsync(new MqttApplicationMessageBuilder()
            .WithTopic("events/one")
            .WithPayload([4, 5, 6])
            .WithContentType("application/octet-stream")
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build());
        var received = await next.WaitAsync(TimeSpan.FromSeconds(5));

        received.Message.Topic.ShouldBe("events/one");
        received.Message.Content.Bytes.ToArray().ShouldBe([4, 5, 6]);
        received.Delivery.IsEmpty.ShouldBeFalse();
        provider.LastReceived!.AutoAcknowledge.ShouldBeFalse();

        await session.AcknowledgeAsync(received.Delivery, MqttWorkflowOutcome.Nak);
        await session.AcknowledgeAsync(received.Delivery, MqttWorkflowOutcome.Ack);

        provider.AcknowledgementCount.ShouldBe(1);
        provider.LastReceived.ProcessingFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task FactoryHonorsCancellationBeforeCreatingSession()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new MqttNetTransportFactory();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await factory.CreateAsync(Configuration(), cancellation.Token));
    }

    [Fact]
    public async Task ConnectRejectionPreservesAuthenticationClassification()
    {
        var provider = new VNextRecordingMqttNetClient
        {
            ConnectResult = new MqttClientConnectResult
            {
                ResultCode = MqttClientConnectResultCode.NotAuthorized,
                ReasonString = "blocked"
            }
        };
        var factory = new MqttNetTransportFactory(
            new MqttClientFactory(),
            () => provider);
        await using var session = await factory.CreateAsync(Configuration());

        var exception = await Should.ThrowAsync<MqttTransportException>(async () =>
            await session.ConnectAsync());

        exception.Category.ShouldBe("Authentication");
        exception.IsTransient.ShouldBeFalse();
        session.IsConnected.ShouldBeFalse();
    }

    private static MqttClientConfiguration Configuration()
        => new()
        {
            Name = "client-1",
            ClientId = "client-1",
            Broker = new MqttBrokerConfiguration
            {
                Host = "broker.internal",
                Port = 1884
            },
            CleanStart = false,
            KeepAlive = TimeSpan.FromSeconds(20),
            AutoConnect = MqttAutoConnectMode.Disabled
        };

    private static async Task<T> ReadNextAsync<T>(IAsyncEnumerable<T> source)
    {
        await foreach (var value in source)
            return value;
        throw new InvalidOperationException("The MQTT transport stream completed without a value.");
    }
}
