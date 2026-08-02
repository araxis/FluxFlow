namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageDeleteOutcome
{
    private string _collection = string.Empty;
    private string _key = string.Empty;

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

    public required bool Deleted { get; init; }

    public long? Version { get; init; }
}
