namespace FluxFlow.Components.FileSystem.Timing;

public interface IFileSystemClock
{
    DateTimeOffset UtcNow { get; }
}
