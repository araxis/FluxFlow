using System.Collections.Concurrent;
using System.Threading.Channels;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace FluxFlow.Components.Mqtt.MqttNet;

public sealed class MqttNetClient :
    IMqttPublisher,
    IMqttTriggerSource,
    IMqttClientHealthSource,
    IAsyncDisposable
{
    private readonly MqttNetClientOptions _options;
    private readonly TimeProvider _clock;
    private readonly MqttClientFactory _factory;
    private readonly IMqttClient _client;
    private readonly Channel<MqttClientHealthEvent> _health =
        Channel.CreateUnbounded<MqttClientHealthEvent>();
    private readonly ConcurrentDictionary<Guid, MqttNetSubscription> _subscriptions = [];
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _reconnectGate = new();
    private Task? _reconnectTask;
    private int _disposed;
    private int _disconnectRequested;

    public MqttNetClient(
        MqttNetClientOptions options,
        TimeProvider? clock = null,
        MqttClientFactory? factory = null)
    {
        _options = ValidateOptions(options);
        _clock = clock ?? TimeProvider.System;
        _factory = factory ?? new MqttClientFactory();
        _client = _factory.CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
        _client.ConnectedAsync += OnConnectedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
    }

    public bool IsConnected => _client.IsConnected;

    public IAsyncEnumerable<MqttClientHealthEvent> Health
        => _health.Reader.ReadAllAsync();

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);

        await _connectionGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        try
        {
            if (_client.IsConnected)
            {
                return;
            }

            Volatile.Write(ref _disconnectRequested, 0);
            WriteHealth(
                MqttClientHealthState.Connecting,
                "Connecting to MQTT broker.",
                null);

            MqttClientConnectResult result;
            try
            {
                result = await _client
                    .ConnectAsync(BuildClientOptions(), linkedCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                WriteHealth(
                    MqttClientHealthState.Faulted,
                    $"MQTT broker connection failed: {exception.Message}",
                    exception.GetType().Name);
                throw new MqttClientUnavailableException(
                    $"MQTT broker connection failed: {exception.Message}",
                    exception);
            }

            if (result.ResultCode != MqttClientConnectResultCode.Success)
            {
                var reason = string.IsNullOrWhiteSpace(result.ReasonString)
                    ? result.ResultCode.ToString()
                    : $"{result.ResultCode}: {result.ReasonString}";
                WriteHealth(
                    MqttClientHealthState.Faulted,
                    $"MQTT broker rejected the connection: {reason}",
                    result.ResultCode.ToString());
                throw new MqttClientUnavailableException(
                    $"MQTT broker rejected the connection: {reason}");
            }
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        Volatile.Write(ref _disconnectRequested, 1);

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_client.IsConnected)
            {
                WriteHealth(
                    MqttClientHealthState.Disconnected,
                    "MQTT client is disconnected.",
                    null);
                return;
            }

            var disconnectOptions = _factory
                .CreateClientDisconnectOptionsBuilder()
                .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                .WithReasonString("Client stopped.")
                .Build();

            await _client.DisconnectAsync(disconnectOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async ValueTask PublishAsync(
        MqttPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);

        if (!_client.IsConnected)
        {
            throw new MqttClientUnavailableException("MQTT client is not connected.");
        }

        ValidatePublishRequest(request);
        var result = await _client
            .PublishAsync(MqttNetMessageMapper.ToApplicationMessage(request), cancellationToken)
            .ConfigureAwait(false);

        if (!IsSuccessfulPublish(result))
        {
            var reason = string.IsNullOrWhiteSpace(result.ReasonString)
                ? result.ReasonCode.ToString()
                : $"{result.ReasonCode}: {result.ReasonString}";
            throw new InvalidOperationException($"MQTT publish failed: {reason}");
        }
    }

    public async ValueTask<IMqttSubscription> SubscribeAsync(
        MqttTriggerOptions options,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);

        if (!_client.IsConnected)
        {
            throw new MqttClientUnavailableException("MQTT client is not connected.");
        }

        ValidateTriggerOptions(options);
        var subscription = new MqttNetSubscription(this, Guid.NewGuid(), options);
        if (!_subscriptions.TryAdd(subscription.Id, subscription))
        {
            throw new InvalidOperationException("MQTT subscription could not be registered.");
        }

        try
        {
            await SubscribeOnBrokerAsync(subscription, cancellationToken)
                .ConfigureAwait(false);
            return subscription;
        }
        catch
        {
            _subscriptions.TryRemove(subscription.Id, out _);
            subscription.Complete();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _disconnectRequested, 1);
        _lifetime.Cancel();

        foreach (var subscription in _subscriptions.Values)
        {
            subscription.Complete();
        }

        _subscriptions.Clear();

        _client.ApplicationMessageReceivedAsync -= OnApplicationMessageReceivedAsync;
        _client.ConnectedAsync -= OnConnectedAsync;
        _client.DisconnectedAsync -= OnDisconnectedAsync;

        try
        {
            if (_client.IsConnected)
            {
                var disconnectOptions = _factory
                    .CreateClientDisconnectOptionsBuilder()
                    .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                    .WithReasonString("Client disposed.")
                    .Build();
                await _client.DisconnectAsync(disconnectOptions, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _client.Dispose();
            _lifetime.Dispose();
            _connectionGate.Dispose();
            _health.Writer.TryComplete();
        }
    }

    internal async ValueTask UnsubscribeAsync(
        MqttNetSubscription subscription,
        CancellationToken cancellationToken)
    {
        if (!_subscriptions.TryRemove(subscription.Id, out _))
        {
            subscription.Complete();
            return;
        }

        try
        {
            if (_client.IsConnected && Volatile.Read(ref _disposed) == 0)
            {
                var options = _factory
                    .CreateUnsubscribeOptionsBuilder()
                    .WithTopicFilter(subscription.TopicFilter)
                    .Build();

                await _client.UnsubscribeAsync(options, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            subscription.Complete();
        }
    }

    private async Task OnApplicationMessageReceivedAsync(
        MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;
        var targets = _subscriptions.Values
            .Where(subscription => MqttNetTopicMatcher.IsMatch(subscription.TopicFilter, topic))
            .ToArray();

        if (targets.Length == 0)
        {
            return;
        }

        var manualAcknowledgement = targets.Any(subscription => subscription.ManualAcknowledgement);
        if (manualAcknowledgement)
        {
            args.AutoAcknowledge = false;
        }

        var acknowledgement = new MqttNetReceivedAcknowledgement(
            args,
            manualAcknowledgement);
        var message = MqttNetMessageMapper.ToReceivedMessage(
            args.ApplicationMessage,
            _clock.GetUtcNow());
        var delivered = false;

        foreach (var subscription in targets)
        {
            var context = new MqttNetReceivedContext(message, acknowledgement);
            delivered |= await subscription
                .WriteAsync(context, _lifetime.Token)
                .ConfigureAwait(false);
        }

        if (!delivered && manualAcknowledgement)
        {
            await acknowledgement.NackAsync(
                new InvalidOperationException(
                    "MQTT message could not be delivered to any active subscription."),
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        WriteHealth(
            MqttClientHealthState.Connected,
            "Connected to MQTT broker.",
            args.ConnectResult.ResultCode.ToString(),
            CreateConnectAttributes(args.ConnectResult));

        foreach (var subscription in _subscriptions.Values)
        {
            try
            {
                await SubscribeOnBrokerAsync(subscription, _lifetime.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                subscription.Complete(exception);
                _subscriptions.TryRemove(subscription.Id, out _);
                WriteHealth(
                    MqttClientHealthState.Faulted,
                    $"MQTT resubscribe failed for '{subscription.TopicFilter}': {exception.Message}",
                    exception.GetType().Name);
            }
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return Task.CompletedTask;
        }

        var reason = string.IsNullOrWhiteSpace(args.ReasonString)
            ? args.Reason.ToString()
            : $"{args.Reason}: {args.ReasonString}";

        if (_options.AutomaticReconnect &&
            Volatile.Read(ref _disconnectRequested) == 0 &&
            !_lifetime.IsCancellationRequested)
        {
            WriteHealth(
                MqttClientHealthState.Reconnecting,
                "MQTT client disconnected; reconnect is scheduled.",
                reason);
            StartReconnectLoop();
            return Task.CompletedTask;
        }

        WriteHealth(
            MqttClientHealthState.Disconnected,
            "Disconnected from MQTT broker.",
            reason);
        return Task.CompletedTask;
    }

    private void StartReconnectLoop()
    {
        lock (_reconnectGate)
        {
            if (_reconnectTask is { IsCompleted: false })
            {
                return;
            }

            _reconnectTask = Task.Run(ReconnectLoopAsync);
        }
    }

    private async Task ReconnectLoopAsync()
    {
        while (!_lifetime.IsCancellationRequested &&
               Volatile.Read(ref _disconnectRequested) == 0 &&
               !_client.IsConnected)
        {
            try
            {
                await Task.Delay(_options.ReconnectDelay, _clock, _lifetime.Token)
                    .ConfigureAwait(false);
                await ConnectAsync(_lifetime.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                WriteHealth(
                    MqttClientHealthState.Reconnecting,
                    $"MQTT reconnect failed: {exception.Message}",
                    exception.GetType().Name);
            }
        }
    }

    private async ValueTask SubscribeOnBrokerAsync(
        MqttNetSubscription subscription,
        CancellationToken cancellationToken)
    {
        var subscribeOptions = _factory
            .CreateSubscribeOptionsBuilder()
            .WithTopicFilter(
                subscription.TopicFilter,
                MqttNetMessageMapper.ToMqttNetQualityOfService(subscription.Options.QualityOfService),
                noLocal: false,
                retainAsPublished: subscription.Options.RetainAsPublished,
                retainHandling: subscription.Options.ReceiveRetainedMessages
                    ? MqttRetainHandling.SendAtSubscribe
                    : MqttRetainHandling.DoNotSendOnSubscribe)
            .Build();

        var result = await _client.SubscribeAsync(subscribeOptions, cancellationToken)
            .ConfigureAwait(false);

        var failedItem = result.Items.FirstOrDefault(item => (int)item.ResultCode >= 128);
        if (failedItem is not null)
        {
            var reason = string.IsNullOrWhiteSpace(result.ReasonString)
                ? failedItem.ResultCode.ToString()
                : $"{failedItem.ResultCode}: {result.ReasonString}";
            throw new InvalidOperationException($"MQTT subscribe failed: {reason}");
        }
    }

    private MqttClientOptions BuildClientOptions()
    {
        var builder = _factory
            .CreateClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithCleanSession(_options.CleanSession)
            .WithTimeout(_options.ConnectTimeout);

        if (!string.IsNullOrWhiteSpace(_options.ClientId))
        {
            builder.WithClientId(_options.ClientId);
        }

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            builder.WithCredentials(_options.Username, _options.Password ?? string.Empty);
        }

        if (_options.KeepAlivePeriod.HasValue)
        {
            builder.WithKeepAlivePeriod(_options.KeepAlivePeriod.Value);
        }

        if (_options.UseTls)
        {
            builder.WithTlsOptions(tls =>
            {
                tls.UseTls();
                if (_options.AllowUntrustedCertificates)
                {
                    tls.WithAllowUntrustedCertificates();
                    tls.WithIgnoreCertificateChainErrors();
                    tls.WithIgnoreCertificateRevocationErrors();
                }
            });
        }

        foreach (var (name, value) in _options.UserProperties)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                builder.WithUserProperty(name, MqttNetMessageMapper.ToUtf8Memory(value));
            }
        }

        ApplyLastWill(builder, _options.LastWill);
        return builder.Build();
    }

    private static void ApplyLastWill(
        MqttClientOptionsBuilder builder,
        MqttNetLastWillOptions? lastWill)
    {
        if (lastWill is null)
        {
            return;
        }

        var payload = lastWill.Payload;
        ArgumentNullException.ThrowIfNull(payload);

        builder
            .WithWillTopic(lastWill.Topic)
            .WithWillPayload(payload.ToArray())
            .WithWillQualityOfServiceLevel(
                MqttNetMessageMapper.ToMqttNetQualityOfService(lastWill.QualityOfService))
            .WithWillRetain(lastWill.Retain);

        if (!string.IsNullOrWhiteSpace(lastWill.ContentType))
        {
            builder.WithWillContentType(lastWill.ContentType);
        }

        if (!string.IsNullOrWhiteSpace(lastWill.Properties?.CorrelationId))
        {
            builder.WithWillCorrelationData(
                System.Text.Encoding.UTF8.GetBytes(lastWill.Properties.CorrelationId));
        }

        if (!string.IsNullOrWhiteSpace(lastWill.Properties?.ResponseTopic))
        {
            builder.WithWillResponseTopic(lastWill.Properties.ResponseTopic);
        }

        if (lastWill.Properties?.UserProperties is null)
        {
            return;
        }

        foreach (var (name, value) in lastWill.Properties.UserProperties)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                builder.WithWillUserProperty(name, MqttNetMessageMapper.ToUtf8Memory(value));
            }
        }
    }

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
            ClientId = _options.ClientId,
            Attributes = attributes ?? CreateBaseAttributes()
        });

    private Dictionary<string, string> CreateBaseAttributes()
        => new(StringComparer.Ordinal)
        {
            ["host"] = _options.Host,
            ["port"] = _options.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["useTls"] = _options.UseTls.ToString()
        };

    private IReadOnlyDictionary<string, string> CreateConnectAttributes(
        MqttClientConnectResult result)
    {
        var values = CreateBaseAttributes();
        if (!string.IsNullOrWhiteSpace(result.AssignedClientIdentifier))
        {
            values["assignedClientId"] = result.AssignedClientIdentifier;
        }

        values["sessionPresent"] = result.IsSessionPresent.ToString();
        values["resultCode"] = result.ResultCode.ToString();
        return values;
    }

    private static bool IsSuccessfulPublish(MqttClientPublishResult result)
        => result.IsSuccess ||
           result.ReasonCode == MqttClientPublishReasonCode.Success ||
           result.ReasonCode == MqttClientPublishReasonCode.NoMatchingSubscribers;

    private static MqttNetClientOptions ValidateOptions(MqttNetClientOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new ArgumentException("MQTT host is required.", nameof(options));
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

        if (options.ReconnectDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.ReconnectDelay,
                "MQTT reconnect delay must be greater than zero.");
        }

        if (options.KeepAlivePeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.KeepAlivePeriod,
                "MQTT keep-alive period must be greater than zero when set.");
        }

        if (options.LastWill is not null)
        {
            ValidateLastWill(options.LastWill);
        }

        return options;
    }

    private static void ValidateLastWill(MqttNetLastWillOptions lastWill)
    {
        var topicValidation =
            FluxFlow.Components.Mqtt.Validation.MqttTopicValidator.ValidatePublishTopic(
                lastWill.Topic);
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
        var topicValidation =
            FluxFlow.Components.Mqtt.Validation.MqttTopicValidator.ValidatePublishTopic(
                request.Topic);
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
        var topicValidation =
            FluxFlow.Components.Mqtt.Validation.MqttTopicValidator.ValidateSubscriptionFilter(
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
