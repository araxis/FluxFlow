namespace FluxFlow.Components.Projections.Timing;

public sealed class SystemProjectionClock : IProjectionClock
{
    public static SystemProjectionClock Instance { get; } = new();

    private SystemProjectionClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
