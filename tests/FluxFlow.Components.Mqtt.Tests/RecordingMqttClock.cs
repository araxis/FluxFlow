using FluxFlow.Components.Mqtt.Timing;

namespace FluxFlow.Components.Mqtt.Tests;

internal sealed class RecordingMqttClock(DateTimeOffset utcNow) : IMqttClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
