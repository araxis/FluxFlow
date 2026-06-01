namespace FluxFlow.Components.Serialization.Contracts;

public sealed record JsonStringifyRequest
{
    public object? Value { get; init; }
    public bool? WriteIndented { get; init; }
    public string? Encoding { get; init; }
}
