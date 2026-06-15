namespace FluxFlow.Components.FileSystem.Options;

public sealed class FileSystemComponentOptions
{
    private TimeProvider _clock = TimeProvider.System;

    public TimeProvider Clock => _clock;

    public FileSystemComponentOptions UseClock(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }
}
