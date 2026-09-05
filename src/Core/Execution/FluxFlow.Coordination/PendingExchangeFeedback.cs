namespace FluxFlow.Coordination;

/// <summary>
/// Reports how feedback affected a pending or recently settled exchange.
/// </summary>
public readonly record struct PendingExchangeFeedback<TKey, TContext, TOutcome>
    where TKey : notnull
{
    internal PendingExchangeFeedback(
        PendingExchangeFeedbackStatus status,
        PendingExchangeCompletion<TKey, TContext, TOutcome>? completion)
    {
        Status = status;
        Completion = completion;
    }

    public PendingExchangeFeedbackStatus Status { get; }

    public PendingExchangeCompletion<TKey, TContext, TOutcome>? Completion { get; }

    public bool IsResolved => Status == PendingExchangeFeedbackStatus.Resolved;
}
