namespace FluxFlow.Components.Resilience;

public static class RetryErrorCodeNames
{
    public const string Nak = "retry.nak";
    public const string Timeout = "retry.timeout";
    public const string Exhausted = "retry.exhausted";
    public const string Cancelled = "retry.cancelled";
    public const string Duplicate = "retry.duplicate";
    public const string CapacityReached = "retry.capacity_reached";
    public const string Stopped = "retry.stopped";
}
