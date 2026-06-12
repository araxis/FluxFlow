namespace FluxFlow.Components.Assertions.Timing;

public sealed class SystemAssertionClock : IAssertionClock
{
    public static SystemAssertionClock Instance { get; } = new();

    private SystemAssertionClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
