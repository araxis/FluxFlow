namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageResult
{
    private string _operation = string.Empty;
    private string _collection = string.Empty;
    private string _key = string.Empty;
    private StorageRecord? _record;
    private string? _message;
    private string? _correlationId;
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

    public required string Key
    {
        get => _key;
        init => _key = StorageContractNormalization.NormalizeRequired(value);
    }

    public required bool Succeeded { get; init; }
    public bool Found { get; init; }
    public bool Deleted { get; init; }

    public StorageRecord? Record
    {
        get => _record;
        init => _record = StorageContractNormalization.CopyRecord(value);
    }

    public long? Version { get; init; }

    public string? Message
    {
        get => _message;
        init => _message = StorageContractNormalization.NormalizeOptional(value);
    }

    public string? CorrelationId
    {
        get => _correlationId;
        init => _correlationId = StorageContractNormalization.NormalizeOptional(value);
    }

    public Dictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = StorageContractNormalization.CopyAttributes(value);
    }
}
