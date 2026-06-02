namespace FluxFlow.Components.Storage.Timing;

public sealed class SystemStorageClock : IStorageClock
{
    public static SystemStorageClock Instance { get; } = new();

    private SystemStorageClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
