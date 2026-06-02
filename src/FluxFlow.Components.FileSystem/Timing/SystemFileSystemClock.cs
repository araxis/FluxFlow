namespace FluxFlow.Components.FileSystem.Timing;

public sealed class SystemFileSystemClock : IFileSystemClock
{
    public static SystemFileSystemClock Instance { get; } = new();

    private SystemFileSystemClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
