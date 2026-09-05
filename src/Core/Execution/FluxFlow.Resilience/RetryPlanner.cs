namespace FluxFlow.Resilience;

public static class RetryPlanner
{
    public static RetryDirective PlanAttempt(
        RetryPolicy policy,
        int attempt,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        double jitterSample = 0.5)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (attempt <= 0)
            throw new ArgumentOutOfRangeException(nameof(attempt));
        if (now < startedAt)
            throw new ArgumentOutOfRangeException(nameof(now), now, "Current time must not precede the retry start time.");

        if (policy.MaximumAttempts is { } maximumAttempts && attempt > maximumAttempts)
            return RetryDirective.Exhausted(attempt, startedAt);

        var elapsed = now - startedAt;
        if (policy.MaximumDuration is { } maximumDuration && elapsed >= maximumDuration)
            return RetryDirective.Exhausted(attempt, startedAt);

        var retryNumber = Math.Max(1, attempt - 1);
        var delay = RetrySchedule.GetDelay(policy, retryNumber, jitterSample);
        if (policy.MaximumDuration is { } duration && delay > duration - elapsed)
            return RetryDirective.Exhausted(attempt, startedAt);

        return RetryDirective.Wait(attempt, startedAt, delay);
    }
}
