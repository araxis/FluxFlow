using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using System.Runtime.CompilerServices;

namespace FluxFlow.Components.Mqtt.Tests;

internal sealed class RecordingMqttClientAdapter : IMqttClientAdapter, IMqttClientHealthSource
{
    private readonly IReadOnlyList<MqttReceivedMessage> _messages;
    private readonly bool _waitForCancellation;
    private readonly Exception? _subscriptionException;
    private readonly Exception? _subscribeStartupException;

    public RecordingMqttClientAdapter(params MqttReceivedMessage[] messages)
        : this(waitForCancellation: false, subscribeStartupException: null, subscriptionException: null, messages)
    {
    }

    public RecordingMqttClientAdapter(bool waitForCancellation, params MqttReceivedMessage[] messages)
        : this(waitForCancellation, subscribeStartupException: null, subscriptionException: null, messages)
    {
    }

    public RecordingMqttClientAdapter(Exception subscriptionException, params MqttReceivedMessage[] messages)
        : this(waitForCancellation: false, subscribeStartupException: null, subscriptionException, messages)
    {
    }

    public RecordingMqttClientAdapter(
        bool waitForCancellation,
        Exception? subscribeStartupException,
        Exception? subscriptionException,
        params MqttReceivedMessage[] messages)
    {
        _waitForCancellation = waitForCancellation;
        _subscribeStartupException = subscribeStartupException;
        _subscriptionException = subscriptionException;
        _messages = messages;
    }

    public Queue<Exception?> PublishOutcomes { get; } = [];

    public List<MqttPublishRequest> Published { get; } = [];

    public List<MqttClientHealthEvent> HealthEvents { get; } = [];

    public MqttSubscriptionOptions? SubscriptionOptions { get; private set; }

    public List<RecordingMqttSubscription> Subscriptions { get; } = [];

    public bool Disposed { get; private set; }

    public ValueTask PublishAsync(
        MqttPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        Published.Add(request);

        if (PublishOutcomes.TryDequeue(out var exception) && exception is not null)
        {
            throw exception;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IMqttSubscription> SubscribeAsync(
        MqttSubscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (_subscribeStartupException is not null)
        {
            throw _subscribeStartupException;
        }

        SubscriptionOptions = options;
        var subscription = new RecordingMqttSubscription(
            _messages,
            _waitForCancellation,
            _subscriptionException);
        Subscriptions.Add(subscription);
        return ValueTask.FromResult<IMqttSubscription>(subscription);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<MqttClientHealthEvent> Health => ReadHealthAsync();

    private async IAsyncEnumerable<MqttClientHealthEvent> ReadHealthAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var health in HealthEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return health;
            await Task.Yield();
        }

        if (_waitForCancellation)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}

internal sealed class RecordingMqttSubscription(
    IReadOnlyList<MqttReceivedMessage> messages,
    bool waitForCancellation,
    Exception? subscriptionException) : IMqttSubscription
{
    public bool Disposed { get; private set; }

    public IAsyncEnumerable<MqttReceivedMessage> Messages => ReadAsync();

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    private async IAsyncEnumerable<MqttReceivedMessage> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return message;
            await Task.Yield();
        }

        if (subscriptionException is not null)
        {
            throw subscriptionException;
        }

        if (waitForCancellation)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
