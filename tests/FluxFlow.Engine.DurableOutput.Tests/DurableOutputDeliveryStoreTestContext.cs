namespace FluxFlow.Engine.DurableOutput.Tests;

/// <summary>
/// Owns the narrow capture and delivery capabilities required by the reusable
/// delivery-store conformance suite.
/// </summary>
public sealed class DurableOutputDeliveryStoreTestContext : IAsyncDisposable
{
    private Func<ValueTask>? _disposeAsync;

    private DurableOutputDeliveryStoreTestContext(
        IDurableOutputStore captureStore,
        IDurableOutputDeliveryStore deliveryStore,
        Func<ValueTask> disposeAsync)
    {
        CaptureStore = captureStore;
        DeliveryStore = deliveryStore;
        _disposeAsync = disposeAsync;
    }

    public IDurableOutputStore CaptureStore { get; }

    public IDurableOutputDeliveryStore DeliveryStore { get; }

    public static DurableOutputDeliveryStoreTestContext Create(
        IDurableOutputStore captureStore,
        IDurableOutputDeliveryStore deliveryStore,
        Func<ValueTask> disposeAsync)
    {
        ArgumentNullException.ThrowIfNull(captureStore);
        ArgumentNullException.ThrowIfNull(deliveryStore);
        ArgumentNullException.ThrowIfNull(disposeAsync);
        return new DurableOutputDeliveryStoreTestContext(
            captureStore,
            deliveryStore,
            disposeAsync);
    }

    public async ValueTask DisposeAsync()
    {
        var disposeAsync = Interlocked.Exchange(ref _disposeAsync, null);
        if (disposeAsync is not null)
            await disposeAsync().ConfigureAwait(false);
    }
}
