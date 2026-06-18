namespace FluxFlow.Components.Http.Options;

public sealed record HttpClientNodeOptions
{
    /// <summary>
    /// Optional name passed to the host's HttpClient resolver (for example an
    /// <c>IHttpClientFactory</c> client name). Null/empty selects the default.
    /// </summary>
    public string? Client { get; init; }

    public int BoundedCapacity { get; init; } = 128;

    public int MaxResponseBodyBytes { get; init; } = 1_048_576;

    public bool TreatNonSuccessStatusAsError { get; init; }

    public int MaxDegreeOfParallelism { get; init; } = 1;

    /// <summary>
    /// Per-request timeout applied when the request input does not specify one.
    /// Null defers entirely to the injected HttpClient's own timeout.
    /// </summary>
    public int? DefaultTimeoutMilliseconds { get; init; }
}
