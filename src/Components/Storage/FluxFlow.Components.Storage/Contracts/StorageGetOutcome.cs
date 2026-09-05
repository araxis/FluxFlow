namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageGetOutcome
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

    public required bool Found { get; init; }

    public StorageContentRecord? Record
    {
        get => _record;
        init => _record = value is null ? null : StorageContentContractMap.CopyRecord(value);
    }
}
