using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Options;
using FluxFlow.Engine.Components;

namespace FluxFlow.Components.Http.Nodes;

public sealed class HttpClientNode : FlowNodeBase, IHttpClientHandle
{
    public HttpClientNode(string clientName, HttpClientOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ArgumentNullException.ThrowIfNull(options);

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
}
