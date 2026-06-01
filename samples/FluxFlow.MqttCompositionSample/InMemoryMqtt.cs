using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using System.Runtime.CompilerServices;

namespace FluxFlow.MqttCompositionSample;

internal sealed class InMemoryMqttClientFactory(InMemoryMqttClientAdapter adapter) : IMqttClientFactory
{
    private readonly object _gate = new();
    private readonly List<MqttClientFactoryContext> _contexts = [];

    public IReadOnlyList<MqttClientFactoryContext> Contexts
    {
        get
        {
            lock (_gate)
            {
                return _contexts.ToArray();
            }
        }
    }

    public ValueTask<MqttClientLease> CreateAsync(
        MqttClientFactoryContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _contexts.Add(context);
        }

        return ValueTask.FromResult(MqttClientLease.Shared(adapter));
    }
}

internal sealed class InMemoryMqttClientAdapter(IReadOnlyList<MqttReceivedMessage> seedMessages)
    : IMqttClientAdapter
{
    private readonly object _gate = new();
    private readonly List<MqttPublishRequest> _published = [];

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
        var messages = seedMessages
            .Where(message => TopicMatches(options.TopicFilter ?? string.Empty, message.Topic))
            .ToArray();

        return ValueTask.FromResult<IMqttSubscription>(new InMemoryMqttSubscription(messages));
    }

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;

    private static bool TopicMatches(string filter, string topic)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return false;
        }

        var filterLevels = filter.Split('/');
        var topicLevels = topic.Split('/');

        for (var index = 0; index < filterLevels.Length; index++)
        {
            var filterLevel = filterLevels[index];
            if (filterLevel == "#")
            {
                return index == filterLevels.Length - 1;
            }

            if (index >= topicLevels.Length)
            {
                return false;
            }

            if (filterLevel != "+" &&
                !string.Equals(filterLevel, topicLevels[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return filterLevels.Length == topicLevels.Length;
    }
}

internal sealed class InMemoryMqttSubscription(IReadOnlyList<MqttReceivedMessage> messages) : IMqttSubscription
{
    public IAsyncEnumerable<MqttReceivedMessage> Messages => ReadAsync();

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;

    private async IAsyncEnumerable<MqttReceivedMessage> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return message;
            await Task.Yield();
        }
    }
}
