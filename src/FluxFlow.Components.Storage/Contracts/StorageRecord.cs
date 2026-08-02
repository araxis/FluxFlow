namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageRecord
{
    private string _collection = string.Empty;
    private string _key = string.Empty;
    private string? _contentType;
    private IReadOnlyDictionary<string, string> _attributes =
        StorageContractNormalization.CopyAttributes(null);
    private string? _correlationId;

    public required string Collection
    {
        get => _collection;
        init => _collection = StorageContractNormalization.NormalizeRequired(value);
    }

    public required string Key
    {
        get => _key;
        init => _key = StorageContractNormalization.NormalizeRequired(value);
    }

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

    public long Version { get; init; }
    public DateTimeOffset StoredAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    public string? CorrelationId
    {
        get => _correlationId;
        init => _correlationId = StorageContractNormalization.NormalizeOptional(value);
    }
}
