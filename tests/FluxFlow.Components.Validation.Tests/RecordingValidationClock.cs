using FluxFlow.Components.Validation.Timing;

namespace FluxFlow.Components.Validation.Tests;

internal sealed class RecordingValidationClock(params DateTimeOffset[] timestamps)
    : IValidationClock
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
