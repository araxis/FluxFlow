namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageDeleteRequest
{
    public string? Collection { get; init; }
    public required string Key { get; init; }
    public string? CorrelationId { get; init; }
}
