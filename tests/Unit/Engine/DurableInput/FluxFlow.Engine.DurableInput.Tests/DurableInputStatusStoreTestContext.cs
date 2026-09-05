namespace FluxFlow.Engine.DurableInput.Tests;

/// <summary>
/// Owns one provider's normal and optional status capabilities for the reusable
/// durable-input status conformance suite.
/// </summary>
public sealed class DurableInputStatusStoreTestContext : IAsyncDisposable
{
    private Func<ValueTask>? _disposeAsync;

    private DurableInputStatusStoreTestContext(
        IDurableInputStore store,
        IDurableInputStatusStore statusStore,
        Func<ValueTask>? disposeAsync)
    {
        Store = store;
        StatusStore = statusStore;
        _disposeAsync = disposeAsync;
    }

    public IDurableInputStore Store { get; }

    public IDurableInputStatusStore StatusStore { get; }

    public static DurableInputStatusStoreTestContext Create(
        IDurableInputStore store,
        IDurableInputStatusStore statusStore,
        Func<ValueTask>? disposeAsync = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(statusStore);
        return new DurableInputStatusStoreTestContext(store, statusStore, disposeAsync);
    }

    public async ValueTask DisposeAsync()
    {
        var disposeAsync = Interlocked.Exchange(ref _disposeAsync, null);
        if (disposeAsync is not null)
            await disposeAsync().ConfigureAwait(false);
    }
}
