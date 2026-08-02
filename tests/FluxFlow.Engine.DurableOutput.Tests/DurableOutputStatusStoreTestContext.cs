namespace FluxFlow.Engine.DurableOutput.Tests;

/// <summary>
/// Owns one provider's capture, delivery, and optional status capabilities for
/// the reusable durable-output status conformance suite.
/// </summary>
public sealed class DurableOutputStatusStoreTestContext : IAsyncDisposable
{
    private Func<ValueTask>? _disposeAsync;

    private DurableOutputStatusStoreTestContext(
        IDurableOutputStore captureStore,
        IDurableOutputDeliveryStore deliveryStore,
        IDurableOutputStatusStore statusStore,
        Func<ValueTask>? disposeAsync)
    {
        CaptureStore = captureStore;
        DeliveryStore = deliveryStore;
        StatusStore = statusStore;
        _disposeAsync = disposeAsync;
    }

    public IDurableOutputStore CaptureStore { get; }

    public IDurableOutputDeliveryStore DeliveryStore { get; }

    public IDurableOutputStatusStore StatusStore { get; }

    public static DurableOutputStatusStoreTestContext Create(
        IDurableOutputStore captureStore,
        IDurableOutputDeliveryStore deliveryStore,
        IDurableOutputStatusStore statusStore,
        Func<ValueTask>? disposeAsync = null)
    {
        ArgumentNullException.ThrowIfNull(captureStore);
        ArgumentNullException.ThrowIfNull(deliveryStore);
        ArgumentNullException.ThrowIfNull(statusStore);
        return new DurableOutputStatusStoreTestContext(
            captureStore,
            deliveryStore,
            statusStore,
            disposeAsync);
    }

    public async ValueTask DisposeAsync()
    {
        var disposeAsync = Interlocked.Exchange(ref _disposeAsync, null);
        if (disposeAsync is not null)
            await disposeAsync().ConfigureAwait(false);
    }
}
