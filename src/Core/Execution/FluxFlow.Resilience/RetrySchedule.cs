namespace FluxFlow.Resilience;

public static class RetrySchedule
{
    public static TimeSpan GetDelay(
        RetryPolicy policy,
        int retryNumber,
        double jitterSample = 0.5)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (retryNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(retryNumber));
        if (jitterSample is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(jitterSample));

        var maximumTicks = policy.MaximumDelay.Ticks;
        var baseTicks = policy.Strategy switch
        {
            RetryBackoffStrategy.Fixed => policy.InitialDelay.Ticks,
            RetryBackoffStrategy.Linear => AddSaturating(
                policy.InitialDelay.Ticks,
                MultiplySaturating(policy.Increment.Ticks, retryNumber - 1L)),
            RetryBackoffStrategy.Exponential => ScaleExponential(
                policy.InitialDelay.Ticks,
                retryNumber - 1,
                maximumTicks),
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy.Strategy, "Retry strategy is not supported.")
        };
        baseTicks = Math.Min(baseTicks, maximumTicks);

        var jitterMultiplier = 1d + (((jitterSample * 2d) - 1d) * policy.JitterFactor);
        var jitteredTicks = baseTicks * jitterMultiplier;
        if (double.IsNaN(jitteredTicks) || jitteredTicks <= 0)
            return TimeSpan.Zero;

        return TimeSpan.FromTicks((long)Math.Min(maximumTicks, Math.Min(long.MaxValue, jitteredTicks)));
    }

    private static long MultiplySaturating(long value, long multiplier)
    {
        if (value == 0 || multiplier == 0)
            return 0;
        return value > long.MaxValue / multiplier ? long.MaxValue : value * multiplier;
    }

    private static long AddSaturating(long left, long right)
        => left > long.MaxValue - right ? long.MaxValue : left + right;

    private static long ScaleExponential(long value, int exponent, long maximum)
    {
        if (value == 0 || maximum == 0)
            return 0;

        var result = Math.Min(value, maximum);
        for (var index = 0; index < exponent && result < maximum; index++)
            result = result > maximum / 2 ? maximum : result * 2;
        return result;
    }
}
