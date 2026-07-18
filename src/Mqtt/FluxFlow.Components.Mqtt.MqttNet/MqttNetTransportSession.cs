using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using MQTTnet;
using MQTTnet.Protocol;
using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using CoreRetainHandling = FluxFlow.Components.Mqtt.Subscriptions.MqttRetainHandling;
using ProviderRetainHandling = MQTTnet.Protocol.MqttRetainHandling;

namespace FluxFlow.Components.Mqtt.MqttNet;

internal sealed class MqttNetTransportSession : IMqttTransportSession
{
    private const int InboundCapacity = 256;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    private readonly MqttClientConfiguration _configuration;
    private readonly MqttClientFactory _builders;
    private readonly IMqttClient _client;
    private readonly TimeProvider _clock;
    private readonly Channel<MqttTransportReceivedMessage> _messages = CreateChannel<MqttTransportReceivedMessage>();
    private readonly Channel<MqttTransportEvent> _events = CreateChannel<MqttTransportEvent>();
    private readonly ConcurrentDictionary<string, string> _subscriptions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MqttApplicationMessageReceivedEventArgs> _pending =
        new(StringComparer.Ordinal);
    private readonly X509Certificate2Collection _certificates;
    private readonly CancellationTokenSource _lifetime = new();
    private int _disconnecting;
    private int _disposed;

