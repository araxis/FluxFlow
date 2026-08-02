namespace FluxFlow.Engine.DurableInput;

/// <summary>
/// Creates an explicit workflow-completion subscription for one exact durable-input lease.
/// </summary>
/// <remarks>
/// Implementations own domain-specific completion observation and must correlate late signals to
/// the exact <see cref="DurableInputLease.LeaseToken"/>. FluxFlow does not infer completion from
/// workflow outputs, graph state, trace identifiers, or timing.
/// </remarks>
public interface IDurableInputCompletionSource
{
    ValueTask<IDurableInputCompletionSubscription> SubscribeAsync(
        DurableInputLease lease,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents one completion observation registered before Engine dispatch.
/// </summary>
public interface IDurableInputCompletionSubscription : IAsyncDisposable
{
    Task<DurableInputCompletionResult> Completion { get; }
}

/// <summary>
/// Explicit terminal result reported by a durable-input completion source.
/// </summary>
public sealed record DurableInputCompletionResult
{
    private DurableInputCompletionResult(string? failureDescription)
    {
        FailureDescription = failureDescription;
    }

    public static DurableInputCompletionResult Completed { get; } = new(failureDescription: null);

    public bool IsCompleted => FailureDescription is null;

    public string? FailureDescription { get; }

    public static DurableInputCompletionResult Failed(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (!string.Equals(description, description.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Failure description cannot have surrounding whitespace.",
                nameof(description));
        }

        return new DurableInputCompletionResult(description);
    }
}
