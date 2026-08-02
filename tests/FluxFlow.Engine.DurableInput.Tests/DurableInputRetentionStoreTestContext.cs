namespace FluxFlow.Engine.DurableInput.Tests;

/// <summary>
/// Owns the narrow capabilities required by the reusable input-retention suite.
/// </summary>
public sealed class DurableInputRetentionStoreTestContext : IAsyncDisposable
{
    private Func<ValueTask>? _disposeAsync;

    private DurableInputRetentionStoreTestContext(
        IDurableInputStore store,
        IDurableInputDeadLetterStore deadLetters,
        IDurableInputStatusStore status,
        IDurableInputRetentionStore retention,
        Func<ValueTask> disposeAsync)
    {
        Store = store;
        DeadLetters = deadLetters;
        Status = status;
        Retention = retention;
        _disposeAsync = disposeAsync;
    }

    public IDurableInputStore Store { get; }

    public IDurableInputDeadLetterStore DeadLetters { get; }

    public IDurableInputStatusStore Status { get; }

    public IDurableInputRetentionStore Retention { get; }

    public static DurableInputRetentionStoreTestContext Create(
        IDurableInputStore store,
        IDurableInputDeadLetterStore deadLetters,
        IDurableInputStatusStore status,
        IDurableInputRetentionStore retention,
        Func<ValueTask> disposeAsync)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(deadLetters);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(retention);
        ArgumentNullException.ThrowIfNull(disposeAsync);
        return new DurableInputRetentionStoreTestContext(
            store,
            deadLetters,
            status,
            retention,
            disposeAsync);
    }

    public async ValueTask DisposeAsync()
    {
        var disposeAsync = Interlocked.Exchange(ref _disposeAsync, null);
        if (disposeAsync is not null)
            await disposeAsync().ConfigureAwait(false);
    }
}
