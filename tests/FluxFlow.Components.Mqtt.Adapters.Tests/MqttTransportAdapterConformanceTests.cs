using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.MqttNet;
using FluxFlow.Components.Mqtt.PulseMqtt;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Data;
using MQTTnet;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using Pulse.Mqtt.Testing;
using Shouldly;
using Xunit;
using CoreRetainHandling = FluxFlow.Components.Mqtt.Subscriptions.MqttRetainHandling;

namespace FluxFlow.Components.Mqtt.Adapters.Tests;

public abstract class MqttTransportAdapterConformanceTests
{
    [Fact]
    public async Task AdapterConformsToLifecycleDeliveryAndAcknowledgementContract()
    {
        await using var environment = await CreateEnvironmentAsync();
        await using var session = await environment.Factory.CreateAsync(Configuration());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        session.Capabilities.DeferredAcknowledgement.ShouldBeTrue();
        await session.ConnectAsync(timeout.Token);
        session.IsConnected.ShouldBeTrue();
        await session.SubscribeAsync("name:events", new MqttSubscriptionDefinition
        {
            TopicFilter = "events/+",
            Qos = MqttQos.AtLeastOnce,
            RetainAsPublished = true,
            RetainHandling = CoreRetainHandling.SendOnNewSubscription
        }, timeout.Token);
        var next = ReadNextAsync(session.Messages, timeout.Token);

        await environment.DeliverAsync(session, Message(), timeout.Token);
        var delivery = await next;

        delivery.Message.Topic.ShouldBe("events/one");
        delivery.Message.Content.Bytes.ToArray().ShouldBe([1, 2, 3]);
        delivery.Message.Content.ContentType.ShouldBe("application/octet-stream");
        delivery.Message.Qos.ShouldBe(MqttQos.AtLeastOnce);
        delivery.Message.CorrelationData.ShouldBe("correlation-1");
        delivery.Message.ResponseTopic.ShouldBe("responses/one");
        delivery.Message.UserProperties["tenant"].ShouldBe("north");
        delivery.Delivery.IsEmpty.ShouldBeFalse();

        await session.AcknowledgeAsync(delivery.Delivery, MqttWorkflowOutcome.Ack, timeout.Token);
        await session.AcknowledgeAsync(delivery.Delivery, MqttWorkflowOutcome.Nak, timeout.Token);
        if (environment.AcknowledgementCount is { } acknowledgementCount)
            acknowledgementCount.ShouldBe(1);
        await session.UnsubscribeAsync("name:events", timeout.Token);

        using (var canceledDisconnect = new CancellationTokenSource())
        {
            canceledDisconnect.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(async () =>
                await session.DisconnectAsync(cancellationToken: canceledDisconnect.Token));
        }
        session.IsConnected.ShouldBeTrue();

        await session.DisconnectAsync(cancellationToken: timeout.Token);
        session.IsConnected.ShouldBeFalse();

        await session.ConnectAsync(timeout.Token);
        session.IsConnected.ShouldBeTrue();
    }

    [Fact]
    public async Task FactoryRejectsPreCanceledCreation()
    {
        await using var environment = await CreateEnvironmentAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await environment.Factory.CreateAsync(Configuration(), cancellation.Token));
    }

    [Fact]
    public async Task AdapterAcceptsImmutableExactContent()
    {
        await using var environment = await CreateEnvironmentAsync();
        await using var session = await environment.Factory.CreateAsync(Configuration());
        await session.ConnectAsync();

        byte[] bytes = [1, 2, 3];
        var message = new MqttPublishMessage
        {
            Topic = "events/one",
            Content = FlowContent.FromBytes(bytes)
        };
        bytes[0] = 9;

        message.Content.Bytes.ToArray().ShouldBe([1, 2, 3]);
        await session.PublishAsync(message);
    }

    protected abstract ValueTask<AdapterEnvironment> CreateEnvironmentAsync();

    private static MqttClientConfiguration Configuration()
        => new()
        {
            Name = "client-1",
            ClientId = "client-1",
            Broker = new MqttBrokerConfiguration { Host = "broker.internal" },
            AutoConnect = MqttAutoConnectMode.Disabled
        };

    private static MqttPublishMessage Message()
        => new()
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
        };

    private static async Task<T> ReadNextAsync<T>(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken)
    {
        await foreach (var value in source.WithCancellation(cancellationToken))
            return value;
        throw new InvalidOperationException("The MQTT transport stream completed without a value.");
    }

    protected abstract class AdapterEnvironment : IAsyncDisposable
    {
        public abstract IMqttTransportFactory Factory { get; }

        public abstract int? AcknowledgementCount { get; }

        public abstract ValueTask DeliverAsync(
            IMqttTransportSession session,
            MqttPublishMessage message,
            CancellationToken cancellationToken);

        public abstract ValueTask DisposeAsync();
    }
}

