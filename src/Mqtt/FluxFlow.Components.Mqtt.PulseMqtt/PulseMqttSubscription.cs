using System.Runtime.CompilerServices;
using FluxFlow.Components.Mqtt.Contracts;
using Pulse.Mqtt.Client;

namespace FluxFlow.Components.Mqtt.PulseMqtt;

internal sealed class PulseMqttSubscription : IMqttSubscription
{
    private readonly PulseMqttClient _owner;
    private readonly MqttRouteStream _stream;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public PulseMqttSubscription(
        PulseMqttClient owner,
        MqttRouteStream stream,
        string topicFilter,
        TimeProvider clock)
    {
        _owner = owner;
        _stream = stream;
        TopicFilter = topicFilter;
        _clock = clock;
    }

    public string TopicFilter { get; }

    public IAsyncEnumerable<IMqttReceivedContext> Messages => ReadMessagesAsync();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);
        try
        {
            await _owner.UnsubscribeAsync(this, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _lifetime.Dispose();
        }
    }

    private async IAsyncEnumerable<IMqttReceivedContext> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);

        IAsyncEnumerable<MqttRoutedMessage> messages =
            _stream.ReadAllAsync(linkedCancellation.Token);

        await using var enumerator = messages.GetAsyncEnumerator(
            linkedCancellation.Token);
        while (true)
        {
            MqttRoutedMessage routed;
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    yield break;
                }

                routed = enumerator.Current;
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                yield break;
            }

            yield return new PulseMqttReceivedContext(
                PulseMqttMessageMapper.ToReceivedMessage(
                    routed.Message,
                    _clock.GetUtcNow()));
        }
    }
}
