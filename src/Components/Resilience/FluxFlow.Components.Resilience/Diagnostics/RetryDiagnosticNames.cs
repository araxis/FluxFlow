namespace FluxFlow.Components.Resilience.Diagnostics;

public static class RetryDiagnosticNames
{
    public const string Attempted = "retry.attempted";
    public const string Scheduled = "retry.scheduled";
    public const string Completed = "retry.completed";
    public const string Exhausted = "retry.exhausted";
    public const string Cancelled = "retry.cancelled";
    public const string Rejected = "retry.rejected";
    public const string FeedbackIgnored = "retry.feedback_ignored";
}
