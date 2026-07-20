using FluxFlow.Data;

namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageContentRecord
{
    private string _collection = string.Empty;
    private string _key = string.Empty;
    private FlowContent? _content;
    private IReadOnlyDictionary<string, string> _attributes =
        StorageContentContractMap.CopyAttributes(null);
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

    public required FlowContent Content
    {
        get => _content!;
        init => _content = StorageContentContractMap.CopyContent(value);
    }

    public IReadOnlyDictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = StorageContentContractMap.CopyAttributes(value);
    }

    public long Version { get; init; }

    public DateTimeOffset StoredAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? CorrelationId
    {
        get => _correlationId;
        init => _correlationId = StorageContentContractMap.NormalizeOptional(value);
    }
}
