namespace FluxFlow.Components.Projections.Timing;

public interface IProjectionClock
{
    DateTimeOffset UtcNow { get; }
}
