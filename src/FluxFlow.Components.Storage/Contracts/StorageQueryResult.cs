namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageQueryResult
{
    private string _operation = string.Empty;
    private string _collection = string.Empty;
    private IReadOnlyList<StorageRecord> _records = [];
    private string? _correlationId;
    private string? _message;
    private Dictionary<string, string> _attributes = new(StringComparer.Ordinal);

    public required DateTimeOffset Timestamp { get; init; }

    public required string Operation
    {
        get => _operation;
        init => _operation = StorageContractNormalization.NormalizeRequired(value);
    }

    public required string Collection
    {
        get => _collection;
        init => _collection = StorageContractNormalization.NormalizeRequired(value);
    }

    public required bool Succeeded { get; init; }
    public required int Count { get; init; }

    public IReadOnlyList<StorageRecord> Records
    {
        get => _records;
        init => _records = StorageContractNormalization.CopyRecords(value);
    }

    public string? CorrelationId
    {
        get => _correlationId;
        init => _correlationId = StorageContractNormalization.NormalizeOptional(value);
    }

    public string? Message
    {
        get => _message;
        init => _message = StorageContractNormalization.NormalizeOptional(value);
    }

    public Dictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = StorageContractNormalization.CopyAttributes(value);
    }
}
