namespace FluxFlow.Components.Projections.Options;

public sealed class ProjectionsComponentOptions
{
    private TimeProvider _clock = TimeProvider.System;

    public TimeProvider Clock => _clock;

    public ProjectionsComponentOptions UseClock(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }
}
