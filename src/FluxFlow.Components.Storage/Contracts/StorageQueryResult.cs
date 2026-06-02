namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageQueryResult
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Operation { get; init; }
    public required string Collection { get; init; }
    public required bool Succeeded { get; init; }
    public required int Count { get; init; }
    public IReadOnlyList<StorageRecord> Records { get; init; } = [];
    public string? CorrelationId { get; init; }
    public string? Message { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = [];
}
