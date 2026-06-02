using FluxFlow.Components.State.Timing;

namespace FluxFlow.Components.State.Tests;

internal sealed class RecordingStateClock(params DateTimeOffset[] timestamps)
    : IStateClock
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
