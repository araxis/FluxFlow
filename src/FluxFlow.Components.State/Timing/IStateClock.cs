namespace FluxFlow.Components.State.Timing;

public interface IStateClock
{
    DateTimeOffset UtcNow { get; }
}
