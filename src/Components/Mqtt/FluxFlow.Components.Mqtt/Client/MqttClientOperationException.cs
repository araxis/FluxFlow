using FluxFlow.Data;

namespace FluxFlow.Components.Mqtt.Client;

public sealed class MqttClientOperationException : Exception
{
    public MqttClientOperationException(MqttClientOperation operation, FlowError error)
        : this(operation, error, null)
    {
    }

    public MqttClientOperationException(MqttClientOperation operation, FlowError error, Exception? innerException)
        : base(error?.Message, innerException)
    {
        Operation = operation;
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public MqttClientOperation Operation { get; }
    public FlowError Error { get; }
}
