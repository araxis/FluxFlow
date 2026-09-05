namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageQueryOutcome
{
    private string _collection = string.Empty;
    private IReadOnlyList<StorageContentRecord> _records =
        Array.Empty<StorageContentRecord>();

    public required string Collection
    {
        get => _collection;
        init => _collection = StorageContractNormalization.NormalizeRequired(value);
    }

    public required int Count { get; init; }

    public IReadOnlyList<StorageContentRecord> Records
    {
        get => _records;
        init => _records = StorageContentContractMap.CopyRecords(value);
    }
}
