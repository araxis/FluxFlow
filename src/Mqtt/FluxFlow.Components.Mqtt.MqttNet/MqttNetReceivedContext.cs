using FluxFlow.Components.Mqtt.Contracts;
using MQTTnet;

namespace FluxFlow.Components.Mqtt.MqttNet;

internal sealed class MqttNetReceivedContext(
    MqttReceivedMessage message,
    MqttNetReceivedAcknowledgement acknowledgement)
    : IMqttReceivedContext
{
    public MqttReceivedMessage Message { get; } = message;

    public ValueTask AckAsync(CancellationToken cancellationToken = default)
        => acknowledgement.AckAsync(cancellationToken);

    public ValueTask NackAsync(
        Exception? error = null,
        CancellationToken cancellationToken = default)
        => acknowledgement.NackAsync(error, cancellationToken);
}

internal sealed class MqttNetReceivedAcknowledgement(
    MqttApplicationMessageReceivedEventArgs args,
    bool manualAcknowledgement)
{
    private int _completed;

    public async ValueTask AckAsync(CancellationToken cancellationToken)
    {
        if (!manualAcknowledgement ||
            Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        await args.AcknowledgeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask NackAsync(Exception? error, CancellationToken cancellationToken)
    {
        if (!manualAcknowledgement ||
            Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        args.ProcessingFailed = true;
        args.ReasonCode = MqttApplicationMessageReceivedReasonCode.ImplementationSpecificError;
        if (!string.IsNullOrWhiteSpace(error?.Message))
        {
            args.ResponseReasonString = TrimReason(error.Message);
        }

        await args.AcknowledgeAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string TrimReason(string value)
        => value.Length <= 128 ? value : value[..128];
}
