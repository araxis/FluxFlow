namespace FluxFlow.Components.State.Timing;

public sealed class SystemStateClock : IStateClock
{
    public static SystemStateClock Instance { get; } = new();

    private SystemStateClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
