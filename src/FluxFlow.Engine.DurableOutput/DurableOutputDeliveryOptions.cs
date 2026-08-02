namespace FluxFlow.Engine.DurableOutput;

/// <summary>
/// Immutable settings for the optional durable-output delivery dispatcher.
/// </summary>
public sealed record DurableOutputDeliveryOptions
{
    public DurableOutputDeliveryOptions(
        TimeSpan leaseDuration,
        TimeSpan leaseRenewalInterval,
        TimeSpan retryDelay,
        TimeSpan idleDelay,
        int? maxDeliveryAttempts = null)
    {
        LeaseDuration = ValidatePositive(leaseDuration, nameof(leaseDuration));
        LeaseRenewalInterval = ValidatePositive(
            leaseRenewalInterval,
            nameof(leaseRenewalInterval));
        if (leaseRenewalInterval >= leaseDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseRenewalInterval),
                leaseRenewalInterval,
                "Lease renewal interval must be shorter than the lease duration.");
        }

        RetryDelay = ValidatePositive(retryDelay, nameof(retryDelay));
        IdleDelay = ValidatePositive(idleDelay, nameof(idleDelay));
        if (maxDeliveryAttempts is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDeliveryAttempts));

        MaxDeliveryAttempts = maxDeliveryAttempts;
    }

    public static DurableOutputDeliveryOptions Default { get; } = new(
        leaseDuration: TimeSpan.FromSeconds(30),
        leaseRenewalInterval: TimeSpan.FromSeconds(10),
        retryDelay: TimeSpan.FromSeconds(1),
        idleDelay: TimeSpan.FromMilliseconds(250));

    public TimeSpan LeaseDuration { get; }

    public TimeSpan LeaseRenewalInterval { get; }

    public TimeSpan RetryDelay { get; }

    public TimeSpan IdleDelay { get; }

    /// <summary>
    /// Maximum handler attempts before a failed lease is dead-lettered.
    /// A null value preserves unlimited retry.
    /// </summary>
    public int? MaxDeliveryAttempts { get; }

    internal static TimeSpan ValidatePositive(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(parameterName, value, "Duration must be greater than zero.");

        return value;
    }
}

/// <summary>
/// Temporary registration-time builder for durable-output delivery settings.
/// </summary>
public sealed class DurableOutputDeliveryOptionsBuilder
{
    public TimeSpan LeaseDuration { get; set; } = DurableOutputDeliveryOptions.Default.LeaseDuration;

    public TimeSpan LeaseRenewalInterval { get; set; } =
        DurableOutputDeliveryOptions.Default.LeaseRenewalInterval;

    public TimeSpan RetryDelay { get; set; } = DurableOutputDeliveryOptions.Default.RetryDelay;

    public TimeSpan IdleDelay { get; set; } = DurableOutputDeliveryOptions.Default.IdleDelay;

    public int? MaxDeliveryAttempts { get; set; }

    internal DurableOutputDeliveryOptions Build()
        => new(
            LeaseDuration,
            LeaseRenewalInterval,
            RetryDelay,
            IdleDelay,
            MaxDeliveryAttempts);
}
