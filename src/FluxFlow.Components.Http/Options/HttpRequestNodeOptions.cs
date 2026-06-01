namespace FluxFlow.Components.Http.Options;

public sealed record HttpRequestNodeOptions
{
    public string? BaseUrl { get; init; }
    public Dictionary<string, string> DefaultHeaders { get; init; } = [];
    public int DefaultTimeoutMilliseconds { get; init; } = 100_000;
    public int MaxResponseBodyBytes { get; init; } = 1_048_576;
    public bool FollowRedirects { get; init; } = true;
    public bool TreatNonSuccessStatusAsError { get; init; }
    public int BoundedCapacity { get; init; } = 128;
}
