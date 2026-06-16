namespace FluxFlow.Components.Http.Contracts;

public interface IHttpClientHandle
{
    string ClientName { get; }
    string? BaseUrl { get; }
    IReadOnlyList<string> AllowedHosts { get; }
    bool RestrictToBaseUrlOrigin { get; }
    bool FollowRedirects { get; }
    int DefaultTimeoutMilliseconds { get; }
    int? PooledConnectionLifetimeSeconds { get; }
    int? MaxConnectionsPerServer { get; }
    IReadOnlyDictionary<string, string> DefaultHeaders { get; }
}
