using FluxFlow.Components.Observability.Timing;

namespace FluxFlow.Components.Observability.Tests;

internal sealed class RecordingObservabilityClock(params DateTimeOffset[] timestamps)
    : IObservabilityClock
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
