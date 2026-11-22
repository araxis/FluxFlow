namespace FluxFlow.Configuration.Components.Http;

/// <summary>Settings for a shared native System.Net.Http.HttpClient resource.</summary>
public sealed class HttpClientConfiguration
{
    public string? BaseAddress { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(100);
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HttpAuthenticationConfiguration? Authentication { get; init; }
    /// <summary>Omit or set MaxRetryAttempts to zero to disable retries.</summary>
    public HttpRetryConfiguration? Retry { get; init; }
    public long MaxResponseContentBufferSize { get; init; } = 10 * 1024 * 1024;
    public TimeSpan PooledConnectionLifetime { get; init; } = TimeSpan.FromMinutes(2);
    public bool AllowAutoRedirect { get; init; }

    public static Uri? ParseBaseAddress(string? address)
    {
        if (address is null) return null;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("BaseAddress must be an absolute HTTP(S) address without embedded credentials or a fragment.");
        return uri;
    }
}
