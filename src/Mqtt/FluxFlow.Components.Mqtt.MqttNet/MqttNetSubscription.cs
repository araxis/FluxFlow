using System.Threading.Channels;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;

namespace FluxFlow.Components.Mqtt.MqttNet;

internal sealed class MqttNetSubscription : IMqttSubscription
{
    private readonly MqttNetClient _owner;
    private readonly Channel<IMqttReceivedContext> _messages;
    private int _disposed;

    public MqttNetSubscription(
        MqttNetClient owner,
        Guid id,
        MqttTriggerOptions options)
    {
        _owner = owner;
        Id = id;
        Options = options;
        TopicFilter = options.TopicFilter ?? throw new ArgumentException(
            "MQTT subscription topic filter is required.",
            nameof(options));
        ManualAcknowledgement = options.Acknowledgement != MqttTriggerAcknowledgement.None;

        _messages = Channel.CreateBounded<IMqttReceivedContext>(
            new BoundedChannelOptions(options.BoundedCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    }

    public Guid Id { get; }

    public string TopicFilter { get; }

    public bool ManualAcknowledgement { get; }

    public MqttTriggerOptions Options { get; }

    public IAsyncEnumerable<IMqttReceivedContext> Messages
        => _messages.Reader.ReadAllAsync();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _owner.UnsubscribeAsync(this, CancellationToken.None).ConfigureAwait(false);
    }

    public async ValueTask<bool> WriteAsync(
        IMqttReceivedContext context,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        try
        {
            await _messages.Writer.WriteAsync(context, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    public void Complete(Exception? error = null)
    {
        Interlocked.Exchange(ref _disposed, 1);
        _messages.Writer.TryComplete(error);
    }
}
