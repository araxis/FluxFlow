using FluxFlow.Components.Mqtt.Contracts;
using Pulse.Mqtt.Client;
using Pulse.Mqtt.Protocol;

namespace FluxFlow.Components.Mqtt.PulseMqtt;

internal sealed class PulseMqttReceivedContext(
    MqttReceivedMessage message,
    MqttAcknowledgedRoutedMessage? acknowledgement = null)
    : IMqttReceivedContext
{
    private int _completed;

    public MqttReceivedMessage Message { get; } = message;

    public ValueTask AckAsync(CancellationToken cancellationToken = default)
    {
        if (acknowledgement is null ||
            Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        return acknowledgement.AcknowledgeAsync(cancellationToken);
    }

    public ValueTask NackAsync(
        Exception? error = null,
        CancellationToken cancellationToken = default)
    {
        if (acknowledgement is null ||
            Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        return acknowledgement.RejectAsync(
            MqttReasonCode.UnspecifiedError,
            TrimReason(error?.Message),
            cancellationToken);
    }

    private static string? TrimReason(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= 128
                ? value
                : value[..128];
}
