using FluxFlow.Data;

namespace FluxFlow.Components.Mqtt.Client;

public sealed class MqttClientOperationException : Exception
{
    public MqttClientOperationException(MqttClientOperation operation, FlowError error)
        : base(error?.Message)
    {
        Operation = operation;
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public MqttClientOperation Operation { get; }
    public FlowError Error { get; }
}
