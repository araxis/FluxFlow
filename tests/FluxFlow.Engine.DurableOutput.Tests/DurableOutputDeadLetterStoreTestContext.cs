namespace FluxFlow.Engine.DurableOutput.Tests;

/// <summary>
/// Owns the narrow capture, delivery, and dead-letter capabilities required by
/// the reusable dead-letter-store conformance suite.
/// </summary>
public sealed class DurableOutputDeadLetterStoreTestContext : IAsyncDisposable
{
    private Func<ValueTask>? _disposeAsync;

    private DurableOutputDeadLetterStoreTestContext(
        IDurableOutputStore captureStore,
        IDurableOutputDeliveryStore deliveryStore,
        IDurableOutputDeadLetterStore deadLetterStore,
        Func<ValueTask> disposeAsync)
    {
        CaptureStore = captureStore;
        DeliveryStore = deliveryStore;
        DeadLetterStore = deadLetterStore;
        _disposeAsync = disposeAsync;
    }

    public IDurableOutputStore CaptureStore { get; }

    public IDurableOutputDeliveryStore DeliveryStore { get; }

    public IDurableOutputDeadLetterStore DeadLetterStore { get; }

    public static DurableOutputDeadLetterStoreTestContext Create(
        IDurableOutputStore captureStore,
        IDurableOutputDeliveryStore deliveryStore,
        IDurableOutputDeadLetterStore deadLetterStore,
        Func<ValueTask> disposeAsync)
    {
        ArgumentNullException.ThrowIfNull(captureStore);
        ArgumentNullException.ThrowIfNull(deliveryStore);
        ArgumentNullException.ThrowIfNull(deadLetterStore);
        ArgumentNullException.ThrowIfNull(disposeAsync);
        return new DurableOutputDeadLetterStoreTestContext(
            captureStore,
            deliveryStore,
            deadLetterStore,
            disposeAsync);
    }

    public async ValueTask DisposeAsync()
    {
        var disposeAsync = Interlocked.Exchange(ref _disposeAsync, null);
        if (disposeAsync is not null)
            await disposeAsync().ConfigureAwait(false);
    }
}
