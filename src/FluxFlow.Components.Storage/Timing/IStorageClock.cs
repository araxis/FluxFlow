namespace FluxFlow.Components.Storage.Timing;

public interface IStorageClock
{
    DateTimeOffset UtcNow { get; }
}
