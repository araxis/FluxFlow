namespace FluxFlow.Components.Storage.Contracts;

public sealed record StoragePutRequest
{
    private string? _collection;
    private string? _contentType;
    private Dictionary<string, string> _attributes = new(StringComparer.Ordinal);
    private string? _correlationId;

    public string? Collection
    {
        get => _collection;
        init => _collection = StorageContractNormalization.NormalizeOptional(value);
    }

    public required string Key { get; init; }
    public object? Value { get; init; }

    public string? ContentType
    {
        get => _contentType;
        init => _contentType = StorageContractNormalization.NormalizeOptional(value);
    }

    public Dictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = StorageContractNormalization.CopyAttributes(value);
    }

    public long? ExpectedVersion { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    public string? CorrelationId
    {
        get => _correlationId;
        init => _correlationId = StorageContractNormalization.NormalizeOptional(value);
    }

    public StorageWriteMode? Mode { get; init; }
}
