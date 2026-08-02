namespace FluxFlow.Components.Storage.Contracts;

public sealed record StoragePutOutcome
{
    private string _collection = string.Empty;
    private string _key = string.Empty;
    private StorageContentRecord? _record;

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

    public required long Version { get; init; }

    public required DateTimeOffset StoredAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public StorageContentRecord? Record
    {
        get => _record;
        init => _record = value is null ? null : StorageContentContractMap.CopyRecord(value);
    }
}
