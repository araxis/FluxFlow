using FluxFlow.Components.Projections.Timing;

namespace FluxFlow.Components.Projections.Tests;

internal sealed class RecordingProjectionClock(DateTimeOffset utcNow) : IProjectionClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
