using System.Globalization;
using System.Threading.Channels;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Validation;
using Pulse.Mqtt;
using Pulse.Mqtt.Client;
using Pulse.Mqtt.Connection;
using Pulse.Mqtt.Packets;
using Pulse.Mqtt.Resilience;
using Pulse.Mqtt.Routing;
using Pulse.Mqtt.Transport;

namespace FluxFlow.Components.Mqtt.PulseMqtt;

public sealed class PulseMqttClient :
    IMqttPublisher,
    IMqttTriggerSource,
    IMqttClientHealthSource,
    IAsyncDisposable
{
    private static readonly TimeSpan ConnectedPollInterval = TimeSpan.FromMilliseconds(50);

    private readonly PulseMqttClientOptions _options;
    private readonly TimeProvider _clock;
    private readonly ResilientMqttClient _client;
    private readonly Channel<MqttClientHealthEvent> _health =
        Channel.CreateUnbounded<MqttClientHealthEvent>();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _healthPumpGate = new();
    private readonly string? _configuredClientId;
    private Task? _healthPump;
    private bool _started;
    private int _disposed;

    public PulseMqttClient(
        PulseMqttClientOptions options,
        TimeProvider? clock = null,
        IMqttTransportFactory? transportFactory = null)
    {
        _options = ValidateOptions(options, transportFactory);
        _clock = clock ?? TimeProvider.System;
        _configuredClientId = string.IsNullOrWhiteSpace(_options.ClientId)
            ? null
            : _options.ClientId;

        _client = new ResilientMqttClient(
            transportFactory ?? BuildTransportFactory(_options),
            BuildClientOptions(_options),
            _clock);
    }

    public bool IsConnected => _client.State == ConnectionState.Connected;

    public ConnectionState State => _client.State;

    public IAsyncEnumerable<MqttClientHealthEvent> Health
        => _health.Reader.ReadAllAsync();

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureHealthPump();

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);

        await _lifecycleGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        try
        {
            if (_started)
            {
                return;
            }

            WriteHealth(
                MqttClientHealthState.Connecting,
                "Starting Pulse MQTT client.",
                null);

            try
            {
                await _client.ConnectAsync(linkedCancellation.Token).ConfigureAwait(false);
                _started = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                WriteHealth(
                    MqttClientHealthState.Faulted,
                    $"Pulse MQTT client start failed: {exception.Message}",
                    exception.GetType().Name);
                throw new MqttClientUnavailableException(
                    $"Pulse MQTT client start failed: {exception.Message}",
                    exception);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        await WaitForConnectedAsync(_options.ConnectTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started)
            {
                WriteHealth(
                    MqttClientHealthState.Disconnected,
                    "Pulse MQTT client is stopped.",
                    null);
                return;
            }

            await _client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            _started = false;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
        => StopAsync(cancellationToken);

    public async ValueTask WaitForConnectedAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "MQTT connect timeout must be greater than zero.");
        }

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token,
            _lifetime.Token);

        try
        {
            while (true)
            {
                switch (_client.State)
                {
                    case ConnectionState.Connected:
                        return;
                    case ConnectionState.Faulted:
                        throw new MqttClientUnavailableException(
                            "Pulse MQTT client entered a faulted state before connecting.");
                }

                await Task.Delay(
                    ConnectedPollInterval,
                    _clock,
                    linkedCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            throw new MqttClientUnavailableException(
                $"Pulse MQTT client did not connect within {timeout}.");
        }
    }

    public async ValueTask PublishAsync(
        MqttPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        ValidatePublishRequest(request);

        if (!_options.AllowOfflinePublishQueue && !IsConnected)
        {
            throw new MqttClientUnavailableException("Pulse MQTT client is not connected.");
        }

        var outcome = await _client
            .PublishAsync(PulseMqttMessageMapper.ToPublishPacket(request), cancellationToken)
            .ConfigureAwait(false);

        if (outcome.Disposition == PublishDisposition.DroppedOffline)
        {
            throw new MqttClientUnavailableException(
                "Pulse MQTT publish was dropped because the client is offline.");
        }

        if (!_options.AllowOfflinePublishQueue &&
            outcome.Disposition != PublishDisposition.Delivered)
        {
            throw new MqttClientUnavailableException(
                $"Pulse MQTT publish was not delivered because the client is {outcome.Disposition}.");
        }

        if ((byte)outcome.ReasonCode >= 128)
        {
            throw new InvalidOperationException(
                $"Pulse MQTT publish failed: {outcome.ReasonCode}.");
        }
    }

    public async ValueTask<IMqttSubscription> SubscribeAsync(
        MqttTriggerOptions options,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);
        ValidateTriggerOptions(options);

        if (!IsConnected)
        {
            throw new MqttClientUnavailableException("Pulse MQTT client is not connected.");
        }

        var routeTemplate = MqttRouteTemplate.Parse(options.TopicFilter!);
        var routeOptions = new MqttRouteOptions
        {
            Capacity = options.BoundedCapacity,
            Overflow = RouteOverflow.Wait
        };
        var subscription = options.Acknowledgement == MqttTriggerAcknowledgement.None
            ? new PulseMqttSubscription(
                this,
                _client.OpenRouteStream(routeTemplate, routeOptions),
                routeTemplate.TopicFilter,
                _clock)
            : new PulseMqttSubscription(
                this,
                _client.OpenAcknowledgedRouteStream(routeTemplate, routeOptions),
                routeTemplate.TopicFilter,
                _clock);

        try
        {
            var result = await _client.SubscribeAsync(
                [BuildTopicFilter(routeTemplate.TopicFilter, options)],
                cancellationToken).ConfigureAwait(false);
            var failedReason = result.FirstOrDefault(reason => (byte)reason >= 128);
            if ((byte)failedReason >= 128)
            {
                throw new InvalidOperationException($"Pulse MQTT subscribe failed: {failedReason}.");
            }

            return subscription;
        }
        catch
        {
            await subscription.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);

        try
        {
            await _client.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Disposal must continue to release the underlying client and complete health readers.
        }

        await _client.DisposeAsync().ConfigureAwait(false);

        try
        {
            if (_healthPump is { } healthPump)
            {
                await healthPump.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal.
        }
        finally
        {
            _lifetime.Dispose();
            _lifecycleGate.Dispose();
            _health.Writer.TryComplete();
        }
    }

    internal async ValueTask UnsubscribeAsync(
        PulseMqttSubscription subscription,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            await _client.UnsubscribeAsync([subscription.TopicFilter], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private async Task PumpHealthAsync()
    {
        try
        {
            await foreach (var change in _client.WatchState(_lifetime.Token)
                .ConfigureAwait(false))
            {
                WriteHealth(
                    MapHealthState(change.Current),
                    BuildHealthMessage(change.Current),
                    change.Reason?.ToString(),
                    CreateStateAttributes(change));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ChannelClosedException)
        {
        }
        catch (Exception exception)
        {
            WriteHealth(
                MqttClientHealthState.Faulted,
                $"Pulse MQTT health stream failed: {exception.Message}",
                exception.GetType().Name);
        }
    }

    private void EnsureHealthPump()
    {
        lock (_healthPumpGate)
        {
            _healthPump ??= Task.Run(PumpHealthAsync);
        }
    }

    private static IMqttTransportFactory BuildTransportFactory(
        PulseMqttClientOptions options)
        => new TcpTransportFactory(new TcpTransportOptions
        {
            Host = options.Host!,
            Port = options.Port,
            UseTls = options.UseTls,
            TlsTargetHost = options.TlsTargetHost,
            ClientCertificates = options.ClientCertificates,
            ServerCertificateValidation = options.ServerCertificateValidation
        });

    private static ResilientMqttClientOptions BuildClientOptions(
        PulseMqttClientOptions options)
    {
        var will = options.LastWill is null
            ? null
            : PulseMqttMessageMapper.ToWillMessage(options.LastWill);

        return new ResilientMqttClientOptions
        {
            Connect = new MqttConnectPacket
            {
                ClientId = options.ClientId ?? string.Empty,
                CleanStart = options.CleanStart,
                KeepAliveSeconds = ToKeepAliveSeconds(options.KeepAlivePeriod),
                Username = string.IsNullOrWhiteSpace(options.Username)
                    ? null
                    : options.Username,
                Password = string.IsNullOrWhiteSpace(options.Password)
                    ? null
                    : PulseMqttMessageMapper.ToUtf8Memory(options.Password),
                UserProperties = PulseMqttMessageMapper.ToUserProperties(options.UserProperties),
                Will = will
            },
            Raw = new RawMqttClientOptions
            {
                ConnAckTimeout = options.ConnectTimeout
            },
            OfflineQueue = new OfflineQueueOptions
            {
                IncludeQos0 = options.QueueQos0WhenDisconnected
            },
            MessageStore = options.AllowOfflinePublishQueue
                ? options.MessageStore
                : new RejectingMessageStore(),
            SessionStore = options.SessionStore,
            PropagateTraceContext = options.PropagateTraceContext,
            Will = will
        };
    }

    private static MqttTopicFilter BuildTopicFilter(
        string topicFilter,
        MqttTriggerOptions options)
        => new(topicFilter)
        {
            MaximumQualityOfService =
                PulseMqttMessageMapper.ToPulseQualityOfService(options.QualityOfService),
            RetainAsPublished = options.RetainAsPublished,
            RetainHandling = options.ReceiveRetainedMessages
                ? MqttRetainHandling.SendAtSubscribe
                : MqttRetainHandling.DoNotSendAtSubscribe
        };

    private void WriteHealth(
        MqttClientHealthState state,
        string message,
        string? reason,
        IReadOnlyDictionary<string, string>? attributes = null)
        => _health.Writer.TryWrite(new MqttClientHealthEvent
        {
            Timestamp = _clock.GetUtcNow(),
            State = state,
            Message = message,
            Reason = reason,
            ConnectionName = _options.ConnectionName,
            ClientId = _configuredClientId,
            Attributes = attributes ?? CreateBaseAttributes()
        });

    private Dictionary<string, string> CreateBaseAttributes()
        => new(StringComparer.Ordinal)
        {
            ["host"] = string.IsNullOrWhiteSpace(_options.Host)
                ? "custom"
                : _options.Host!,
            ["port"] = _options.Port.ToString(CultureInfo.InvariantCulture),
            ["useTls"] = _options.UseTls.ToString(CultureInfo.InvariantCulture)
        };

    private IReadOnlyDictionary<string, string> CreateStateAttributes(
        ConnectionStateChanged change)
    {
        var values = CreateBaseAttributes();
        values["previousState"] = change.Previous.ToString();
        values["currentState"] = change.Current.ToString();
        values["attempt"] = change.Attempt.ToString(CultureInfo.InvariantCulture);
        return values;
    }

    private static MqttClientHealthState MapHealthState(ConnectionState state)
        => state switch
        {
            ConnectionState.Connecting => MqttClientHealthState.Connecting,
            ConnectionState.Connected => MqttClientHealthState.Connected,
            ConnectionState.Reconnecting or ConnectionState.WaitingRetry =>
                MqttClientHealthState.Reconnecting,
            ConnectionState.Faulted => MqttClientHealthState.Faulted,
            ConnectionState.Disconnected or ConnectionState.Stopped =>
                MqttClientHealthState.Disconnected,
            _ => MqttClientHealthState.Unknown
        };

    private static string BuildHealthMessage(ConnectionState state)
        => state switch
        {
            ConnectionState.Connecting => "Connecting to MQTT broker.",
            ConnectionState.Connected => "Connected to MQTT broker.",
            ConnectionState.Reconnecting => "MQTT client disconnected; reconnect is in progress.",
            ConnectionState.WaitingRetry => "MQTT client is waiting before reconnect.",
            ConnectionState.Faulted => "MQTT client entered a faulted state.",
            ConnectionState.Stopped => "Pulse MQTT client is stopped.",
            ConnectionState.Disconnected => "Pulse MQTT client is disconnected.",
            _ => $"Pulse MQTT client state changed to {state}."
        };

    private static ushort ToKeepAliveSeconds(TimeSpan? keepAlivePeriod)
    {
        if (!keepAlivePeriod.HasValue)
        {
            return 60;
        }

        var seconds = checked((int)Math.Ceiling(keepAlivePeriod.Value.TotalSeconds));
        return checked((ushort)seconds);
    }

    private static PulseMqttClientOptions ValidateOptions(
        PulseMqttClientOptions? options,
        IMqttTransportFactory? transportFactory)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (transportFactory is null && string.IsNullOrWhiteSpace(options.Host))
        {
            throw new ArgumentException(
                "MQTT host is required when no Pulse transport factory is supplied.",
                nameof(options));
        }

        if (options.Port is <= 0 or > 65_535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Port,
                "MQTT port must be between 1 and 65535.");
        }

        if (options.ConnectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.ConnectTimeout,
                "MQTT connect timeout must be greater than zero.");
        }

        if (options.KeepAlivePeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.KeepAlivePeriod,
                "MQTT keep-alive period must be greater than zero when set.");
        }

        if (options.KeepAlivePeriod?.TotalSeconds > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.KeepAlivePeriod,
                "MQTT keep-alive period is too large.");
        }

        if (!options.AllowOfflinePublishQueue && options.MessageStore is not null)
        {
            throw new ArgumentException(
                "An MQTT offline publish message store requires AllowOfflinePublishQueue.",
                nameof(options));
        }

        if (options.LastWill is not null)
        {
            ValidateLastWill(options.LastWill);
        }

        return options;
    }

    private static void ValidateLastWill(PulseMqttLastWillOptions lastWill)
    {
        var topicValidation =
            MqttTopicValidator.ValidatePublishTopic(lastWill.Topic);
        if (!topicValidation.IsValid)
        {
            throw new ArgumentException(
                topicValidation.Message ?? "MQTT Last Will topic is invalid.",
                nameof(lastWill));
        }

        if (lastWill.Payload is null)
        {
            throw new ArgumentException("MQTT Last Will payload is required.", nameof(lastWill));
        }

        if (!Enum.IsDefined(lastWill.QualityOfService))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastWill),
                lastWill.QualityOfService,
                "MQTT Last Will quality-of-service value is not supported.");
        }
    }

    private static void ValidatePublishRequest(MqttPublishRequest request)
    {
        var topicValidation = MqttTopicValidator.ValidatePublishTopic(request.Topic);
        if (!topicValidation.IsValid)
        {
            throw new ArgumentException(
                topicValidation.Message ?? "MQTT publish topic is invalid.",
                nameof(request));
        }

        if (request.Payload is null)
        {
            throw new ArgumentException("MQTT publish payload is required.", nameof(request));
        }

        if (!Enum.IsDefined(request.QualityOfService))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.QualityOfService,
                "MQTT publish quality-of-service value is not supported.");
        }
    }

    private static void ValidateTriggerOptions(MqttTriggerOptions options)
    {
        var topicValidation = MqttTopicValidator.ValidateSubscriptionFilter(
            options.TopicFilter);
        if (!topicValidation.IsValid)
        {
            throw new ArgumentException(
                topicValidation.Message ?? "MQTT subscription topic filter is invalid.",
                nameof(options));
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.BoundedCapacity,
                "MQTT subscription bounded capacity must be greater than zero.");
        }

        if (!Enum.IsDefined(options.QualityOfService))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.QualityOfService,
                "MQTT subscription quality-of-service value is not supported.");
        }

        if (!Enum.IsDefined(options.Acknowledgement))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Acknowledgement,
                "MQTT subscription acknowledgement mode is not supported.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
