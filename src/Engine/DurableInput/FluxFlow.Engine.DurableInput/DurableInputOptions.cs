namespace FluxFlow.Engine.DurableInput;

public enum DurableInputAcknowledgementMode
{
    EngineAccepted = 0,
    WorkflowCompleted = 1
}

public sealed record DurableInputOptions
{
    public DurableInputOptions(
        int batchSize,
        TimeSpan leaseDuration,
        TimeSpan pollInterval,
        TimeSpan retryDelay,
        TimeSpan storeFailureDelay,
        int maxDeliveryAttempts)
        : this(
            batchSize,
            leaseDuration,
            pollInterval,
            retryDelay,
            storeFailureDelay,
            maxDeliveryAttempts,
            DurableInputAcknowledgementMode.EngineAccepted,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(10))
    {
    }

    public DurableInputOptions(
        int batchSize,
        TimeSpan leaseDuration,
        TimeSpan pollInterval,
        TimeSpan retryDelay,
        TimeSpan storeFailureDelay,
        int maxDeliveryAttempts,
        DurableInputAcknowledgementMode acknowledgementMode,
        TimeSpan workflowCompletionTimeout,
        TimeSpan leaseRenewalInterval)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (pollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        if (retryDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        if (storeFailureDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(storeFailureDelay));
        if (maxDeliveryAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDeliveryAttempts));
        if (!Enum.IsDefined(acknowledgementMode))
            throw new ArgumentOutOfRangeException(nameof(acknowledgementMode));
        if (workflowCompletionTimeout <= TimeSpan.Zero &&
            workflowCompletionTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(workflowCompletionTimeout));
        }
        if (leaseRenewalInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseRenewalInterval));
        if (acknowledgementMode == DurableInputAcknowledgementMode.WorkflowCompleted &&
            leaseRenewalInterval >= leaseDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseRenewalInterval),
                "The lease renewal interval must be shorter than the lease duration in workflow-completion mode.");
        }

        BatchSize = batchSize;
        LeaseDuration = leaseDuration;
        PollInterval = pollInterval;
        RetryDelay = retryDelay;
        StoreFailureDelay = storeFailureDelay;
        MaxDeliveryAttempts = maxDeliveryAttempts;
        AcknowledgementMode = acknowledgementMode;
        WorkflowCompletionTimeout = workflowCompletionTimeout;
        LeaseRenewalInterval = leaseRenewalInterval;
    }

    public static DurableInputOptions Default { get; } = new(
        batchSize: 64,
        leaseDuration: TimeSpan.FromSeconds(30),
        pollInterval: TimeSpan.FromMilliseconds(250),
        retryDelay: TimeSpan.FromSeconds(1),
        storeFailureDelay: TimeSpan.FromSeconds(2),
        maxDeliveryAttempts: 10);

    public int BatchSize { get; }

    public TimeSpan LeaseDuration { get; }

    public TimeSpan PollInterval { get; }

    public TimeSpan RetryDelay { get; }

    public TimeSpan StoreFailureDelay { get; }

    public int MaxDeliveryAttempts { get; }

    public DurableInputAcknowledgementMode AcknowledgementMode { get; }

    public TimeSpan WorkflowCompletionTimeout { get; }

    public TimeSpan LeaseRenewalInterval { get; }
}

public sealed class DurableInputOptionsBuilder
{
    public int BatchSize { get; set; } = DurableInputOptions.Default.BatchSize;

    public TimeSpan LeaseDuration { get; set; } = DurableInputOptions.Default.LeaseDuration;

    public TimeSpan PollInterval { get; set; } = DurableInputOptions.Default.PollInterval;

    public TimeSpan RetryDelay { get; set; } = DurableInputOptions.Default.RetryDelay;

    public TimeSpan StoreFailureDelay { get; set; } = DurableInputOptions.Default.StoreFailureDelay;

    public int MaxDeliveryAttempts { get; set; } = DurableInputOptions.Default.MaxDeliveryAttempts;

    public DurableInputAcknowledgementMode AcknowledgementMode { get; set; } =
        DurableInputOptions.Default.AcknowledgementMode;

    public TimeSpan WorkflowCompletionTimeout { get; set; } =
        DurableInputOptions.Default.WorkflowCompletionTimeout;

    public TimeSpan LeaseRenewalInterval { get; set; } =
        DurableInputOptions.Default.LeaseRenewalInterval;

    internal DurableInputOptions Build()
    {
        if (BatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(BatchSize));
        if (LeaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(LeaseDuration));
        if (PollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(PollInterval));
        if (RetryDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RetryDelay));
        if (StoreFailureDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(StoreFailureDelay));
        if (MaxDeliveryAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxDeliveryAttempts));

        return new DurableInputOptions(
            BatchSize,
            LeaseDuration,
            PollInterval,
            RetryDelay,
            StoreFailureDelay,
            MaxDeliveryAttempts,
            AcknowledgementMode,
            WorkflowCompletionTimeout,
            LeaseRenewalInterval);
    }
}
