using FluxFlow.Components.Storage.Timing;

namespace FluxFlow.Components.Storage.Tests;

internal sealed class RecordingStorageClock(DateTimeOffset utcNow) : IStorageClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
