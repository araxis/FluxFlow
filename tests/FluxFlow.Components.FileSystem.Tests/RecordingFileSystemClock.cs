using FluxFlow.Components.FileSystem.Timing;

namespace FluxFlow.Components.FileSystem.Tests;

internal sealed class RecordingFileSystemClock(params DateTimeOffset[] timestamps)
    : IFileSystemClock
{
    private readonly Queue<DateTimeOffset> _timestamps = new(timestamps);
    private DateTimeOffset _last = timestamps.FirstOrDefault(DateTimeOffset.UnixEpoch);

    public DateTimeOffset UtcNow
    {
        get
        {
            if (_timestamps.TryDequeue(out var timestamp))
            {
                _last = timestamp;
            }

            return _last;
        }
    }
}
