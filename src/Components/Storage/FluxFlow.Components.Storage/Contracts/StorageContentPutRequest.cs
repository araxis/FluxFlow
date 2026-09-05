using FluxFlow.Data;

namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageContentPutRequest
{
    private string? _collection;
    private string _key = string.Empty;
    private FlowContent? _content;
    private IReadOnlyDictionary<string, string> _attributes =
        StorageContentContractMap.CopyAttributes(null);

    public string? Collection
    {
        get => _collection;
        init => _collection = StorageContentContractMap.NormalizeOptional(value);
    }

    public required string Key
    {
        get => _key;
        init => _key = value;
    }

    public required FlowContent Content
    {
        get => _content!;
        init => _content = value;
    }

    public IReadOnlyDictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = StorageContentContractMap.CopyAttributes(value);
    }

    public long? ExpectedVersion { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public StorageWriteMode? Mode { get; init; }
}
