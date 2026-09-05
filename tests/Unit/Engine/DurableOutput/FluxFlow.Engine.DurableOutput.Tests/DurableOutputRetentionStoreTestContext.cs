namespace FluxFlow.Engine.DurableOutput.Tests;

/// <summary>
/// Owns the narrow capabilities required by the reusable output-retention suite.
/// </summary>
public sealed class DurableOutputRetentionStoreTestContext : IAsyncDisposable
{
    private Func<ValueTask>? _disposeAsync;

    private DurableOutputRetentionStoreTestContext(
        IDurableOutputStore captureStore,
        IDurableOutputDeliveryStore deliveryStore,
        IDurableOutputDeadLetterStore deadLetters,
        IDurableOutputStatusStore status,
        IDurableOutputRetentionStore retention,
        Func<ValueTask> disposeAsync)
    {
        CaptureStore = captureStore;
        DeliveryStore = deliveryStore;
        DeadLetters = deadLetters;
        Status = status;
        Retention = retention;
        _disposeAsync = disposeAsync;
    }

    public IDurableOutputStore CaptureStore { get; }

    public IDurableOutputDeliveryStore DeliveryStore { get; }

    public IDurableOutputDeadLetterStore DeadLetters { get; }

    public IDurableOutputStatusStore Status { get; }

    public IDurableOutputRetentionStore Retention { get; }

    public static DurableOutputRetentionStoreTestContext Create(
        IDurableOutputStore captureStore,
        IDurableOutputDeliveryStore deliveryStore,
        IDurableOutputDeadLetterStore deadLetters,
        IDurableOutputStatusStore status,
        IDurableOutputRetentionStore retention,
        Func<ValueTask> disposeAsync)
    {
        ArgumentNullException.ThrowIfNull(captureStore);
        ArgumentNullException.ThrowIfNull(deliveryStore);
        ArgumentNullException.ThrowIfNull(deadLetters);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(retention);
        ArgumentNullException.ThrowIfNull(disposeAsync);
        return new DurableOutputRetentionStoreTestContext(
            captureStore,
            deliveryStore,
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
