using FluxFlow.Components.Storage.Timing;

namespace FluxFlow.Components.Storage.FileSystem.Tests;

internal sealed class RecordingStorageClock(DateTimeOffset utcNow) : IStorageClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
