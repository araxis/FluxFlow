namespace FluxFlow.Components.Http.Options;

public sealed record HttpClientNodeOptions
{
    public static readonly HttpClientNodeOptions Default = new();

    public int BoundedCapacity { get; init; } = 128;

    public int MaxResponseBodyBytes { get; init; } = 1_048_576;

    public bool TreatNonSuccessStatusAsError { get; init; }

    public int MaxDegreeOfParallelism { get; init; } = 1;

    /// <summary>
    /// Per-request timeout used when the request input does not specify one. Null
    /// defers entirely to the injected HttpClient's own timeout.
    /// </summary>
    public int? DefaultTimeoutMilliseconds { get; init; }
}
