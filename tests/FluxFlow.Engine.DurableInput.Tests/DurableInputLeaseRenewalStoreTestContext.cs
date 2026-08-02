namespace FluxFlow.Engine.DurableInput.Tests;

/// <summary>
/// Owns one store used by the reusable lease-renewal capability suite.
/// </summary>
public sealed class DurableInputLeaseRenewalStoreTestContext : IAsyncDisposable
{
    private Func<ValueTask>? _disposeAsync;

    private DurableInputLeaseRenewalStoreTestContext(
        IDurableInputStore store,
        IDurableInputLeaseRenewalStore renewalStore,
        Func<ValueTask>? disposeAsync)
    {
        Store = store;
        RenewalStore = renewalStore;
        _disposeAsync = disposeAsync;
    }

    public IDurableInputStore Store { get; }

    public IDurableInputLeaseRenewalStore RenewalStore { get; }

    public static DurableInputLeaseRenewalStoreTestContext Create(
        IDurableInputStore store,
        IDurableInputLeaseRenewalStore renewalStore,
        Func<ValueTask>? disposeAsync = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(renewalStore);
        return new DurableInputLeaseRenewalStoreTestContext(
            store,
            renewalStore,
            disposeAsync);
    }

    public async ValueTask DisposeAsync()
    {
        var disposeAsync = Interlocked.Exchange(ref _disposeAsync, null);
        if (disposeAsync is not null)
            await disposeAsync().ConfigureAwait(false);
    }
}
