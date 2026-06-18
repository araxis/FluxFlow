namespace FluxFlow.Components.Http.Options;

public sealed class HttpComponentOptions
{
    private Func<string?, HttpClient>? _httpClientResolver;
    private TimeProvider _clock = TimeProvider.System;

    public TimeProvider Clock => _clock;

    /// <summary>
    /// Supplies the single <see cref="HttpClient"/> every http.client node uses.
    /// The host owns the client (and all of its transport policy: base address,
    /// pooling, redirects, default headers, TLS, any allow-list/SSRF delegating
    /// handler). The node never disposes it.
    /// </summary>
    public HttpComponentOptions UseHttpClient(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _httpClientResolver = _ => client;
        return this;
    }

    /// <summary>
    /// Supplies a resolver invoked per node with the node's optional <c>client</c>
    /// name, so different nodes can use different clients (for example bridging to
    /// <c>IHttpClientFactory.CreateClient(name)</c>).
    /// </summary>
    public HttpComponentOptions UseHttpClient(Func<string?, HttpClient> resolver)
    {
        _httpClientResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        return this;
    }

    public HttpComponentOptions UseClock(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }

    internal HttpClient ResolveHttpClient(string? name)
    {
        if (_httpClientResolver is null)
        {
            throw new InvalidOperationException(
                "No HttpClient is configured. Call UseHttpClient(...) when registering the HTTP components.");
        }

        return _httpClientResolver(name)
            ?? throw new InvalidOperationException(
                $"The configured HttpClient resolver returned null for client '{name ?? "(default)"}'.");
    }
}
