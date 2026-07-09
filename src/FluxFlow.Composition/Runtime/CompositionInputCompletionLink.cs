namespace FluxFlow.Composition;

internal sealed class CompositionInputCompletionLink : IDisposable
{
    private readonly CancellationTokenSource _disposed = new();
    private readonly Task[] _watchers;
    private int _remaining;
    private int _finished;
    private int _disposeStarted;

    public CompositionInputCompletionLink(
        CompositionInputPort input,
        IReadOnlyCollection<CompositionOutputPort> upstreams)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(upstreams);
        if (upstreams.Count == 0)
            throw new ArgumentException("At least one upstream output is required.", nameof(upstreams));

        _remaining = upstreams.Count;
        _watchers = upstreams
            .Select(upstream => WatchSourceCompletionAsync(input, upstream.Completion, _disposed.Token))
            .ToArray();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        _disposed.Cancel();
        _ = DisposeTokenWhenWatchersStopAsync();
    }

    private async Task DisposeTokenWhenWatchersStopAsync()
    {
        try
        {
            await Task.WhenAll(_watchers).ConfigureAwait(false);
        }
        catch
        {
            // Watchers only observe completion so the token source can be released.
        }
        finally
        {
            _disposed.Dispose();
        }
    }

    private async Task WatchSourceCompletionAsync(
        CompositionInputPort input,
        Task sourceCompletion,
        CancellationToken cancellationToken)
    {
        try
        {
            await sourceCompletion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            if (Volatile.Read(ref _disposeStarted) != 0)
                return;

            if (Interlocked.Exchange(ref _finished, 1) == 0)
                input.Fault(exception);

            return;
        }

        if (Interlocked.Decrement(ref _remaining) == 0
            && Interlocked.Exchange(ref _finished, 1) == 0)
        {
            input.Complete();
        }
    }
}
