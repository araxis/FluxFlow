using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Contracts;
using System.Collections.Immutable;
using System.Threading.Channels;

namespace FluxFlow.Components.Mqtt.Subscriptions;

public sealed record MqttTriggerRegistrationOptions
{
    private IReadOnlyList<MqttSubscriptionTarget> _subscriptions =
        ImmutableArray<MqttSubscriptionTarget>.Empty;

    public required string TriggerId { get; init; }

    public IReadOnlyList<MqttSubscriptionTarget> Subscriptions
    {
        get => _subscriptions;
        init => _subscriptions = value is null || value.Count == 0
            ? ImmutableArray<MqttSubscriptionTarget>.Empty
            : value.ToImmutableArray();
    }

    public MqttWorkflowAcknowledgement WorkflowAcknowledgement { get; init; }

    public MqttBrokerAcknowledgement BrokerAcknowledgement { get; init; }

    public int MaximumPendingMessages { get; init; } = 128;
}

public interface IMqttTriggerRegistration : IAsyncDisposable
{
    IAsyncEnumerable<MqttTriggerDelivery> Messages { get; }
}

public sealed class MqttTriggerDelivery
{
    private readonly Func<MqttWorkflowOutcome, CancellationToken, ValueTask> _complete;
    private int _completed;

    internal MqttTriggerDelivery(
        MqttReceivedApplicationMessage message,
        Func<MqttWorkflowOutcome, CancellationToken, ValueTask> complete)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));
    }

    public MqttReceivedApplicationMessage Message { get; }

    public ValueTask CompleteBrokerAcknowledgementAsync(
        MqttWorkflowOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return ValueTask.CompletedTask;

        return _complete(outcome, cancellationToken);
    }
}

internal sealed class MqttTriggerRegistration : IMqttTriggerRegistration
{
    private readonly Channel<MqttTriggerDelivery> _messages;
    private readonly Func<MqttTriggerRegistration, ValueTask> _dispose;
    private int _disposed;

    public MqttTriggerRegistration(
        MqttTriggerRegistrationOptions options,
        Func<MqttTriggerRegistration, ValueTask> dispose)
    {
        Options = options;
        _dispose = dispose;
        _messages = Channel.CreateBounded<MqttTriggerDelivery>(new BoundedChannelOptions(
            options.MaximumPendingMessages)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
    }

    internal MqttTriggerRegistrationOptions Options { get; }

    public IAsyncEnumerable<MqttTriggerDelivery> Messages => _messages.Reader.ReadAllAsync();

    internal ValueTask WriteAsync(
        MqttTriggerDelivery delivery,
        CancellationToken cancellationToken)
        => _messages.Writer.WriteAsync(delivery, cancellationToken);

    internal void Complete(Exception? exception = null)
    {
        if (exception is null)
            _messages.Writer.TryComplete();
        else
            _messages.Writer.TryComplete(exception);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;

        Complete();
        return _dispose(this);
    }
}
