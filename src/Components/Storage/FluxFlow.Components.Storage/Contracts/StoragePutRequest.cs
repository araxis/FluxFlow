namespace FluxFlow.Components.Storage.Contracts;

public sealed record StoragePutRequest
{
    private string? _collection;
    private string? _contentType;
    private IReadOnlyDictionary<string, string> _attributes =
        StorageContractNormalization.CopyAttributes(null);
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

    public IReadOnlyDictionary<string, string> Attributes
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
