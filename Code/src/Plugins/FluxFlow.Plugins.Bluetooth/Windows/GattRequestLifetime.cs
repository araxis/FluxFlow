namespace FluxFlow.Components.Bluetooth.Windows;

// Native deferrals and notifications must finish before their service is released.
internal sealed class GattRequestLifetime(int capacity, CancellationToken stopping)
{
    private readonly object _gate = new();
    private readonly HashSet<Task> _pending = [];
    private bool _closed;
    public TaskCompletionSource<Exception> Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task RunAsync(Func<Task> operation)
    {
        lock (_gate)
        {
            if (_closed || stopping.IsCancellationRequested) throw new OperationCanceledException(stopping);
            // A request can finish before its removal continuation runs.
            if (_pending.Count >= capacity) _pending.RemoveWhere(static task => task.IsCompleted);
            if (_pending.Count >= capacity)
            {
                var error = new InvalidOperationException("The bounded GATT request buffer is full.");
                Failed.TrySetResult(error);
                throw error;
            }
            var task = Task.Run(async () =>
            {
                try { await operation().ConfigureAwait(false); }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
                catch (Exception error) { Failed.TrySetResult(error); throw; }
            });
            _pending.Add(task);
            _ = task.ContinueWith(done => { _ = done.Exception; lock (_gate) _pending.Remove(done); }, TaskScheduler.Default);
            return task;
        }
    }
    public Task CloseAsync()
    {
        lock (_gate) { _closed = true; return Task.WhenAll(_pending.ToArray()); }
    }
}