    public MqttNetTransportSession(
        MqttClientConfiguration configuration,
        MqttClientFactory builders,
        IMqttClient client,
        TimeProvider clock)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _builders = builders ?? throw new ArgumentNullException(nameof(builders));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (configuration.LastWill is { } lastWill)
            ValidateExactContent(lastWill, nameof(configuration));
        _certificates = LoadCertificates(configuration.Certificates);

        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.ConnectedAsync += OnConnectedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
    }

    public MqttTransportCapabilities Capabilities { get; } = new()
    {
        DeferredAcknowledgement = true,
        NegativeAcknowledgement = true
    };

    public bool IsConnected => _client.IsConnected;

    public IAsyncEnumerable<MqttTransportReceivedMessage> Messages =>
        _messages.Reader.ReadAllAsync();

    public IAsyncEnumerable<MqttTransportEvent> Events =>
        _events.Reader.ReadAllAsync();

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (_client.IsConnected)
            return;

        Volatile.Write(ref _disconnecting, 0);
        try
        {
            var result = await _client
                .ConnectAsync(BuildClientOptions(), cancellationToken)
                .ConfigureAwait(false);
            if (result.ResultCode != MqttClientConnectResultCode.Success)
            {
                throw CreateConnectRejection(result);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MqttTransportException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Availability("MQTT connection failed.", exception);
        }
    }

    public async ValueTask DisconnectAsync(
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0 || !_client.IsConnected)
            return;

        Volatile.Write(ref _disconnecting, 1);
        try
        {
            var options = _builders
                .CreateClientDisconnectOptionsBuilder()
                .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                .WithReasonString(TrimReason(reason) ?? "Client stopped.")
                .Build();
            await _client.DisconnectAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Volatile.Write(ref _disconnecting, 0);
            throw;
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _disconnecting, 0);
            throw Availability("MQTT disconnect failed.", exception);
        }
    }

    public async ValueTask PublishAsync(
        MqttPublishMessage message,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(message);
        ValidateExactContent(message, nameof(message));
        try
        {
            var result = await _client
                .PublishAsync(MqttNetMessageMapper.ToApplicationMessage(message), cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess &&
                result.ReasonCode is not MqttClientPublishReasonCode.Success and
                    not MqttClientPublishReasonCode.NoMatchingSubscribers)
            {
                throw ProtocolFailure("publish", result.ReasonCode, result.ReasonString);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MqttTransportException)
        {
            throw;
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

        var options = _builders
            .CreateSubscribeOptionsBuilder()
            .WithTopicFilter(
                subscription.TopicFilter,
                MqttNetMessageMapper.ToMqttNetQualityOfService(subscription.Qos),
                subscription.NoLocal,
                subscription.RetainAsPublished,
                ToProviderRetainHandling(subscription.RetainHandling))
            .Build();

        try
        {
            var result = await _client.SubscribeAsync(options, cancellationToken)
                .ConfigureAwait(false);
            var failed = result.Items.FirstOrDefault(static item => (int)item.ResultCode >= 128);
            if (failed is not null)
                throw ProtocolFailure("subscribe", failed.ResultCode, result.ReasonString);

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

        var options = _builders
            .CreateUnsubscribeOptionsBuilder()
            .WithTopicFilter(topicFilter)
            .Build();
        try
        {
            var result = await _client.UnsubscribeAsync(options, cancellationToken)
                .ConfigureAwait(false);
            var failed = result.Items.FirstOrDefault(static item => (int)item.ResultCode >= 128);
            if (failed is not null)
                throw ProtocolFailure("unsubscribe", failed.ResultCode, result.ReasonString);

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
        if (delivery.IsEmpty || !_pending.TryRemove(delivery.Value, out var received))
            return;

        if (outcome != MqttWorkflowOutcome.Ack)
        {
            received.ProcessingFailed = true;
            received.ReasonCode = MqttApplicationMessageReceivedReasonCode.ImplementationSpecificError;
            received.ResponseReasonString = outcome == MqttWorkflowOutcome.Timeout
                ? "Workflow acknowledgement timed out."
                : "Workflow rejected the message.";
        }

        await received.AcknowledgeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _lifetime.CancelAsync().ConfigureAwait(false);
        _client.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;
        _client.ConnectedAsync -= OnConnectedAsync;
        _client.DisconnectedAsync -= OnDisconnectedAsync;

        try
        {
            if (_client.IsConnected)
            {
                var options = _builders
                    .CreateClientDisconnectOptionsBuilder()
                    .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                    .WithReasonString("Client disposed.")
                    .Build();
                await _client.DisconnectAsync(options, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // Provider disposal and all owned certificates must still be released.
        }
        finally
        {
            _pending.Clear();
            _subscriptions.Clear();
            _messages.Writer.TryComplete();
            _events.Writer.TryComplete();
            _client.Dispose();
            foreach (var certificate in _certificates)
                certificate.Dispose();
            _certificates.Clear();
            _lifetime.Dispose();
        }
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs received)
    {
        var qos = MqttNetMessageMapper.FromMqttNetQos(received.ApplicationMessage.QualityOfServiceLevel);
        var token = default(MqttTransportDeliveryToken);
        if (qos != MqttQos.AtMostOnce)
        {
            received.AutoAcknowledge = false;
            token = new MqttTransportDeliveryToken(Guid.NewGuid().ToString("N"));
            if (!_pending.TryAdd(token.Value, received))
                throw new InvalidOperationException("MQTT delivery identity collision.");
        }

        try
        {
            await _messages.Writer.WriteAsync(new MqttTransportReceivedMessage
            {
                Message = MqttNetMessageMapper.ToReceivedApplicationMessage(
                    received.ApplicationMessage,
                    _clock.GetUtcNow()),
                Delivery = token
            }, _lifetime.Token).ConfigureAwait(false);
        }
        catch
        {
            if (!token.IsEmpty && _pending.TryRemove(token.Value, out _))
            {
                received.ProcessingFailed = true;
                received.ReasonCode =
                    MqttApplicationMessageReceivedReasonCode.ImplementationSpecificError;
                received.ResponseReasonString = "The MQTT delivery could not be handed off.";
                await received.AcknowledgeAsync(CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
    }

    private Task OnConnectedAsync(MqttClientConnectedEventArgs _)
        => WriteEventAsync(new MqttTransportEvent
        {
            Kind = MqttTransportEventKind.Connected,
            Message = "Connected to MQTT broker."
        });

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs disconnected)
        => WriteEventAsync(new MqttTransportEvent
        {
            Kind = MqttTransportEventKind.Disconnected,
            Message = string.IsNullOrWhiteSpace(disconnected.ReasonString)
                ? disconnected.Reason.ToString()
                : disconnected.ReasonString,
            IsTransient = Volatile.Read(ref _disconnecting) == 0
        });

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

    private MqttClientOptions BuildClientOptions()
    {
        var builder = _builders
            .CreateClientOptionsBuilder()
            .WithTcpServer(_configuration.Broker.Host, _configuration.Broker.Port)
            .WithClientId(_configuration.ClientId)
            .WithCleanStart(_configuration.CleanStart)
            .WithKeepAlivePeriod(_configuration.KeepAlive)
            .WithTimeout(ConnectTimeout);

        if (!string.IsNullOrWhiteSpace(_configuration.Credentials?.Username))
        {
            builder.WithCredentials(
                _configuration.Credentials.Username,
                _configuration.Credentials.Password ?? string.Empty);
        }

        if (_configuration.Broker.UseTls)
        {
            builder.WithTlsOptions(tls =>
            {
                tls.UseTls();
                tls.WithTargetHost(_configuration.Broker.ServerName ?? _configuration.Broker.Host);
                if (_certificates.Count > 0)
                    tls.WithClientCertificates(_certificates);
            });
        }

        ApplyLastWill(builder, _configuration.LastWill);
        return builder.Build();
    }

    private static void ApplyLastWill(
        MqttClientOptionsBuilder builder,
        MqttPublishMessage? lastWill)
    {
        if (lastWill is null)
            return;

        builder
            .WithWillTopic(lastWill.Topic)
            .WithWillPayload(lastWill.Content.OriginalBytes.AsSpan().ToArray())
            .WithWillQualityOfServiceLevel(MqttNetMessageMapper.ToMqttNetQualityOfService(lastWill.Qos))
            .WithWillRetain(lastWill.Retain);
        if (!string.IsNullOrWhiteSpace(lastWill.Content.ContentType))
            builder.WithWillContentType(lastWill.Content.ContentType);
        if (!string.IsNullOrWhiteSpace(lastWill.CorrelationData))
            builder.WithWillCorrelationData(System.Text.Encoding.UTF8.GetBytes(lastWill.CorrelationData));
        if (!string.IsNullOrWhiteSpace(lastWill.ResponseTopic))
            builder.WithWillResponseTopic(lastWill.ResponseTopic);
        foreach (var property in lastWill.UserProperties)
            builder.WithWillUserProperty(
                property.Key,
                MqttNetMessageMapper.ToUtf8Memory(property.Value));
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

    private static ProviderRetainHandling ToProviderRetainHandling(CoreRetainHandling handling)
        => handling switch
        {
            CoreRetainHandling.SendOnSubscribe => ProviderRetainHandling.SendAtSubscribe,
            CoreRetainHandling.SendOnNewSubscription =>
                ProviderRetainHandling.SendAtSubscribeIfNewSubscriptionOnly,
            CoreRetainHandling.DoNotSend => ProviderRetainHandling.DoNotSendOnSubscribe,
            _ => throw new ArgumentOutOfRangeException(nameof(handling))
        };

    private static MqttTransportException CreateConnectRejection(MqttClientConnectResult result)
    {
        var reason = string.IsNullOrWhiteSpace(result.ReasonString)
            ? result.ResultCode.ToString()
            : $"{result.ResultCode}: {result.ReasonString}";
        var category = result.ResultCode switch
        {
            MqttClientConnectResultCode.BadUserNameOrPassword or
            MqttClientConnectResultCode.NotAuthorized or
            MqttClientConnectResultCode.Banned => "Authentication",
            MqttClientConnectResultCode.ClientIdentifierNotValid or
            MqttClientConnectResultCode.UnsupportedProtocolVersion => "Protocol",
            _ => "Availability"
        };
        return new MqttTransportException(
            $"MQTT broker rejected the connection: {reason}",
            category,
            category == "Availability");
    }

    private static MqttTransportException ProtocolFailure(
        string operation,
        object reasonCode,
        string? reason)
        => new(
            string.IsNullOrWhiteSpace(reason)
                ? $"MQTT {operation} failed: {reasonCode}."
                : $"MQTT {operation} failed: {reasonCode}: {reason}",
            "Protocol",
            isTransient: false);

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

    private static string? TrimReason(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= 128 ? value : value[..128];

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
}
