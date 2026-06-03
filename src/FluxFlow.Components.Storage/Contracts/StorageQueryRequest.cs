namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageQueryRequest
{
    public string? Collection { get; init; }
    public string? KeyPrefix { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = [];
    public DateTimeOffset? StoredFrom { get; init; }
    public DateTimeOffset? StoredTo { get; init; }
    public bool? IncludeExpired { get; init; }
    public int? Offset { get; init; }
    public int? Limit { get; init; }
    public string? CorrelationId { get; init; }
}
