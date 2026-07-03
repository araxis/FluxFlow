namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageGetRequest
{
    private string? _collection;
    private string? _correlationId;

    public string? Collection
    {
        get => _collection;
        init => _collection = StorageContractNormalization.NormalizeOptional(value);
    }

    public required string Key { get; init; }
    public bool? IncludeExpired { get; init; }

    public string? CorrelationId
    {
        get => _correlationId;
        init => _correlationId = StorageContractNormalization.NormalizeOptional(value);
    }
}
