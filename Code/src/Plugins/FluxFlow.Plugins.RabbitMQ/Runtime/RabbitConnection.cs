using RabbitMQ.Client;

namespace FluxFlow.Components.RabbitMQ;

/// <summary>One run-owned connection; components own their individual channels.</summary>
public sealed class RabbitConnection(ConnectionFactory factory) : IAsyncDisposable
{
    public bool AutomaticRecovery => factory.AutomaticRecoveryEnabled;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private bool _disposed;
    public async Task<IChannel> OpenChannelAsync(bool confirms, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _connection ??= await factory.CreateConnectionAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return await _connection.CreateChannelAsync(new CreateChannelOptions(confirms, confirms), token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            if (_connection is not null) await _connection.DisposeAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
