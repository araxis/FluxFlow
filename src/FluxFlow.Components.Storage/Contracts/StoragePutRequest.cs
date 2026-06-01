namespace FluxFlow.Components.Storage.Contracts;

public sealed record StoragePutRequest
{
    public string? Collection { get; init; }
    public required string Key { get; init; }
    public object? Value { get; init; }
    public string? ContentType { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = [];
    public long? ExpectedVersion { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? CorrelationId { get; init; }
    public StorageWriteMode? Mode { get; init; }
}
