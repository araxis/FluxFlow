namespace FluxFlow.Engine.Scenarios;

public sealed class ScenarioRunLifetime : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<Func<ValueTask>> _cleanupActions = [];
    private bool _disposed;

    public void Register(IAsyncDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        Register(resource.DisposeAsync);
    }

    public void Register(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        Register(() =>
        {
            resource.Dispose();
            return ValueTask.CompletedTask;
        });
    }

    public void Register(Func<ValueTask> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _cleanupActions.Add(cleanup);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Func<ValueTask>[] cleanupActions;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cleanupActions = _cleanupActions.ToArray();
            _cleanupActions.Clear();
        }

        List<Exception>? exceptions = null;
        for (var index = cleanupActions.Length - 1; index >= 0; index--)
        {
            try
            {
                await cleanupActions[index]().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (exceptions ??= []).Add(exception);
            }
        }

        if (exceptions is null)
        {
            return;
        }

        if (exceptions.Count == 1)
        {
            throw exceptions[0];
        }

        throw new AggregateException("Scenario run cleanup failed.", exceptions);
    }
}
