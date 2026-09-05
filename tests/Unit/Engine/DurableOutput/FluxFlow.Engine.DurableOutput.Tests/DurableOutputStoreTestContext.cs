namespace FluxFlow.Engine.DurableOutput.Tests;

/// <summary>
/// Owns one store and a provider-observable exact-key reader used by the
/// reusable durable-output conformance suite.
/// </summary>
public sealed class DurableOutputStoreTestContext : IAsyncDisposable
{
    private readonly Func<DurableOutputKey, CancellationToken, ValueTask<DurableOutputEnvelope?>>
        _readAsync;
    private Func<ValueTask>? _disposeAsync;

    private DurableOutputStoreTestContext(
        IDurableOutputStore store,
        Func<DurableOutputKey, CancellationToken, ValueTask<DurableOutputEnvelope?>> readAsync,
        Func<ValueTask>? disposeAsync)
    {
        Store = store;
        _readAsync = readAsync;
        _disposeAsync = disposeAsync;
    }

    public IDurableOutputStore Store { get; }

    public static DurableOutputStoreTestContext Create(
        IDurableOutputStore store,
        Func<DurableOutputKey, CancellationToken, ValueTask<DurableOutputEnvelope?>> readAsync,
        Func<ValueTask>? disposeAsync = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(readAsync);
        return new DurableOutputStoreTestContext(store, readAsync, disposeAsync);
    }

    public ValueTask<DurableOutputEnvelope?> ReadAsync(
        DurableOutputKey key,
        CancellationToken cancellationToken = default)
        => _readAsync(key, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        var disposeAsync = Interlocked.Exchange(ref _disposeAsync, null);
        if (disposeAsync is not null)
            await disposeAsync().ConfigureAwait(false);
    }
}
