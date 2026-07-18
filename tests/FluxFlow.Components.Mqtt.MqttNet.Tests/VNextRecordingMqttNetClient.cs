using MQTTnet;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Packets;

namespace FluxFlow.Components.Mqtt.MqttNet.Tests;

internal sealed class VNextRecordingMqttNetClient : IMqttClient
{
    public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceivedAsync;
    public event Func<MqttClientConnectedEventArgs, Task>? ConnectedAsync
    {
        add { }
        remove { }
    }
    public event Func<MqttClientConnectingEventArgs, Task>? ConnectingAsync
    {
        add { }
        remove { }
    }
    public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync
    {
        add { }
        remove { }
    }
    public event Func<InspectMqttPacketEventArgs, Task>? InspectPacketAsync
    {
        add { }
        remove { }
    }

    public bool IsConnected { get; private set; }

    public MqttClientOptions Options { get; private set; } = new();

    public MqttClientConnectResult ConnectResult { get; set; } = new()
    {
        ResultCode = MqttClientConnectResultCode.Success
    };

    public MqttApplicationMessage? Published { get; private set; }

    public MqttClientSubscribeOptions? Subscribed { get; private set; }

    public MqttClientUnsubscribeOptions? Unsubscribed { get; private set; }

    public MqttApplicationMessageReceivedEventArgs? LastReceived { get; private set; }

    public int AcknowledgementCount { get; private set; }

    public Task<MqttClientConnectResult> ConnectAsync(
        MqttClientOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Options = options;
        IsConnected = ConnectResult.ResultCode == MqttClientConnectResultCode.Success;
        return Task.FromResult(ConnectResult);
    }

    public Task DisconnectAsync(
        MqttClientDisconnectOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task PingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<MqttClientSubscribeResult> SubscribeAsync(
        MqttClientSubscribeOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Subscribed = options;
        return Task.FromResult(new MqttClientSubscribeResult(
            packetIdentifier: 1,
            Array.Empty<MqttClientSubscribeResultItem>(),
            reasonString: null,
            Array.Empty<MqttUserProperty>()));
    }

    public Task<MqttClientUnsubscribeResult> UnsubscribeAsync(
        MqttClientUnsubscribeOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Unsubscribed = options;
        return Task.FromResult(new MqttClientUnsubscribeResult(
            packetIdentifier: 1,
            Array.Empty<MqttClientUnsubscribeResultItem>(),
            reasonString: null,
            Array.Empty<MqttUserProperty>()));
    }

    public async Task ReceiveAsync(MqttApplicationMessage message)
    {
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
        LastReceived = args;
        if (ApplicationMessageReceivedAsync is null)
            return;

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
