using System.Diagnostics.CodeAnalysis;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Http.Nodes;

/// <summary>
/// Owns the HTTP client lifecycle. Establishing and tearing down the shared
/// request sender is an explicit, host-API-only decision: there is no
/// auto-connect, no lazy connect, and no in-graph command port. Request nodes
/// borrow the established sender via <see cref="TryGetSender"/> at call-time
/// only and never connect or dispose it.
/// </summary>
public sealed class HttpClientNode : FlowNodeBase, IHttpClientHandle, IAsyncDisposable
{
    private readonly NodeAddress _address;
    private readonly IHttpRequestSenderFactory _senderFactory;
    private readonly TimeProvider _clock;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifecycleCancellation = new();

    private volatile IHttpRequestSender? _sender;
    private volatile HttpClientConnectionState _state = HttpClientConnectionState.Disconnected;

    private Task<IHttpRequestSender>? _inFlightConnect;
    private CancellationTokenSource? _connectCts;
    private bool _userDisconnected;
    private bool _disposed;

    internal HttpClientNode(
        NodeAddress address,
        string clientName,
        HttpClientOptions options,
        IHttpRequestSenderFactory senderFactory,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(senderFactory);
        ArgumentNullException.ThrowIfNull(clock);

        _address = address;
        _senderFactory = senderFactory;
        _clock = clock;

        ClientName = clientName;
        BaseUrl = options.BaseUrl;
        AllowedHosts = options.AllowedHosts;
        RestrictToBaseUrlOrigin = options.RestrictToBaseUrlOrigin;
        FollowRedirects = options.FollowRedirects;
        DefaultTimeoutMilliseconds = options.DefaultTimeoutMilliseconds;
        PooledConnectionLifetimeSeconds = options.PooledConnectionLifetimeSeconds;
        MaxConnectionsPerServer = options.MaxConnectionsPerServer;
        DefaultHeaders = options.DefaultHeaders;
    }

    public string ClientName { get; }

    public string? BaseUrl { get; }

    public IReadOnlyList<string> AllowedHosts { get; }

    public bool RestrictToBaseUrlOrigin { get; }

    public bool FollowRedirects { get; }

    public int DefaultTimeoutMilliseconds { get; }

    public int? PooledConnectionLifetimeSeconds { get; }

    public int? MaxConnectionsPerServer { get; }

    public IReadOnlyDictionary<string, string> DefaultHeaders { get; }

    public HttpClientConnectionState State => _state;

    // StartAsync stays a no-op: connecting is an explicit host decision.

    public bool TryGetSender([NotNullWhen(true)] out IHttpRequestSender? sender)
    {
        // Lock-free borrow. Read state first, then the sender, so a concurrent
        // disconnect (which clears the sender before flipping state) can only ever
        // cause a false negative, never a borrow of a torn-down sender.
        if (_state == HttpClientConnectionState.Connected)
        {
            var current = _sender;
            if (current is not null)
            {
                sender = current;
                return true;
            }
        }

        sender = null;
        return false;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // Idempotent fast path: already connected.
        if (_state == HttpClientConnectionState.Connected)
        {
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        Task<IHttpRequestSender> connect;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_state == HttpClientConnectionState.Connected)
            {
                return;
            }

            // Single-flight: if a connect is already running, capture it, release
            // the gate, and await it instead of building a second sender.
            if (_inFlightConnect is not null)
            {
                connect = _inFlightConnect;
            }
            else
            {
                _userDisconnected = false;

                // Dispose the previous connect CTS before creating a new one so a
                // sequence of connects cannot leak cancellation sources.
                _connectCts?.Dispose();
                _connectCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _lifecycleCancellation.Token);

                _state = HttpClientConnectionState.Connecting;
                connect = EstablishAsync(_connectCts.Token);
                _inFlightConnect = connect;
            }
        }
        finally
        {
            _gate.Release();
        }

        await connect.ConfigureAwait(false);
    }

    private async Task<IHttpRequestSender> EstablishAsync(CancellationToken ct)
    {
        // Yield off the gate-holding caller so the in-flight Task is observable to a
        // concurrent ConnectAsync before the sender build runs.
        await Task.Yield();

        IHttpRequestSender? sender = null;
        try
        {
            ct.ThrowIfCancellationRequested();

            var context = new HttpClientSenderContext
            {
                Address = _address,
                Client = this,
                Clock = _clock
            };

            sender = _senderFactory.CreateClient(context);
            ArgumentNullException.ThrowIfNull(sender);

            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                // If a disconnect or dispose won the race while we were building,
                // honor it: drop the freshly built sender and stay not-connected.
                if (_userDisconnected || _disposed || ct.IsCancellationRequested)
                {
                    await sender.DisposeAsync().ConfigureAwait(false);
                    return sender;
                }

                // Publish order: sender FIRST, then flip state to Connected LAST so
                // a borrow that observes Connected always sees a non-null sender.
                _sender = sender;
                _state = HttpClientConnectionState.Connected;
                _inFlightConnect = null;
            }
            finally
            {
                _gate.Release();
            }

            return sender;
        }
        catch (Exception exception)
        {
            // Cancellation here is a requested disconnect/dispose, not a fault: do
            // not clobber the Disconnected state.
            var cancelled = exception is OperationCanceledException &&
                (ct.IsCancellationRequested || _lifecycleCancellation.IsCancellationRequested);

            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                _inFlightConnect = null;

                // Never leave a half-built sender behind.
                if (sender is not null)
                {
                    await sender.DisposeAsync().ConfigureAwait(false);
                }

                if (!cancelled && !_userDisconnected && !_disposed)
                {
                    _sender = null;
                    _state = HttpClientConnectionState.Faulted;
                }
            }
            finally
            {
                _gate.Release();
            }

            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        IHttpRequestSender? sender;
        try
        {
            _userDisconnected = true;
            _connectCts?.Cancel();
            _inFlightConnect = null;

            // Clear the sender FIRST so borrows immediately observe not-connected,
            // and flip state so TryGetSender returns false even before teardown.
            sender = Interlocked.Exchange(ref _sender, null);
            _state = HttpClientConnectionState.Disconnected;
        }
        finally
        {
            _gate.Release();
        }

        // Dispose the old sender (it disposes the HttpClient/handler) AFTER the
        // borrow-visible state has been cleared.
        if (sender is not null)
        {
            await sender.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            // Shared idempotent teardown core; resources dispose LAST in the runtime.
            await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The gate may be disposed by a concurrent dispose path; tolerate it.
        }

        _lifecycleCancellation.Cancel();
        _lifecycleCancellation.Dispose();
        _connectCts?.Dispose();

        try
        {
            _gate.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        CompleteNode();
    }

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _lifecycleCancellation.Cancel();
        FaultNode(exception);
    }
}
