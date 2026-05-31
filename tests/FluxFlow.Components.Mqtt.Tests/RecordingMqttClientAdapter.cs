using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using System.Runtime.CompilerServices;

namespace FluxFlow.Components.Mqtt.Tests;

internal sealed class RecordingMqttClientAdapter : IMqttClientAdapter
{
    private readonly IReadOnlyList<MqttReceivedMessage> _messages;
    private readonly bool _waitForCancellation;
    private readonly Exception? _subscribeException;

    public RecordingMqttClientAdapter(params MqttReceivedMessage[] messages)
        : this(waitForCancellation: false, subscribeException: null, messages)
    {
    }

    public RecordingMqttClientAdapter(bool waitForCancellation, params MqttReceivedMessage[] messages)
        : this(waitForCancellation, subscribeException: null, messages)
    {
    }

    public RecordingMqttClientAdapter(Exception subscribeException, params MqttReceivedMessage[] messages)
        : this(waitForCancellation: false, subscribeException, messages)
    {
    }

    private RecordingMqttClientAdapter(
        bool waitForCancellation,
        Exception? subscribeException,
        params MqttReceivedMessage[] messages)
    {
        _waitForCancellation = waitForCancellation;
        _subscribeException = subscribeException;
        _messages = messages;
    }

    public List<MqttPublishRequest> Published { get; } = [];

    public MqttSubscriptionOptions? SubscriptionOptions { get; private set; }

    public bool Disposed { get; private set; }

    public ValueTask PublishAsync(
        MqttPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        Published.Add(request);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<MqttReceivedMessage> SubscribeAsync(
        MqttSubscriptionOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        SubscriptionOptions = options;

        foreach (var message in _messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return message;
            await Task.Yield();
        }

        if (_subscribeException is not null)
        {
            throw _subscribeException;
        }

        if (_waitForCancellation)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
