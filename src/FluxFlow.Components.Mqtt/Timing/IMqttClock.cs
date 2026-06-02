namespace FluxFlow.Components.Mqtt.Timing;

public interface IMqttClock
{
    DateTimeOffset UtcNow { get; }
}
