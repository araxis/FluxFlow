namespace FluxFlow.Engine.DurableInput.Tests;

/// <summary>
/// Owns one store instance used by the reusable provider conformance suite.
/// </summary>
public sealed class DurableInputStoreTestContext : IAsyncDisposable
{
    private Func<ValueTask>? _disposeAsync;

    private DurableInputStoreTestContext(
        IDurableInputStore store,
        Func<ValueTask>? disposeAsync)
    {
        Store = store;
        _disposeAsync = disposeAsync;
    }

    public IDurableInputStore Store { get; }

    public static DurableInputStoreTestContext Create(
        IDurableInputStore store,
        Func<ValueTask>? disposeAsync = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new DurableInputStoreTestContext(store, disposeAsync);
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
