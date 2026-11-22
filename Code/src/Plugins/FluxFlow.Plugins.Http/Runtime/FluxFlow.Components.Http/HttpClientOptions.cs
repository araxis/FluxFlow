namespace FluxFlow.Components.Http;

public sealed class HttpClientOptions
{
    /// <summary>The shared client is owned by the host, not the component.</summary>
    public required System.Net.Http.HttpClient Client { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
