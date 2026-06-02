namespace FluxFlow.Components.Http.Timing;

public interface IHttpClock
{
    DateTimeOffset UtcNow { get; }
}
