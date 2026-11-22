using MQTTnet;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using System.Threading.Channels;
using Polly;

namespace FluxFlow.Components.Mqtt;

/// <summary>Owns one native client. Serializes protocol operations against connection changes.</summary>
public sealed class MqttClientResource : IAsyncDisposable
{
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _options;
    private readonly bool _autoConnect;
    private readonly ResiliencePipeline _reconnect;
    private readonly Channel<bool> _reconnectRequests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite
    });
    private CancellationTokenSource? _reconnectCancellation;
    private int _connected;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MqttConnection? _connectionOwner;
    private object? _subscriberOwner;
    private int _disposed;
    private bool _connectDesired;
    private long _generation;
    internal long Generation => Interlocked.Read(ref _generation);
    internal event Action<FlowEvent>? StateChanged;
    internal Func<IMqttClient, bool, CancellationToken, Task>? RestoreSubscriptions { get; set; }
    internal IMqttClient Client => _client;
    public bool IsConnected => _client.IsConnected;

    public MqttClientResource(MqttClientResourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Client);
        if (options.Client.Timeout != Timeout.InfiniteTimeSpan &&
            (options.Client.Timeout <= TimeSpan.Zero || options.Client.Timeout.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(options.Client.Timeout));
        _options = options.Client;
        var pipeline = new ResiliencePipelineBuilder { Name = "mqtt.reconnect" };
        if (options.ReconnectPolicy is { } retry) pipeline.AddRetry(retry);
        _reconnect = pipeline.Build();
        _autoConnect = options.AutoConnect;
        _connectDesired = _autoConnect;
        if (_connectDesired) _reconnectRequests.Writer.TryWrite(true);
        _client = new MqttClientFactory().CreateMqttClient();
        // No subscriber (including the unsubscribe/disposal interval) means no accepted delivery.
        // The subscriber explicitly enables automatic acknowledgement after output admission.
        _client.ApplicationMessageReceivedAsync += args =>
        {
            args.AutoAcknowledge = false;
            return Task.CompletedTask;
        };
        _client.ConnectingAsync += _ =>
        {
            Interlocked.Increment(ref _generation);
            NotifyState("mqtt.connecting");
            return Task.CompletedTask;
        };
        _client.ConnectedAsync += _ =>
        {
            NotifyState("mqtt.connected");
            return Task.CompletedTask;
        };
        _client.DisconnectedAsync += args =>
        {
            if (_client.IsConnected) return Task.CompletedTask; // Ignore a delayed notification from an older connection.
            NotifyState("mqtt.disconnected", args.Reason.ToString());
            if (Interlocked.Exchange(ref _connected, 0) != 0) _reconnectRequests.Writer.TryWrite(true);
            return Task.CompletedTask;
        };
    }

    internal void ClaimConnection(MqttConnection owner)
    {
        if (Interlocked.CompareExchange(ref _connectionOwner, owner, null) is not null)
            throw new InvalidOperationException("An MQTT client resource supports one connection component.");
        _connectDesired = _autoConnect;
        if (_connectDesired) _reconnectRequests.Writer.TryWrite(true);
    }

    internal void ClaimSubscriber(object owner)
    {
        if (Interlocked.CompareExchange(ref _subscriberOwner, owner, null) is not null)
            throw new InvalidOperationException("An MQTT client resource supports one subscriber with multiple topic filters.");
    }

    internal void ReleaseConnection(MqttConnection owner) => Interlocked.CompareExchange(ref _connectionOwner, null, owner);
    internal void ReleaseSubscriber(object owner) => Interlocked.CompareExchange(ref _subscriberOwner, null, owner);

    internal async Task<T> ExecuteAsync<T>(Func<IMqttClient, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return await operation(_client, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    internal TimeSpan OperationTimeout => _options.Timeout;

    internal Task<MqttClientPublishResult> PublishAsync(MqttApplicationMessage publication, CancellationToken cancellationToken)
        => ExecuteAsync(async (client, token) =>
        {
            var stopping = Volatile.Read(ref _connectionOwner)?.ConnectionStopping ?? default;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, stopping);
            timeout.CancelAfter(OperationTimeout);
            timeout.Token.ThrowIfCancellationRequested();
            return await client.PublishAsync(publication, timeout.Token).ConfigureAwait(false);
        }, cancellationToken);

    // Called only while ExecuteAsync holds the operation gate; receive callbacks never take it.
    private async Task ConnectAsync(IMqttClient client, CancellationToken cancellationToken)
    {
        if (client.IsConnected) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        try
        {
            var result = await client.ConnectAsync(_options, timeout.Token).ConfigureAwait(false);
            if (result.ResultCode != MqttClientConnectResultCode.Success)
                throw new InvalidOperationException($"MQTT connection refused: {result.ResultCode}.");
            if (RestoreSubscriptions is { } restore)
                await restore(client, result.IsSessionPresent, timeout.Token).ConfigureAwait(false);
            if (!client.IsConnected) throw new IOException("MQTT connection closed while restoring subscriptions.");
            Volatile.Write(ref _connected, 1);
            // A disconnect can arrive between the readiness check and arming loss detection.
            if (!client.IsConnected && Interlocked.Exchange(ref _connected, 0) != 0)
                _reconnectRequests.Writer.TryWrite(true);
        }
        catch (Exception error)
        {
            var timedOut = timeout.IsCancellationRequested;
            using var disconnectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await DisconnectAsync(client, disconnectTimeout.Token).ConfigureAwait(false); }
            catch { /* Preserve the subscription failure; reconnection remains enabled. */ }
            cancellationToken.ThrowIfCancellationRequested();
            if (timedOut)
                throw new TimeoutException("MQTT connection attempt timed out.", error);
            throw;
        }
    }

    internal Task<bool> ExecuteCommandAsync(MqttConnectionCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(async (client, token) =>
        {
            switch (command.Command)
            {
                case MqttConnectionOperation.Connect:
                    _connectDesired = true;
                    _reconnectCancellation?.Cancel();
                    try { await ConnectAsync(client, token).ConfigureAwait(false); }
                    catch { _reconnectRequests.Writer.TryWrite(true); throw; }
                    break;
                case MqttConnectionOperation.Disconnect:
                    _connectDesired = _autoConnect && !command.DisableReconnect;
                    _reconnectCancellation?.Cancel();
                    try { await DisconnectAsync(client, token).ConfigureAwait(false); }
                    finally
                    {
                        if (_connectDesired) _reconnectRequests.Writer.TryWrite(true);
                    }
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(command));
            }
            return client.IsConnected;
        }, cancellationToken);

    internal async Task RunAsync(Func<FlowError, CancellationToken, ValueTask> reportError, CancellationToken cancellationToken)
    {
        while (await _reconnectRequests.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_reconnectRequests.Reader.TryRead(out _)) { }
            var cycle = await ExecuteAsync((client, token) =>
            {
                if (!_connectDesired || client.IsConnected) return Task.FromResult<CancellationTokenSource?>(null);
                _reconnectCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                return Task.FromResult<CancellationTokenSource?>(_reconnectCancellation);
            }, cancellationToken).ConfigureAwait(false);
            if (cycle is null) continue;
            try
            {
                await _reconnect.ExecuteAsync(async token =>
                {
                    try
                    {
                        await ExecuteAsync(async (client, attemptToken) =>
                        {
                            attemptToken.ThrowIfCancellationRequested();
                            if (_connectDesired) await ConnectAsync(client, attemptToken).ConfigureAwait(false);
                            return true;
                        }, token).ConfigureAwait(false);
                    }
                    catch (Exception error) when (!token.IsCancellationRequested)
                    {
                        await reportError(new FlowError("mqtt.connection_failed", error.Message, "mqtt"), token).ConfigureAwait(false);
                        throw;
                    }
                }, cycle.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cycle.IsCancellationRequested) { }
            catch (Exception error)
            {
                StateChanged?.Invoke(new FlowEvent
                {
                    Name = "mqtt.reconnect_exhausted", Kind = FlowEventKind.Diagnostic,
                    Level = FlowEventLevel.Error, Message = error.Message
                });
            }
            finally
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (ReferenceEquals(_reconnectCancellation, cycle)) _reconnectCancellation = null;
                    cycle.Dispose();
                }
                finally { _gate.Release(); }
            }
        }
    }

    internal async ValueTask StopAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ExecuteCommandAsync(new() { Command = MqttConnectionOperation.Disconnect, DisableReconnect = true }, timeout.Token).ConfigureAwait(false);
    }

    private async Task DisconnectAsync(IMqttClient client, CancellationToken cancellationToken)
    {
        // Protocol cleanup cannot start a new retry budget; the command decides whether to reconnect.
        Interlocked.Exchange(ref _connected, 0);
        if (!client.IsConnected) return;
        NotifyState("mqtt.disconnecting");
        await client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken).ConfigureAwait(false);
    }

    private void NotifyState(string name, string? message = null)
        => StateChanged?.Invoke(new FlowEvent { Name = name, Kind = FlowEventKind.Lifecycle, Message = message });

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _connectDesired = false;
            _reconnectCancellation?.Cancel();
            _reconnectRequests.Writer.TryComplete();
            try
            {
                if (_client.IsConnected)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await DisconnectAsync(_client, timeout.Token).ConfigureAwait(false);
                }
            }
            finally { _client.Dispose(); }
        }
        finally { _gate.Release(); }
    }
}
