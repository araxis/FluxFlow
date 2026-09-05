namespace FluxFlow.Engine.DurableInput.Tests;

/// <summary>
/// Owns one provider instance used by the reusable dead-letter conformance suite.
/// </summary>
public sealed class DurableInputDeadLetterStoreTestContext : IAsyncDisposable
{
    private Func<ValueTask>? _disposeAsync;

    private DurableInputDeadLetterStoreTestContext(
        IDurableInputStore store,
        IDurableInputDeadLetterStore deadLetters,
        Func<ValueTask>? disposeAsync)
    {
        Store = store;
        DeadLetters = deadLetters;
        _disposeAsync = disposeAsync;
    }

    public IDurableInputStore Store { get; }

    public IDurableInputDeadLetterStore DeadLetters { get; }

    public static DurableInputDeadLetterStoreTestContext Create(
        IDurableInputStore store,
        IDurableInputDeadLetterStore deadLetters,
        Func<ValueTask>? disposeAsync = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(deadLetters);
        return new DurableInputDeadLetterStoreTestContext(store, deadLetters, disposeAsync);
    }

    public async ValueTask DisposeAsync()
    {
        var disposeAsync = Interlocked.Exchange(ref _disposeAsync, null);
        if (disposeAsync is not null)
        {
            await disposeAsync().ConfigureAwait(false);
        }
    }
}
