namespace FluxFlow.Components.Http.Timing;

public sealed class SystemHttpClock : IHttpClock
{
    public static SystemHttpClock Instance { get; } = new();

    private SystemHttpClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
