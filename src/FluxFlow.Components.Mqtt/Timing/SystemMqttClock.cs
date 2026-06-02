namespace FluxFlow.Components.Mqtt.Timing;

public sealed class SystemMqttClock : IMqttClock
{
    public static SystemMqttClock Instance { get; } = new();

    private SystemMqttClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
