using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace FluxFlow.Components.Mqtt.Tests;

/// <summary>
/// Deterministic in-memory MQTT adapter test double. Publish round-trips into
/// <see cref="Published"/>; subscriptions are fed through a bounded channel so
/// tests can push messages and observe delivery without sleeps. The adapter also
/// exposes an optional health stream for connection health assertions.
/// </summary>
internal sealed class RecordingMqttClientAdapter : IMqttClientAdapter, IMqttClientHealthSource
{
    private readonly object _gate = new();
    private readonly Channel<MqttReceivedMessage> _incoming =
        Channel.CreateUnbounded<MqttReceivedMessage>();
    private readonly Channel<MqttClientHealthEvent> _health =
        Channel.CreateUnbounded<MqttClientHealthEvent>();
    private readonly List<MqttPublishRequest> _published = [];
    private readonly List<RecordingMqttSubscription> _subscriptions = [];
    private readonly TaskCompletionSource _subscribed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _subscribeCalls;
    private int _disposeCalls;

    public IReadOnlyList<MqttPublishRequest> Published
    {
        get
        {
            lock (_gate)
            {
                return _published.ToArray();
            }
        }
    }

    public IReadOnlyList<RecordingMqttSubscription> Subscriptions
    {
        get
        {
            lock (_gate)
            {
                return _subscriptions.ToArray();
            }
        }
    }

    public int SubscribeCalls => Volatile.Read(ref _subscribeCalls);

    public int DisposeCalls => Volatile.Read(ref _disposeCalls);

    public MqttSubscriptionOptions? SubscriptionOptions { get; private set; }

    /// <summary>Completes when SubscribeAsync is first invoked.</summary>
    public Task Subscribed => _subscribed.Task;

    public ValueTask PublishAsync(
        MqttPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _published.Add(request);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IMqttSubscription> SubscribeAsync(
        MqttSubscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _subscribeCalls);
        SubscriptionOptions = options;

        var subscription = new RecordingMqttSubscription(_incoming.Reader);
        lock (_gate)
        {
            _subscriptions.Add(subscription);
        }

        _subscribed.TrySetResult();
        return ValueTask.FromResult<IMqttSubscription>(subscription);
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCalls);
        _incoming.Writer.TryComplete();
        _health.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    /// <summary>Pushes a message to whichever subscription is currently pumping.</summary>
    public void PushMessage(MqttReceivedMessage message)
        => _incoming.Writer.TryWrite(message);

    public void PushHealth(MqttClientHealthEvent health)
        => _health.Writer.TryWrite(health);

    public void CompleteHealth()
        => _health.Writer.TryComplete();

    public IAsyncEnumerable<MqttClientHealthEvent> Health => ReadHealthAsync();

    private async IAsyncEnumerable<MqttClientHealthEvent> ReadHealthAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var health in _health.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return health;
        }
    }
}

internal sealed class RecordingMqttSubscription(ChannelReader<MqttReceivedMessage> reader)
    : IMqttSubscription
{
    private int _disposeCalls;

    public int DisposeCalls => Volatile.Read(ref _disposeCalls);

    public bool Disposed => DisposeCalls > 0;

    public IAsyncEnumerable<MqttReceivedMessage> Messages => reader.ReadAllAsync();

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCalls);
        return ValueTask.CompletedTask;
    }
}
