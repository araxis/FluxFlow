using FluxFlow.Components.Projections.Timing;

namespace FluxFlow.Components.Projections.Options;

public sealed class ProjectionsComponentOptions
{
    private IProjectionClock _clock = SystemProjectionClock.Instance;

    public IProjectionClock Clock => _clock;

    public ProjectionsComponentOptions UseClock(IProjectionClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }
}
