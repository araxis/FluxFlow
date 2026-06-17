using FluxFlow.Components.Http.Contracts;

namespace FluxFlow.Components.Http.Tests;

/// <summary>
/// In-memory sender double. Records every <see cref="SendAsync"/> call and hands
/// back a canned response so http.request round-trips deterministically without
/// real sockets. Counts disposals so tests can assert the http.client node
/// tears the sender down exactly once on disconnect/dispose.
/// </summary>
internal sealed class InMemoryHttpRequestSender : IHttpRequestSender, IHttpRedirectPolicy
{
    private readonly List<HttpRequestSendContext> _sent = [];
    private readonly Func<HttpRequestSendContext, HttpResponseOutput> _respond;
    private int _disposeCalls;

    public InMemoryHttpRequestSender(
        bool allowAutoRedirect,
        Func<HttpRequestSendContext, HttpResponseOutput>? respond = null)
    {
        AllowAutoRedirect = allowAutoRedirect;
        _respond = respond ?? DefaultResponse;
    }

    public bool AllowAutoRedirect { get; }

    public int DisposeCalls => Volatile.Read(ref _disposeCalls);

    public IReadOnlyList<HttpRequestSendContext> Sent
    {
        get
        {
            lock (_sent)
            {
                return _sent.ToArray();
            }
        }
    }

    public Task<HttpResponseOutput> SendAsync(
        HttpRequestSendContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sent)
        {
            _sent.Add(context);
        }

        return Task.FromResult(_respond(context));
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCalls);
        return ValueTask.CompletedTask;
    }

    private static HttpResponseOutput DefaultResponse(HttpRequestSendContext context)
        => new()
        {
            Timestamp = default,
            Method = context.Method,
            Url = context.Url.ToString(),
            StatusCode = 200,
            ReasonPhrase = "OK",
            Headers = [],
            BodyBytes = [],
            Body = string.Empty,
            ContentType = "text/plain",
            ElapsedMilliseconds = 0,
            Success = true,
            BodyTruncated = false
        };
}

/// <summary>
/// Client-scoped sender factory double. Builds <see cref="InMemoryHttpRequestSender"/>
/// instances from the http.client handle on the <see cref="HttpClientSenderContext"/>,
/// records each <see cref="CreateClient"/> call, and optionally gates the build so
/// concurrent ConnectAsync calls can be observed sharing a single in-flight build.
/// </summary>
internal sealed class RecordingHttpRequestSenderFactory : IHttpRequestSenderFactory
{
    private readonly object _gate = new();
    private readonly List<HttpClientSenderContext> _clientContexts = [];
    private readonly List<InMemoryHttpRequestSender> _senders = [];
    private readonly Func<HttpRequestSendContext, HttpResponseOutput>? _respond;
    private readonly Task? _buildGate;
    private int _createClientCalls;

    public RecordingHttpRequestSenderFactory(
        Func<HttpRequestSendContext, HttpResponseOutput>? respond = null,
        Task? buildGate = null)
    {
        _respond = respond;
        _buildGate = buildGate;
    }

    public int CreateClientCalls => Volatile.Read(ref _createClientCalls);

    public IReadOnlyList<InMemoryHttpRequestSender> Senders
    {
        get
        {
            lock (_gate)
            {
                return _senders.ToArray();
            }
        }
    }

    public InMemoryHttpRequestSender LastSender
    {
        get
        {
            lock (_gate)
            {
                return _senders[^1];
            }
        }
    }

    public IReadOnlyList<HttpClientSenderContext> ClientContexts
    {
        get
        {
            lock (_gate)
            {
                return _clientContexts.ToArray();
            }
        }
    }

    public IHttpRequestSender Create(HttpRequestSenderContext context)
        => throw new InvalidOperationException(
            "The http.client node builds client-scoped senders; Create(request) must not be called.");

    public IHttpRequestSender CreateClient(HttpClientSenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Client);

        Interlocked.Increment(ref _createClientCalls);

        // Honor an optional build gate so the single-flight test can hold two
        // concurrent ConnectAsync calls inside one in-flight build deterministically.
        _buildGate?.GetAwaiter().GetResult();

        // Mirror the production guard decision so the connected sender's redirect
        // policy reflects the handle's allow-list/origin configuration.
        var allowAutoRedirect = context.Client.FollowRedirects &&
            !(context.Client.RestrictToBaseUrlOrigin || context.Client.AllowedHosts.Count > 0);

        var sender = new InMemoryHttpRequestSender(allowAutoRedirect, _respond);
        lock (_gate)
        {
            _clientContexts.Add(context);
            _senders.Add(sender);
        }

        return sender;
    }
}
