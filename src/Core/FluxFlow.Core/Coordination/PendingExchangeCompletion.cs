namespace FluxFlow.Coordination;

/// <summary>
/// Describes the one terminal result of an accepted pending exchange.
/// </summary>
public sealed class PendingExchangeCompletion<TKey, TContext, TOutcome>
    where TKey : notnull
{
    internal PendingExchangeCompletion(
        TKey key,
        TContext context,
        PendingExchangeCompletionKind kind,
        TOutcome? outcome,
        Exception? error,
        DateTimeOffset completedAt)
    {
        Key = key;
        Context = context;
        Kind = kind;
        Outcome = outcome;
        Error = error;
        CompletedAt = completedAt;
    }

    public TKey Key { get; }

    public TContext Context { get; }

    public PendingExchangeCompletionKind Kind { get; }

    public TOutcome? Outcome { get; }

    public Exception? Error { get; }

    public DateTimeOffset CompletedAt { get; }
}
