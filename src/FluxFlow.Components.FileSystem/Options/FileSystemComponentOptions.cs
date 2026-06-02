using FluxFlow.Components.FileSystem.Timing;

namespace FluxFlow.Components.FileSystem.Options;

public sealed class FileSystemComponentOptions
{
    private IFileSystemClock _clock = SystemFileSystemClock.Instance;

    public IFileSystemClock Clock => _clock;

    public FileSystemComponentOptions UseClock(IFileSystemClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }
}
