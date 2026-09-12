namespace FluxFlow.Coordination;

/// <summary>
/// Reports whether a pending exchange was accepted and exposes its completion.
/// </summary>
public readonly record struct PendingExchangeStart<TKey, TContext, TOutcome>
    where TKey : notnull
{
    internal PendingExchangeStart(
        PendingExchangeStartStatus status,
        Task<PendingExchangeCompletion<TKey, TContext, TOutcome>>? completion)
    {
        Status = status;
        Completion = completion;
    }

    public PendingExchangeStartStatus Status { get; }

    public Task<PendingExchangeCompletion<TKey, TContext, TOutcome>>? Completion { get; }

    public bool IsAccepted => Status == PendingExchangeStartStatus.Accepted;
}
