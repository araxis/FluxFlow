namespace FluxFlow.Components.Mqtt.Contracts;

public sealed class MqttClientUnavailableException : InvalidOperationException
{
    public MqttClientUnavailableException()
        : base("The MQTT client is not available.")
    {
    }

    public MqttClientUnavailableException(string message)
        : base(message)
    {
    }

    public MqttClientUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
