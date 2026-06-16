namespace FluxFlow.Components.Http.Options;

public sealed record HttpRequestNodeOptions
{
    public string? Client { get; init; }
    public int MaxResponseBodyBytes { get; init; } = 1_048_576;
    public bool TreatNonSuccessStatusAsError { get; init; }
    public int BoundedCapacity { get; init; } = 128;
}
