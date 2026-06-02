namespace FluxFlow.Components.Http.Timing;

internal static class HttpClockSupport
{
    public static long GetElapsedMilliseconds(
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
        => Math.Max(0, (long)(completedAt - startedAt).TotalMilliseconds);
}
