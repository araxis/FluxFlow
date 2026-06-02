namespace FluxFlow.Components.Observability.Timing;

public sealed class SystemObservabilityClock : IObservabilityClock
{
    public static SystemObservabilityClock Instance { get; } = new();

    private SystemObservabilityClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
