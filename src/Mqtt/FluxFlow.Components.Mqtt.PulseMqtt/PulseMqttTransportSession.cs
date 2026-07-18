using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using Pulse.Mqtt;
using Pulse.Mqtt.Connection;
using Pulse.Mqtt.Packets;
using Pulse.Mqtt.Protocol;
using Pulse.Mqtt.Transport;
using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using CoreRetainHandling = FluxFlow.Components.Mqtt.Subscriptions.MqttRetainHandling;
using ProviderRetainHandling = Pulse.Mqtt.MqttRetainHandling;
using ProviderTransportFactory = Pulse.Mqtt.Transport.IMqttTransportFactory;

namespace FluxFlow.Components.Mqtt.PulseMqtt;

internal sealed class PulseMqttTransportSession : FluxFlow.Components.Mqtt.Transport.IMqttTransportSession
{
    private const int InboundCapacity = 256;

    private readonly MqttClientConfiguration _configuration;
    private readonly TimeProvider _clock;
    private readonly ProviderTransportFactory _transportFactory;
    private readonly X509Certificate2Collection _certificates;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly Channel<MqttTransportReceivedMessage> _messages = CreateChannel<MqttTransportReceivedMessage>();
    private readonly Channel<MqttTransportEvent> _events = CreateChannel<MqttTransportEvent>();
    private readonly ConcurrentDictionary<string, string> _subscriptions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MqttInboundPublishContext> _pending = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private ConnectionState? _connection;
    private int _disposed;

    public PulseMqttTransportSession(
        MqttClientConfiguration configuration,
        TimeProvider clock,
        ProviderTransportFactory? transportFactory = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (configuration.LastWill is { } lastWill)
            ValidateExactContent(lastWill, nameof(configuration));
        _certificates = transportFactory is null
            ? LoadCertificates(configuration.Certificates)
            : [];
        _transportFactory = transportFactory ??
            new TcpTransportFactory(new TcpTransportOptions
            {
                Host = configuration.Broker.Host,
                Port = configuration.Broker.Port,
                UseTls = configuration.Broker.UseTls,
                TlsTargetHost = configuration.Broker.ServerName ?? configuration.Broker.Host,
                ClientCertificates = _certificates.Count == 0 ? null : _certificates
            });
    }

    public MqttTransportCapabilities Capabilities { get; } = new()
    {
        DeferredAcknowledgement = true,
        NegativeAcknowledgement = true
    };

    public bool IsConnected => Volatile.Read(ref _connection)?.IsConnected == true;

    public IAsyncEnumerable<MqttTransportReceivedMessage> Messages =>
        _messages.Reader.ReadAllAsync();

    public IAsyncEnumerable<MqttTransportEvent> Events =>
        _events.Reader.ReadAllAsync();

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (IsConnected)
                return;

            if (Volatile.Read(ref _connection) is { } prior)
            {
                await prior.DisposeAsync().ConfigureAwait(false);
                Interlocked.CompareExchange(ref _connection, null, prior);
            }

            var client = new RawMqttClient(
                _transportFactory,
                new RawMqttClientOptions { InboundMessageCapacity = InboundCapacity },
                _clock);
            var state = new ConnectionState(client);
            client.AcknowledgedMessageSink = (context, token) =>
                OnMessageReceivedAsync(context, token);
            Volatile.Write(ref _connection, state);

