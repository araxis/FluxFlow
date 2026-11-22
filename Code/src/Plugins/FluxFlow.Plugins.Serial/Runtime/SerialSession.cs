using System.IO.Ports;

namespace FluxFlow.Components.Serial;

/// <summary>Run-owned port session: one reader, serialized writers, and no retry after ambiguous I/O.</summary>
public sealed class SerialSession : IAsyncDisposable
{
    private readonly string _name;
    private readonly Action _open;
    private readonly Func<byte[], int> _read;
    private readonly Action<byte[]> _write;
    private readonly Action _close;
    private readonly SemaphoreSlim _lifecycle = new(1);
    private readonly SemaphoreSlim _writes = new(1);
    private readonly SemaphoreSlim _reads = new(1);
    private SerialPortLease? _lease;
    private bool _opened;
    private volatile bool _disposed;
    private Exception? _failure;
    private int _reader;

    public SerialSession(SerialPort port) : this(port.PortName, port.Open,
        buffer => port.Read(buffer, 0, buffer.Length), bytes => port.Write(bytes, 0, bytes.Length), port.Close)
    {
        if (port.IsOpen || port.ReadTimeout <= 0 || port.WriteTimeout <= 0)
            throw new ArgumentException("Serial sessions require an unopened port with finite positive I/O timeouts.", nameof(port));
    }

    internal SerialSession(string name, Action open, Func<byte[], int> read, Action<byte[]> write, Action close)
    { _name = name; _open = open; _read = read; _write = write; _close = close; }

    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Check();
            if (_opened) return;
            cancellationToken.ThrowIfCancellationRequested();
            _lease = SerialPortLease.Acquire(_name);
            try { _open(); _opened = true; }
            catch (Exception error) { _failure = error; _lease.Dispose(); _lease = null; throw; }
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally { _lifecycle.Release(); }
    }

    internal IDisposable ClaimReader()
    {
        Check();
        if (Interlocked.CompareExchange(ref _reader, 1, 0) != 0)
            throw new InvalidOperationException("A serial session supports one reader. Use workflow links to fan out frames.");
        return new ReaderClaim(this);
    }
    private sealed class ReaderClaim(SerialSession session) : IDisposable
    {
        private SerialSession? _session = session;
        public void Dispose()
        { if (Interlocked.Exchange(ref _session, null) is { } owner) Interlocked.Exchange(ref owner._reader, 0); }
    }

    internal int Read(byte[] buffer, CancellationToken cancellationToken)
    {
        _reads.Wait(cancellationToken);
        try
        {
            Check(); cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var count = _read(buffer);
                if (count == 0) throw new EndOfStreamException("The serial device stopped receiving data.");
                return count;
            }
            catch (TimeoutException) { throw; }
            catch (Exception error) { Interlocked.CompareExchange(ref _failure, error, null); throw; }
        }
        finally { _reads.Release(); }
    }

    public async Task WriteAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        // Copy before awaiting so caller mutations cannot alter an admitted command.
        var snapshot = bytes.ToArray();
        await OpenAsync(cancellationToken).ConfigureAwait(false);
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Check(); cancellationToken.ThrowIfCancellationRequested();
            // Once native I/O starts, wait for its finite timeout; cancellation cannot undo partial transmission.
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { _write(snapshot); }
                catch (Exception error) { Interlocked.CompareExchange(ref _failure, error, null); throw; }
            }, CancellationToken.None).ConfigureAwait(false);
        }
        finally { _writes.Release(); }
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failure is { } failure) throw new IOException("The serial session failed. Start a new run before sending another command.", failure);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            await _writes.WaitAsync().ConfigureAwait(false);
            try
            {
                await _reads.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_opened) { _close(); _opened = false; }
                    // If Close fails, retain ownership and report it; disposal may be retried.
                    _lease?.Dispose(); _lease = null;
                }
                finally { _reads.Release(); }
            }
            finally { _writes.Release(); }
        }
        finally { _lifecycle.Release(); }
    }
}
