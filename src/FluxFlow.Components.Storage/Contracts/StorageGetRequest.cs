namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageGetRequest
{
    public string? Collection { get; init; }
    public required string Key { get; init; }
    public bool? IncludeExpired { get; init; }
    public string? CorrelationId { get; init; }
}