            try
            {
                var result = await client.ConnectAsync(
                    BuildConnectPacket(),
                    cancellationToken).ConfigureAwait(false);
                if (result.ReasonCode != MqttReasonCode.Success)
                    throw CreateConnectRejection(result.ReasonCode, result.ReasonString);

                state.MarkConnected();
                state.Monitor = MonitorConnectionAsync(state);
                await WriteEventAsync(new MqttTransportEvent
                {
                    Kind = MqttTransportEventKind.Connected,
                    Message = "Connected to MQTT broker."
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await state.DisposeAsync().ConfigureAwait(false);
                Interlocked.CompareExchange(ref _connection, null, state);
                throw;
            }
            catch (MqttTransportException)
            {
                await state.DisposeAsync().ConfigureAwait(false);
                Interlocked.CompareExchange(ref _connection, null, state);
                throw;
            }
            catch (MqttProtocolException exception)
            {
                await state.DisposeAsync().ConfigureAwait(false);
                Interlocked.CompareExchange(ref _connection, null, state);
                throw Protocol("MQTT connection protocol failed.", exception);
            }
            catch (Exception exception)
            {
                await state.DisposeAsync().ConfigureAwait(false);
                Interlocked.CompareExchange(ref _connection, null, state);
                throw Availability("MQTT connection failed.", exception);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisconnectAsync(
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        _ = reason;
        if (Volatile.Read(ref _disposed) != 0)
            return;

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _connection) is not { } state)
                return;

            state.MarkExpectedDisconnect();
            try
            {
                await state.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                state.CancelExpectedDisconnect();
                throw;
            }
            catch (Exception exception)
            {
                await state.DisposeAsync().ConfigureAwait(false);
                await ReportDisconnectedAsync(state).ConfigureAwait(false);
                throw Availability("MQTT disconnect failed.", exception);
            }

            await ReportDisconnectedAsync(state).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask PublishAsync(
        MqttPublishMessage message,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(message);
        ValidateExactContent(message, nameof(message));
        var client = ConnectedClient();
        try
        {
            var reason = await client.PublishAsync(
                PulseMqttMessageMapper.ToPublishPacket(message),
                cancellationToken).ConfigureAwait(false);
            if ((byte)reason >= 0x80)
                throw ProtocolFailure("publish", reason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MqttTransportException)
        {
            throw;
        }
        catch (MqttProtocolException exception)
        {
            throw Protocol("MQTT publish protocol failed.", exception);
        }
        catch (Exception exception)
        {
            throw Availability("MQTT publish failed.", exception);
        }
    }

    public async ValueTask SubscribeAsync(
        string identity,
        MqttSubscriptionDefinition subscription,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentNullException.ThrowIfNull(subscription);
        var filter = new MqttTopicFilter(subscription.TopicFilter)
        {
            MaximumQualityOfService = PulseMqttMessageMapper.ToPulseQualityOfService(subscription.Qos),
            NoLocal = subscription.NoLocal,
            RetainAsPublished = subscription.RetainAsPublished,
            RetainHandling = ToProviderRetainHandling(subscription.RetainHandling)
        };

        try
        {
            var results = await ConnectedClient()
                .SubscribeAsync([filter], cancellationToken)
                .ConfigureAwait(false);
            var failure = results.FirstOrDefault(static result => (byte)result >= 0x80);
            if ((byte)failure >= 0x80)
                throw ProtocolFailure("subscribe", failure);
            _subscriptions[identity] = subscription.TopicFilter;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MqttTransportException)
        {
            throw;
        }
        catch (MqttProtocolException exception)
        {
            throw Protocol("MQTT subscribe protocol failed.", exception);
        }
        catch (Exception exception)
        {
            throw Availability("MQTT subscribe failed.", exception);
        }
    }

    public async ValueTask UnsubscribeAsync(
        string identity,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        if (!_subscriptions.TryGetValue(identity, out var topicFilter))
            return;

        try
        {
            var results = await ConnectedClient()
                .UnsubscribeAsync([topicFilter], cancellationToken)
                .ConfigureAwait(false);
            var failure = results.FirstOrDefault(static result => (byte)result >= 0x80);
            if ((byte)failure >= 0x80)
                throw ProtocolFailure("unsubscribe", failure);
            _subscriptions.TryRemove(identity, out _);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MqttTransportException)
        {
            throw;
        }
        catch (MqttProtocolException exception)
        {
            throw Protocol("MQTT unsubscribe protocol failed.", exception);
        }
        catch (Exception exception)
        {
            throw Availability("MQTT unsubscribe failed.", exception);
        }
    }

    public async ValueTask AcknowledgeAsync(
        MqttTransportDeliveryToken delivery,
        MqttWorkflowOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        if (delivery.IsEmpty || !_pending.TryRemove(delivery.Value, out var context))
            return;

        if (outcome == MqttWorkflowOutcome.Ack || !context.CanReject)
        {
            await context.AcknowledgeAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await context.RejectAsync(
            MqttReasonCode.UnspecifiedError,
            outcome == MqttWorkflowOutcome.Timeout
                ? "Workflow acknowledgement timed out."
                : "Workflow rejected the message.",
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _connection) is { } state)
            {
                state.MarkExpectedDisconnect();
                await state.DisposeAsync().ConfigureAwait(false);
                await ReportDisconnectedAsync(state).ConfigureAwait(false);
                if (state.Monitor is { } monitor)
                    await monitor.ConfigureAwait(false);
            }

            _pending.Clear();
            _subscriptions.Clear();
            _messages.Writer.TryComplete();
            _events.Writer.TryComplete();
            foreach (var certificate in _certificates)
                certificate.Dispose();
            _certificates.Clear();
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
            _lifetime.Dispose();
        }
    }

    private async ValueTask OnMessageReceivedAsync(
        MqttInboundPublishContext context,
        CancellationToken cancellationToken)
    {
        var qos = PulseMqttMessageMapper.FromPulseQos(context.Message.QualityOfService);
        var token = default(MqttTransportDeliveryToken);
        if (qos != MqttQos.AtMostOnce)
        {
            token = new MqttTransportDeliveryToken(Guid.NewGuid().ToString("N"));
            if (!_pending.TryAdd(token.Value, context))
                throw new InvalidOperationException("MQTT delivery identity collision.");
        }

        try
        {
            await _messages.Writer.WriteAsync(new MqttTransportReceivedMessage
            {
                Message = PulseMqttMessageMapper.ToReceivedApplicationMessage(
                    context.Message,
                    _clock.GetUtcNow()),
                Delivery = token
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (!token.IsEmpty && _pending.TryRemove(token.Value, out _))
            {
                if (context.CanReject)
                {
                    await context.RejectAsync(
                        MqttReasonCode.UnspecifiedError,
                        "The MQTT delivery could not be handed off.",
                        CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    await context.AcknowledgeAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            throw;
        }
    }

    private async Task MonitorConnectionAsync(ConnectionState state)
    {
        await state.Client.Completion.ConfigureAwait(false);
        await ReportDisconnectedAsync(state).ConfigureAwait(false);
        await state.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ReportDisconnectedAsync(ConnectionState state)
    {
        state.MarkDisconnected();
        Interlocked.CompareExchange(ref _connection, null, state);
        if (!state.TryMarkEventReported())
            return;

        var serverDisconnect = state.Client.ServerDisconnect;
        await WriteEventAsync(new MqttTransportEvent
        {
            Kind = MqttTransportEventKind.Disconnected,
            Message = serverDisconnect is null
                ? state.IsExpectedDisconnect
                    ? "Disconnected from MQTT broker."
                    : "The MQTT connection ended."
                : string.IsNullOrWhiteSpace(serverDisconnect.ReasonString)
                    ? serverDisconnect.ReasonCode.ToString()
                    : serverDisconnect.ReasonString,
            IsTransient = !state.IsExpectedDisconnect
        }).ConfigureAwait(false);
    }

    private async Task WriteEventAsync(MqttTransportEvent transportEvent)
    {
        try
        {
            await _events.Writer.WriteAsync(transportEvent, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException)
        {
        }
    }

    private MqttConnectPacket BuildConnectPacket()
        => new()
        {
            ClientId = _configuration.ClientId,
            CleanStart = _configuration.CleanStart,
            KeepAliveSeconds = checked((ushort)Math.Ceiling(_configuration.KeepAlive.TotalSeconds)),
            Username = string.IsNullOrWhiteSpace(_configuration.Credentials?.Username)
                ? null
                : _configuration.Credentials.Username,
            Password = _configuration.Credentials?.Password is { } password
                ? PulseMqttMessageMapper.ToUtf8Memory(password)
                : null,
            Will = _configuration.LastWill is { } lastWill
                ? PulseMqttMessageMapper.ToWillMessage(lastWill)
                : null
        };

    private RawMqttClient ConnectedClient()
        => Volatile.Read(ref _connection) is { IsConnected: true } state
            ? state.Client
            : throw Availability(
                "The MQTT client is not connected.",
                new InvalidOperationException("Connect before performing MQTT operations."));

    private static ProviderRetainHandling ToProviderRetainHandling(CoreRetainHandling handling)
        => handling switch
        {
            CoreRetainHandling.SendOnSubscribe => ProviderRetainHandling.SendAtSubscribe,
            CoreRetainHandling.SendOnNewSubscription =>
                ProviderRetainHandling.SendAtSubscribeIfNewSubscription,
            CoreRetainHandling.DoNotSend => ProviderRetainHandling.DoNotSendAtSubscribe,
            _ => throw new ArgumentOutOfRangeException(nameof(handling))
        };

    private static MqttTransportException CreateConnectRejection(
        MqttReasonCode reasonCode,
        string? reasonString)
    {
        var category = reasonCode switch
        {
            MqttReasonCode.BadUserNameOrPassword or
            MqttReasonCode.NotAuthorized or
            MqttReasonCode.Banned or
            MqttReasonCode.BadAuthenticationMethod => "Authentication",
            MqttReasonCode.ClientIdentifierNotValid or
            MqttReasonCode.MalformedPacket or
            MqttReasonCode.ProtocolError => "Protocol",
            _ => "Availability"
        };
        return new MqttTransportException(
            string.IsNullOrWhiteSpace(reasonString)
                ? $"MQTT broker rejected the connection: {reasonCode}."
                : $"MQTT broker rejected the connection: {reasonCode}: {reasonString}",
            category,
            category == "Availability");
    }

    private static MqttTransportException ProtocolFailure(
        string operation,
        MqttReasonCode reasonCode)
        => new($"MQTT {operation} failed: {reasonCode}.", "Protocol", isTransient: false);

    private static MqttTransportException Protocol(string message, Exception exception)
        => new(message, "Protocol", isTransient: false, exception);

    private static MqttTransportException Availability(string message, Exception exception)
        => new(message, "Availability", isTransient: true, exception);

    private static void ValidateExactContent(MqttPublishMessage message, string parameterName)
    {
        if (!message.Content.HasOriginalRepresentation)
        {
            throw new ArgumentException(
                "An MQTT publish message requires FlowContent with an exact byte representation.",
                parameterName);
        }
    }

    private static X509Certificate2Collection LoadCertificates(
        IReadOnlyList<MqttClientCertificate> certificates)
    {
        var loaded = new X509Certificate2Collection();
        foreach (var certificate in certificates)
        {
            var content = certificate.Content.ToArray();
            var isPkcs12 = certificate.Password is not null ||
                certificate.Name.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) ||
                certificate.Name.EndsWith(".p12", StringComparison.OrdinalIgnoreCase);
#if NET9_0_OR_GREATER
            if (isPkcs12)
            {
                loaded.AddRange(X509CertificateLoader.LoadPkcs12Collection(
                    content,
                    certificate.Password,
                    X509KeyStorageFlags.EphemeralKeySet,
                    Pkcs12LoaderLimits.Defaults));
            }
            else
            {
                loaded.Add(X509CertificateLoader.LoadCertificate(content));
            }
#else
            if (isPkcs12)
            {
                loaded.Import(
                    content,
                    certificate.Password,
                    X509KeyStorageFlags.EphemeralKeySet);
            }
            else
            {
                loaded.Add(new X509Certificate2(content));
            }
#endif
        }

        return loaded;
    }

    private static Channel<T> CreateChannel<T>()
        => Channel.CreateBounded<T>(new BoundedChannelOptions(InboundCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed class ConnectionState(RawMqttClient client)
    {
        private readonly SemaphoreSlim _disposeGate = new(1, 1);
        private int _connected;
        private int _expectedDisconnect;
        private int _eventReported;
        private int _disposed;

        internal RawMqttClient Client { get; } = client;

        internal bool IsConnected => Volatile.Read(ref _connected) != 0;

        internal bool IsExpectedDisconnect => Volatile.Read(ref _expectedDisconnect) != 0;

        internal Task? Monitor { get; set; }

        internal void MarkConnected() => Volatile.Write(ref _connected, 1);

        internal void MarkDisconnected() => Volatile.Write(ref _connected, 0);

        internal void MarkExpectedDisconnect() => Volatile.Write(ref _expectedDisconnect, 1);

        internal void CancelExpectedDisconnect() => Volatile.Write(ref _expectedDisconnect, 0);

        internal bool TryMarkEventReported() => Interlocked.Exchange(ref _eventReported, 1) == 0;

        internal async ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            await _disposeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return;
                await Client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _disposed, 1);
            }
            finally
            {
                _disposeGate.Release();
            }
        }

        internal async ValueTask DisposeAsync()
        {
            await _disposeGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    await Client.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _disposeGate.Release();
            }
        }
    }
}
