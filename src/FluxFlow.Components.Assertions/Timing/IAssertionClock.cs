namespace FluxFlow.Components.Assertions.Timing;

public interface IAssertionClock
{
    DateTimeOffset UtcNow { get; }
}
