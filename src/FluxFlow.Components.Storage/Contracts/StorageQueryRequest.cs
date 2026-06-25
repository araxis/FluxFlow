namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageQueryRequest
{
    private string? _collection;
    private string? _keyPrefix;
    private Dictionary<string, string> _attributes = new(StringComparer.Ordinal);
    private string? _correlationId;

    public string? Collection
    {
        get => _collection;
        init => _collection = StorageContractNormalization.NormalizeOptional(value);
    }

    public string? KeyPrefix
    {
        get => _keyPrefix;
        init => _keyPrefix = StorageContractNormalization.NormalizeOptional(value);
    }

    public Dictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = StorageContractNormalization.CopyAttributes(value);
    }

    public DateTimeOffset? StoredFrom { get; init; }
    public DateTimeOffset? StoredTo { get; init; }
    public bool? IncludeExpired { get; init; }
    public int? Offset { get; init; }
    public int? Limit { get; init; }

    public string? CorrelationId
    {
        get => _correlationId;
        init => _correlationId = StorageContractNormalization.NormalizeOptional(value);
    }
}
