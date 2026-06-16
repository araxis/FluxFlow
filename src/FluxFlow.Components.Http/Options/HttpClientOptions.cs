namespace FluxFlow.Components.Http.Options;

public sealed record HttpClientOptions
{
    public string? BaseUrl { get; init; }
    public List<string> AllowedHosts { get; init; } = [];
    public bool RestrictToBaseUrlOrigin { get; init; }
    public bool FollowRedirects { get; init; } = true;
    public int DefaultTimeoutMilliseconds { get; init; } = 100_000;
    public int? PooledConnectionLifetimeSeconds { get; init; }
    public int? MaxConnectionsPerServer { get; init; }
    public Dictionary<string, string> DefaultHeaders { get; init; } = [];
}
