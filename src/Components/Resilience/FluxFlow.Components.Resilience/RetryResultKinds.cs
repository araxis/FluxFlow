namespace FluxFlow.Components.Resilience;

public static class RetryResultKinds
{
    public const string Attempt = "retry.attempt";
    public const string Completed = "retry.completed";
    public const string Scheduled = "retry.scheduled";
    public const string Exhausted = "retry.exhausted";
    public const string Cancelled = "retry.cancelled";
    public const string Rejected = "retry.rejected";
}