public sealed class MqttNetAdapterConformanceTests : MqttTransportAdapterConformanceTests
{
    protected override ValueTask<AdapterEnvironment> CreateEnvironmentAsync()
        => ValueTask.FromResult<AdapterEnvironment>(new MqttNetEnvironment());

    private sealed class MqttNetEnvironment : AdapterEnvironment
    {
        private readonly RecordingClient _client = new();

        public override IMqttTransportFactory Factory { get; }

        public override int? AcknowledgementCount => _client.AcknowledgementCount;

        public MqttNetEnvironment()
        {
            Factory = new MqttNetTransportFactory(
                new MqttClientFactory(),
                () => _client);
        }

        public override async ValueTask DeliverAsync(
            IMqttTransportSession session,
            MqttPublishMessage message,
            CancellationToken cancellationToken)
        {
            await session.PublishAsync(message, cancellationToken);
            await _client.ReceivePublishedAsync();
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed class PulseMqttAdapterConformanceTests : MqttTransportAdapterConformanceTests
{
    protected override ValueTask<AdapterEnvironment> CreateEnvironmentAsync()
        => ValueTask.FromResult<AdapterEnvironment>(new PulseEnvironment());

    private sealed class PulseEnvironment : AdapterEnvironment
    {
        private readonly PulseMqttTestBroker _broker = new();

        public override IMqttTransportFactory Factory => new PulseMqttTransportFactory(_broker);

        public override int? AcknowledgementCount => null;

        public override async ValueTask DeliverAsync(
            IMqttTransportSession session,
            MqttPublishMessage message,
            CancellationToken cancellationToken)
        {
            await session.PublishAsync(message, cancellationToken);
        }

        public override ValueTask DisposeAsync() => _broker.DisposeAsync();
    }
}

internal sealed class RecordingClient : IMqttClient
{
    public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceivedAsync;
    public event Func<MqttClientConnectedEventArgs, Task>? ConnectedAsync { add { } remove { } }
    public event Func<MqttClientConnectingEventArgs, Task>? ConnectingAsync { add { } remove { } }
    public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync { add { } remove { } }
    public event Func<InspectMqttPacketEventArgs, Task>? InspectPacketAsync { add { } remove { } }

    public bool IsConnected { get; private set; }

    public MqttClientOptions Options { get; private set; } = new();

    public MqttApplicationMessage? Published { get; private set; }

    public int AcknowledgementCount { get; private set; }

    public Task<MqttClientConnectResult> ConnectAsync(
        MqttClientOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Options = options;
        IsConnected = true;
        return Task.FromResult(new MqttClientConnectResult
        {
            ResultCode = MqttClientConnectResultCode.Success
        });
    }

    public Task DisconnectAsync(
        MqttClientDisconnectOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task PingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<MqttClientPublishResult> PublishAsync(
        MqttApplicationMessage applicationMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Published = applicationMessage;
        return Task.FromResult(new MqttClientPublishResult(
            packetIdentifier: null,
            MqttClientPublishReasonCode.Success,
            reasonString: null,
            Array.Empty<MqttUserProperty>()));
    }

    public Task SendEnhancedAuthenticationExchangeDataAsync(
        MqttEnhancedAuthenticationExchangeData data,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<MqttClientSubscribeResult> SubscribeAsync(
        MqttClientSubscribeOptions options,
        CancellationToken cancellationToken)
        => Task.FromResult(new MqttClientSubscribeResult(
            packetIdentifier: 1,
            Array.Empty<MqttClientSubscribeResultItem>(),
            reasonString: null,
            Array.Empty<MqttUserProperty>()));

    public Task<MqttClientUnsubscribeResult> UnsubscribeAsync(
        MqttClientUnsubscribeOptions options,
        CancellationToken cancellationToken)
        => Task.FromResult(new MqttClientUnsubscribeResult(
            packetIdentifier: 1,
            Array.Empty<MqttClientUnsubscribeResultItem>(),
            reasonString: null,
            Array.Empty<MqttUserProperty>()));

    public async Task ReceivePublishedAsync()
    {
        var message = Published ?? throw new InvalidOperationException("Publish before receiving.");
        var args = new MqttApplicationMessageReceivedEventArgs(
            "test-client",
            message,
            new MqttPublishPacket { PacketIdentifier = 1 },
            (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                AcknowledgementCount++;
                return Task.CompletedTask;
            });
        if (ApplicationMessageReceivedAsync is null)
            throw new InvalidOperationException("No MQTT message handler is registered.");
        foreach (Func<MqttApplicationMessageReceivedEventArgs, Task> handler in
            ApplicationMessageReceivedAsync.GetInvocationList())
        {
            await handler(args).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        IsConnected = false;
        ApplicationMessageReceivedAsync = null;
    }
}
