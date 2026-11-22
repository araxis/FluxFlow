using NATS.Client.Core;

namespace FluxFlow.Components.Nats;

/// <summary>One run-owned official client; components own and dispose their subscriptions.</summary>
public sealed class NatsSession(NatsOpts options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NatsConnection? _connection;
    private bool _disposed;
    private readonly CancellationTokenSource _failure = new();
    private readonly CancellationTokenSource _monitorStop = new();
    private Task? _monitor;
    private Exception? _error;
    internal CancellationToken FailureToken => _failure.Token;
    internal void ThrowIfFailed()
    {
        if (Volatile.Read(ref _error) is { } error)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    public async Task<NatsConnection> OpenAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThrowIfFailed();
            if (_connection is not null) return _connection;
            token.ThrowIfCancellationRequested();
            var connection = new NatsConnection(options);
            connection.ServerError += ServerError;
            var connect = connection.ConnectAsync().AsTask();
            try { await connect.WaitAsync(token).ConfigureAwait(false); token.ThrowIfCancellationRequested(); }
            catch (Exception original)
            {
                try { await connection.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanup) { throw new AggregateException("NATS startup and cleanup failed.", original, cleanup); }
                finally { try { await connect.ConfigureAwait(false); } catch { /* Observe the cancelled startup task. */ } }
                throw;
            }
            _connection = connection;
            _monitor = MonitorAsync(connection);
            return connection;
        }
        finally { _gate.Release(); }
    }
    private ValueTask ServerError(object? sender, NatsServerErrorEventArgs args)
    {
        // Permission errors arrive asynchronously and cannot safely be attributed to one concurrent command.
        // Fail the shared session rather than continue reporting successful operations on rejected subjects.
        if (args.Kind != NatsServerErrorKind.StaleConnection) Fail(new NatsException(args.Error));
        return ValueTask.CompletedTask;
    }
    private void Fail(Exception error)
    {
        if (Interlocked.CompareExchange(ref _error, error, null) is null) _failure.Cancel();
    }
    private async Task MonitorAsync(NatsConnection connection)
    {
        // The client exposes terminal state, but no public terminal-failure event.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_monitorStop.Token).ConfigureAwait(false))
                if (connection.ConnectionState == NatsConnectionState.Failed)
                {
                    Fail(new NatsConnectionFailedException("The NATS connection exhausted its reconnect attempts."));
                    return;
                }
        }
        catch (OperationCanceledException) when (_monitorStop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            _monitorStop.Cancel();
            if (_monitor is not null) await _monitor.ConfigureAwait(false);
            if (_connection is { } connection)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                connection.ServerError -= ServerError;
                _connection = null;
            }
        }
        finally { _gate.Release(); }
    }
}
