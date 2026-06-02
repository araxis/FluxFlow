namespace FluxFlow.Components.Observability.Timing;

public interface IObservabilityClock
{
    DateTimeOffset UtcNow { get; }
}
