namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageResult
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Operation { get; init; }
    public required string Collection { get; init; }
    public required string Key { get; init; }
    public required bool Succeeded { get; init; }
    public bool Found { get; init; }
    public bool Deleted { get; init; }
    public StorageRecord? Record { get; init; }
    public long? Version { get; init; }
    public string? Message { get; init; }
    public string? CorrelationId { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = [];
}
