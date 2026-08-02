namespace FluxFlow.Components.Mqtt.Transport;

public sealed class MqttTransportException : Exception
{
    public MqttTransportException(
        string message,
        string category,
        bool isTransient,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        Category = category.Trim();
        IsTransient = isTransient;
    }

    public string Category { get; }

    public bool IsTransient { get; }
}
